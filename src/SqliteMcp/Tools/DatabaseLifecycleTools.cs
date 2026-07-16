using System.ComponentModel;
using ModelContextProtocol.Server;
using SqliteMcp.Hooks;
using SqliteMcp.Json;
using SqliteMcp.Sql;

namespace SqliteMcp.Tools;

/// <summary>
/// MCP tools for opening, closing, and inspecting SQLite connections.
/// </summary>
[McpServerToolType]
public sealed class DatabaseLifecycleTools(SqliteConnectionManager connections, ICliHookRunner hooks)
{
    private readonly Lock _closeAllGate = new();
    /// <summary>Opens a DB under a connection key (or the default slot).</summary>
    [McpServerTool(Name = "open_db"), Description(
        "Open a SQLite database and register it under a connection key. " +
        "Omit connectionKey to open/bind the default slot. " +
        "If the key already exists with the same path, reuses the connection. " +
        "If the key exists with a different path, returns an error.")]
    public string OpenDb(
        [Description("Path to the SQLite database file")] string path,
        [Description("Connection key for this database. Omit to use 'default'.")] string? connectionKey = null)
    {
        var key = string.IsNullOrWhiteSpace(connectionKey)
            ? SqliteConnectionManager.DefaultKey
            : connectionKey.Trim();
        var normalizedPath = SqliteConnectionManager.NormalizePath(path);
        var context = new HookContext
        {
            ConnectionKey = key,
            DbPath = normalizedPath
        };

        // Pair Before/After on whether Before ran, not on WasReused: a race can make Open
        // reuse after Before already executed; After must still run to keep the lifecycle paired.
        var beforeRan = false;
        var openResult = connections.Open(path, connectionKey, beforeCreate: () =>
        {
            beforeRan = true;
            hooks.Run(HookEventKind.Open, HookPhase.Before, context);
        });
        if (beforeRan)
        {
            hooks.Run(HookEventKind.Open, HookPhase.After, context);
        }

        return SqliteCommandRunner.ToJson(
            new OpenDbResult(
                "Database opened.",
                openResult.Entry.Key,
                openResult.Entry.Path,
                openResult.Entry.Key == SqliteConnectionManager.DefaultKey),
            AppJsonContext.Default.OpenDbResult);
    }

    /// <summary>Closes one connection and releases its file lock.</summary>
    [McpServerTool(Name = "close_db"), Description(
        "Close and release one SQLite connection (unlocks the database file). " +
        "Omit connectionKey to close the default connection.")]
    public string CloseDb(
        [Description("Connection key to close. Omit to close 'default'.")] string? connectionKey = null)
    {
        if (!connections.TryGetOpenConnectionPath(connectionKey, out var key, out var path))
        {
            return SqliteCommandRunner.ToJson(
                connections.Close(connectionKey),
                AppJsonContext.Default.CloseDbResult);
        }

        var context = new HookContext
        {
            ConnectionKey = key,
            DbPath = path
        };

        hooks.Run(HookEventKind.Close, HookPhase.Before, context);
        var result = connections.Close(connectionKey);
        hooks.Run(HookEventKind.Close, HookPhase.After, context);

        return SqliteCommandRunner.ToJson(result, AppJsonContext.Default.CloseDbResult);
    }

    /// <summary>Closes every open connection. Destructive for concurrent keyed work.</summary>
    [McpServerTool(Name = "close_all"), Description(
        "WARNING: Closes EVERY open database connection and releases all file locks. " +
        "Configured default path is kept for later lazy reopen. Use close_db to close a single connection.")]
    public string CloseAll()
    {
        using (_closeAllGate.EnterScope())
        {
            var snapshot = connections.ListConnections().ToList();
            var closedKeys = string.Join(',', snapshot.Select(c => c.ConnectionKey));
            var closeAllContext = new HookContext { ClosedKeys = closedKeys };

            hooks.Run(HookEventKind.CloseAll, HookPhase.Before, closeAllContext);

            var closed = new List<ClosedConnectionInfo>();
            foreach (var connection in snapshot)
            {
                if (!connections.TryGetOpenConnectionPath(connection.ConnectionKey, out var key, out var path))
                {
                    continue;
                }

                var closeContext = new HookContext
                {
                    ConnectionKey = key,
                    DbPath = path,
                    ClosedKeys = closedKeys
                };

                hooks.Run(HookEventKind.Close, HookPhase.Before, closeContext);
                if (connections.TryClose(connection.ConnectionKey, out var result))
                {
                    closed.Add(new ClosedConnectionInfo(result.ConnectionKey, result.Path));
                }

                hooks.Run(HookEventKind.Close, HookPhase.After, closeContext);
            }

            hooks.Run(HookEventKind.CloseAll, HookPhase.After, closeAllContext);

            return SqliteCommandRunner.ToJson(
                new CloseAllResult(
                    $"Closed {closed.Count} connection(s). All file locks released.",
                    closed),
                AppJsonContext.Default.CloseAllResult);
        }
    }

    /// <summary>Lists open keys, paths, and configured default path.</summary>
    [McpServerTool(Name = "list_connections"), Description(
        "List all currently open database connections (keys, paths, and which is default).")]
    public string ListConnections()
    {
        return SqliteCommandRunner.ToJson(
            new ListConnectionsResult(connections.DefaultPath, connections.ListConnections()),
            AppJsonContext.Default.ListConnectionsResult);
    }

    /// <summary>Returns path, size, and table count for a connection.</summary>
    [McpServerTool(Name = "db_info"), Description(
        "Get information about a SQLite database including path, existence, size, and table count.")]
    public string DbInfo(
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var dbPath = entry.Path;
        var exists = File.Exists(dbPath);
        long size = 0;
        DateTimeOffset? lastModified = null;
        if (exists)
        {
            var info = new FileInfo(dbPath);
            size = info.Length;
            lastModified = info.LastWriteTimeUtc;
        }

        var tableCountRows = SqliteCommandRunner.Query(
            entry.Connection,
            """
            SELECT count(*) AS count FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            """);

        var tableCount = tableCountRows[0]?["count"]?.GetValue<long>() ?? 0;

        return SqliteCommandRunner.ToJson(
            new DbInfoResult(entry.Key, dbPath, exists, size, lastModified, tableCount, IsOpen: true),
            AppJsonContext.Default.DbInfoResult);
    }
}
