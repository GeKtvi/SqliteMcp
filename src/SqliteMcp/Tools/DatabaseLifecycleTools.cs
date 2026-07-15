using System.ComponentModel;
using ModelContextProtocol.Server;
using SqliteMcp.Sql;

namespace SqliteMcp.Tools;

/// <summary>
/// MCP tools for opening, closing, and inspecting SQLite connections.
/// </summary>
[McpServerToolType]
public sealed class DatabaseLifecycleTools(SqliteConnectionManager connections)
{
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
        var entry = connections.Open(path, connectionKey);
        return SqliteCommandRunner.ToJson(new
        {
            message = "Database opened.",
            connectionKey = entry.Key,
            path = entry.Path,
            isDefault = entry.Key == SqliteConnectionManager.DefaultKey
        });
    }

    /// <summary>Closes one connection and releases its file lock.</summary>
    [McpServerTool(Name = "close_db"), Description(
        "Close and release one SQLite connection (unlocks the database file). " +
        "Omit connectionKey to close the default connection.")]
    public string CloseDb(
        [Description("Connection key to close. Omit to close 'default'.")] string? connectionKey = null)
    {
        return SqliteCommandRunner.ToJson(connections.Close(connectionKey));
    }

    /// <summary>Closes every open connection. Destructive for concurrent keyed work.</summary>
    [McpServerTool(Name = "close_all"), Description(
        "WARNING: Closes EVERY open database connection and releases all file locks. " +
        "Configured default path is kept for later lazy reopen. Use close_db to close a single connection.")]
    public string CloseAll()
    {
        return SqliteCommandRunner.ToJson(connections.CloseAll());
    }

    /// <summary>Lists open keys, paths, and configured default path.</summary>
    [McpServerTool(Name = "list_connections"), Description(
        "List all currently open database connections (keys, paths, and which is default).")]
    public string ListConnections()
    {
        return SqliteCommandRunner.ToJson(new
        {
            defaultPath = connections.DefaultPath,
            connections = connections.ListConnections()
        });
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

        var tableCount = SqliteCommandRunner.Query(
            entry.Connection,
            """
            SELECT count(*) AS count FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            """);

        return SqliteCommandRunner.ToJson(new
        {
            connectionKey = entry.Key,
            dbPath,
            exists,
            size,
            lastModified,
            tableCount = Convert.ToInt64(tableCount[0]["count"]),
            isOpen = true
        });
    }
}
