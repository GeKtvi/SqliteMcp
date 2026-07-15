using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqliteMcp;

HostApplicationBuilder? builder = Host.CreateApplicationBuilder(args);

// MCP uses stdout for JSON-RPC; keep all logs on stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

string? defaultDbPath = DefaultDbPathResolver.Resolve(builder.Configuration, args);

builder.Services.AddSingleton(_ =>
{
    var connections = new SqliteConnectionManager();
    if (!string.IsNullOrWhiteSpace(defaultDbPath))
    {
        // Store only — connection opens lazily on first tool use.
        connections.SetDefaultPath(defaultDbPath);
    }

    return connections;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
