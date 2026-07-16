using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;

namespace SqliteMcp.Logging;

internal static class FileLoggingSetup
{
    private const string DefaultRelativePath = "logs/sqlite-mcp.log";

    /// <summary>
    /// Registers NReco file logging under <see cref="AppContext.BaseDirectory"/>.
    /// Returns the effective log file path (PID-suffixed if the default file could not be opened).
    /// </summary>
    public static string AddFileLogging(ILoggingBuilder logging, IConfiguration configuration)
    {
        var fileSection = configuration.GetSection("Logging:File");
        var configuredPath = fileSection["Path"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = DefaultRelativePath;
        }

        var absolutePath = ResolvePath(configuredPath);
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var effectivePath = absolutePath;

        logging.AddFile(absolutePath, options =>
        {
            fileSection.Bind(options);
            options.HandleFileError = err =>
            {
                var dir = Path.GetDirectoryName(err.LogFileName);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Path.GetDirectoryName(absolutePath) ?? AppContext.BaseDirectory;
                }

                var baseName = Path.GetFileNameWithoutExtension(absolutePath);
                var extension = Path.GetExtension(absolutePath);
                var pidPath = Path.Combine(dir, $"{baseName}-{Environment.ProcessId}{extension}");
                effectivePath = pidPath;
                err.UseNewLogFileName(pidPath);
                Console.Error.WriteLine(
                    $"SqliteMcp: log file unavailable ({err.LogFileName}); using {pidPath}");
            };
        });

        return effectivePath;
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
