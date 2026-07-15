using System.Text.Json;
using SqliteMcp;
using SqliteMcp.Tools;

namespace SqliteMcp.Tests.Infrastructure;

/// <summary>
/// Wires all MCP tool types to a shared connection manager for integration tests.
/// </summary>
public sealed class McpToolHarness : IDisposable
{
    private readonly string _tempDirectory;
    private bool _disposed;

    private McpToolHarness(SqliteConnectionManager connections, string tempDirectory)
    {
        Connections = connections;
        _tempDirectory = tempDirectory;
        Lifecycle = new DatabaseLifecycleTools(connections);
        Query = new QueryTools(connections);
        Schema = new SchemaTools(connections);
        Crud = new CrudTools(connections);
    }

    public SqliteConnectionManager Connections { get; }

    public DatabaseLifecycleTools Lifecycle { get; }

    public QueryTools Query { get; }

    public SchemaTools Schema { get; }

    public CrudTools Crud { get; }

    public static McpToolHarness CreateWithDefaultPath(string? dbFileName = null)
    {
        var tempDirectory = CreateTempDirectory();
        var dbPath = Path.Combine(tempDirectory, dbFileName ?? "default.db");
        var connections = new SqliteConnectionManager();
        connections.SetDefaultPath(dbPath);
        return new McpToolHarness(connections, tempDirectory);
    }

    public static McpToolHarness CreateEmpty()
    {
        var tempDirectory = CreateTempDirectory();
        return new McpToolHarness(new SqliteConnectionManager(), tempDirectory);
    }

    public string DbPath(string fileName = "default.db") => Path.Combine(_tempDirectory, fileName);

    public string TempDirectory => _tempDirectory;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Connections.Dispose();

        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup; file lock tests may still hold handles briefly.
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sqlite-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
