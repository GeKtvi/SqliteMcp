using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqliteMcp;
using SqliteMcp.Hooks;
using SqliteMcp.Json;
using SqliteMcp.Logging;
using SqliteMcp.Tools;

var settingsPath = AppSettingsPathResolver.Resolve(args);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

AppSettingsConfiguration.Apply(builder, settingsPath);

// MCP uses stdout for JSON-RPC; keep all logs on stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var logFilePath = FileLoggingSetup.AddFileLogging(builder.Logging, builder.Configuration);

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

var host = builder.Build();

var appLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SqliteMcp");
if (appLogger.IsEnabled(LogLevel.Information))
{
    appLogger.LogInformation("Using configuration: {ConfigPath}", settingsPath.Path);
    appLogger.LogInformation("File logging to {LogFilePath}", logFilePath);
    if (!string.IsNullOrWhiteSpace(defaultDbPath))
    {
        appLogger.LogInformation("Default DB path configured: {DefaultDbPath}", defaultDbPath);
    }
}

host.Services.GetRequiredService<IOptionsMonitor<HookOptions>>()
    .OnChange(_ =>
    {
        if (appLogger.IsEnabled(LogLevel.Information))
        {
            appLogger.LogInformation("Hooks configuration reloaded.");
        }
    });

await host.RunAsync();
