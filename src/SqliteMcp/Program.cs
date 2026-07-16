using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqliteMcp;
using SqliteMcp.Hooks;
using SqliteMcp.Json;
using SqliteMcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

// MCP uses stdout for JSON-RPC; keep all logs on stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var defaultDbPath = DefaultDbPathResolver.Resolve(builder.Configuration, args);

builder.Services.Configure<HookOptions>(builder.Configuration.GetSection("Hooks"));
builder.Services.AddSingleton<ICliHookRunner, CliHookRunner>();

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

var jsonOptions = AppJsonContext.Default.Options;

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DatabaseLifecycleTools>(jsonOptions)
    .WithTools<SchemaTools>(jsonOptions)
    .WithTools<QueryTools>(jsonOptions)
    .WithTools<CrudTools>(jsonOptions);

await builder.Build().RunAsync();
