using Microsoft.Data.Sqlite;

namespace SqliteMcp;

/// <summary>
/// One open SQLite connection registered under a dictionary key.
/// </summary>
public sealed class ConnectionEntry : IDisposable
{
    /// <summary>Agent-facing connection key (e.g. <c>default</c> or a custom name).</summary>
    public required string Key { get; init; }

    /// <summary>Absolute path to the database file.</summary>
    public required string Path { get; init; }

    /// <summary>Live connection; disposed when the entry is closed.</summary>
    public required SqliteConnection Connection { get; init; }

    /// <summary>Releases the underlying SQLite connection (and its file lock).</summary>
    public void Dispose()
    {
        Connection.Dispose();
    }
}
