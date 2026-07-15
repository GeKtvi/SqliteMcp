using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

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
    private readonly object _gate = new();
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
    public ConnectionEntry Open(string path, string? connectionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var key = string.IsNullOrWhiteSpace(connectionKey) ? DefaultKey : connectionKey.Trim();
        var normalizedPath = NormalizePath(path);

        lock (_gate)
        {
            if (_connections.TryGetValue(key, out var existing))
            {
                if (!PathsEqual(existing.Path, normalizedPath))
                {
                    throw new InvalidOperationException(
                        $"Connection key '{key}' is already open for '{existing.Path}'. " +
                        $"Cannot open '{normalizedPath}' with the same key. Close it first or use a different key.");
                }

                return existing;
            }

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

            return entry;
        }
    }

    /// <summary>
    /// Resolves a connection for tool use.
    /// With a key: requires an already-open entry.
    /// Without a key: returns/lazily opens the default connection.
    /// </summary>
    public ConnectionEntry GetConnection(string? connectionKey = null)
    {
        lock (_gate)
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

            return Open(_defaultPath, DefaultKey);
        }
    }

    /// <summary>
    /// Disposes one connection and removes it from the dictionary, unlocking the file.
    /// Omitting the key targets the default slot. Configured <see cref="DefaultPath"/> is kept.
    /// </summary>
    public object Close(string? connectionKey = null)
    {
        lock (_gate)
        {
            string key;
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
                throw new InvalidOperationException(
                    "No connectionKey provided and no default database is configured.");
            }

            if (!_connections.TryRemove(key, out var entry))
            {
                throw new InvalidOperationException(
                    $"No open connection for key '{key}'.");
            }

            entry.Dispose();
            return new
            {
                message = $"Closed connection '{key}'.",
                connectionKey = key,
                path = entry.Path
            };
        }
    }

    /// <summary>
    /// Disposes every open connection. Does not clear <see cref="DefaultPath"/>.
    /// </summary>
    public object CloseAll()
    {
        lock (_gate)
        {
            var closed = new List<object>();
            foreach (var key in _connections.Keys.ToArray())
            {
                if (_connections.TryRemove(key, out var entry))
                {
                    closed.Add(new { connectionKey = key, path = entry.Path });
                    entry.Dispose();
                }
            }

            return new
            {
                message = $"Closed {closed.Count} connection(s). All file locks released.",
                closed
            };
        }
    }

    /// <summary>
    /// Returns open connection keys and paths for agent observability.
    /// </summary>
    public IReadOnlyList<object> ListConnections()
    {
        return _connections.Values
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => (object)new
            {
                connectionKey = e.Key,
                path = e.Path,
                isDefault = e.Key == DefaultKey
            })
            .ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CloseAll();
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
}
