using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using SqliteMcp.Json;

namespace SqliteMcp;

/// <summary>
/// Thread-safe registry of open SQLite connections keyed by agent-provided names.
/// Supports a configured default path that is opened lazily on first unkeyed access.
/// </summary>
public sealed class SqliteConnectionManager : IDisposable
{
    /// <summary>Dictionary key used for the default database slot.</summary>
    public const string DefaultKey = "default";

    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private string? _defaultPath;

    /// <summary>
    /// Absolute path configured as the default DB (may be set without an open connection).
    /// </summary>
    public string? DefaultPath => _defaultPath;

    /// <summary>
    /// Stores the default database path without opening a connection.
    /// Pass null or whitespace to clear.
    /// </summary>
    public void SetDefaultPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _defaultPath = null;
            return;
        }

        _defaultPath = NormalizePath(path);
    }

    /// <summary>
    /// Opens a database under <paramref name="connectionKey"/> (or <see cref="DefaultKey"/> if omitted).
    /// Reuses an existing entry when the key and path match; throws if the key is bound to a different path.
    /// </summary>
    /// <param name="beforeCreate">
    /// Invoked only when this call is about to create a new connection (outside the connection lock).
    /// If another thread opens the same key between the pre-check and create, this may still have run;
    /// callers that run a matching After when this fired keep Before/After paired.
    /// </param>
    public OpenResult Open(string path, string? connectionKey = null, Action? beforeCreate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using (_gate.EnterScope())
        {
            if (TryGetReuseOrThrow(path, connectionKey, out var reused))
            {
                return reused;
            }
        }

        beforeCreate?.Invoke();

        using (_gate.EnterScope())
        {
            return OpenCore(path, connectionKey);
        }
    }

    /// <summary>
    /// Resolves a connection for tool use.
    /// With a key: requires an already-open entry.
    /// Without a key: returns/lazily opens the default connection.
    /// </summary>
    public ConnectionEntry GetConnection(string? connectionKey = null)
    {
        using (_gate.EnterScope())
        {
            if (!string.IsNullOrWhiteSpace(connectionKey))
            {
                var key = connectionKey.Trim();
                if (!_connections.TryGetValue(key, out var entry))
                {
                    throw new InvalidOperationException(
                        $"No open connection for key '{key}'. Call open_db first.");
                }

                return entry;
            }

            if (_connections.TryGetValue(DefaultKey, out var defaultEntry))
            {
                return defaultEntry;
            }

            if (_defaultPath is null)
            {
                throw new InvalidOperationException(
                    "No default database is configured and no connectionKey was provided. " +
                    "Pass --default-db / DefaultDbPath at startup, or call open_db with a path and connectionKey.");
            }

            return OpenCore(_defaultPath, DefaultKey).Entry;
        }
    }

    /// <summary>
    /// Resolves an already-open connection for close hooks without lazy-opening the default.
    /// </summary>
    internal bool TryGetOpenConnectionPath(
        string? connectionKey,
        out string key,
        out string path)
    {
        using (_gate.EnterScope())
        {
            if (!string.IsNullOrWhiteSpace(connectionKey))
            {
                key = connectionKey.Trim();
            }
            else if (_connections.ContainsKey(DefaultKey) || _defaultPath is not null)
            {
                key = DefaultKey;
            }
            else
            {
                key = DefaultKey;
                path = "";
                return false;
            }

            if (!_connections.TryGetValue(key, out var entry))
            {
                path = "";
                return false;
            }

            path = entry.Path;
            return true;
        }
    }

    /// <summary>
    /// Disposes one connection and removes it from the dictionary, unlocking the file.
    /// Omitting the key targets the default slot. Configured <see cref="DefaultPath"/> is kept.
    /// </summary>
    public CloseDbResult Close(string? connectionKey = null)
    {
        if (!TryClose(connectionKey, out var result))
        {
            throw CreateNotOpenException(connectionKey);
        }

        return result;
    }

    /// <summary>
    /// Closes a connection when it is still open. Returns false without throwing when the key is absent.
    /// </summary>
    public bool TryClose(string? connectionKey, out CloseDbResult result)
    {
        using (_gate.EnterScope())
        {
            if (!TryResolveCloseKey(connectionKey, out var key, out _))
            {
                result = default!;
                return false;
            }

            if (!_connections.TryRemove(key, out var entry))
            {
                result = default!;
                return false;
            }

            entry.Dispose();
            result = new CloseDbResult(
                $"Closed connection '{key}'.",
                key,
                entry.Path);
            return true;
        }
    }

    /// <summary>
    /// Disposes every open connection. Does not clear <see cref="DefaultPath"/>.
    /// </summary>
    public CloseAllResult CloseAll()
    {
        using (_gate.EnterScope())
        {
            List<ClosedConnectionInfo> closed = [];
            foreach (var key in (string[])[.. _connections.Keys])
            {
                if (_connections.TryRemove(key, out var entry))
                {
                    closed.Add(new ClosedConnectionInfo(key, entry.Path));
                    entry.Dispose();
                }
            }

            return new CloseAllResult(
                $"Closed {closed.Count} connection(s). All file locks released.",
                closed);
        }
    }

    /// <summary>
    /// Returns open connection keys and paths for agent observability.
    /// </summary>
    public IReadOnlyList<ConnectionInfo> ListConnections()
    {
        using (_gate.EnterScope())
        {
            return [.. _connections.Values
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new ConnectionInfo(e.Key, e.Path, e.Key == DefaultKey))];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CloseAll();
    }

    private bool TryGetReuseOrThrow(string path, string? connectionKey, out OpenResult reused)
    {
        var key = string.IsNullOrWhiteSpace(connectionKey) ? DefaultKey : connectionKey.Trim();
        var normalizedPath = NormalizePath(path);

        if (!_connections.TryGetValue(key, out var existing))
        {
            reused = default;
            return false;
        }

        if (!PathsEqual(existing.Path, normalizedPath))
        {
            throw new InvalidOperationException(
                $"Connection key '{key}' is already open for '{existing.Path}'. " +
                $"Cannot open '{normalizedPath}' with the same key. Close it first or use a different key.");
        }

        reused = new OpenResult(existing, WasReused: true);
        return true;
    }

    private OpenResult OpenCore(string path, string? connectionKey)
    {
        if (TryGetReuseOrThrow(path, connectionKey, out var reused))
        {
            return reused;
        }

        var key = string.IsNullOrWhiteSpace(connectionKey) ? DefaultKey : connectionKey.Trim();
        var normalizedPath = NormalizePath(path);

        var connection = OpenConnection(normalizedPath);
        var entry = new ConnectionEntry
        {
            Key = key,
            Path = normalizedPath,
            Connection = connection
        };

        if (!_connections.TryAdd(key, entry))
        {
            connection.Dispose();
            throw new InvalidOperationException($"Failed to register connection key '{key}'.");
        }

        if (key == DefaultKey)
        {
            _defaultPath = normalizedPath;
        }

        return new OpenResult(entry, WasReused: false);
    }

    /// <summary>
    /// Opens a non-pooled connection so Dispose fully releases the Windows file lock.
    /// </summary>
    private static SqliteConnection OpenConnection(string absolutePath)
    {
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = absolutePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>Resolves a path to an absolute full path.</summary>
    internal static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private bool TryResolveCloseKey(string? connectionKey, out string key, out bool missingConfiguredDefault)
    {
        if (!string.IsNullOrWhiteSpace(connectionKey))
        {
            key = connectionKey.Trim();
            missingConfiguredDefault = false;
            return true;
        }

        if (_connections.ContainsKey(DefaultKey) || _defaultPath is not null)
        {
            key = DefaultKey;
            missingConfiguredDefault = false;
            return true;
        }

        key = DefaultKey;
        missingConfiguredDefault = true;
        return false;
    }

    private InvalidOperationException CreateNotOpenException(string? connectionKey)
    {
        using (_gate.EnterScope())
        {
            if (!TryResolveCloseKey(connectionKey, out var key, out var missingConfiguredDefault))
            {
                return new InvalidOperationException(
                    "No connectionKey provided and no default database is configured.");
            }

            _ = missingConfiguredDefault;
            return new InvalidOperationException(
                $"No open connection for key '{key}'.");
        }
    }
}
