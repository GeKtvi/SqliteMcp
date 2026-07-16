namespace SqliteMcp;

/// <summary>
/// Resolves which <c>appsettings</c> JSON file to load: exe-directory default, or <c>--config</c>.
/// </summary>
public static class AppSettingsPathResolver
{
    private const string FlagName = "--config";
    private const string FlagPrefix = "--config=";
    private const string DefaultFileName = "appsettings.json";

    /// <summary>
    /// Resolves the absolute settings path.
    /// No <c>--config</c> → <c>{baseDirectory}/appsettings.json</c>.
    /// With <c>--config</c> → that path (relative paths use <paramref name="currentDirectory"/>).
    /// </summary>
    public static AppSettingsPathResult Resolve(
        string[] args,
        string? baseDirectory = null,
        string? currentDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        currentDirectory ??= Directory.GetCurrentDirectory();

        var fromArgs = TryGetConfigArg(args);
        if (fromArgs is not null)
        {
            if (string.IsNullOrWhiteSpace(fromArgs))
            {
                throw new ArgumentException("The --config argument must not be empty.", nameof(args));
            }

            var absolute = Path.IsPathRooted(fromArgs)
                ? Path.GetFullPath(fromArgs)
                : Path.GetFullPath(Path.Combine(currentDirectory, fromArgs));

            return new AppSettingsPathResult(absolute, IsExplicit: true);
        }

        var defaultPath = Path.GetFullPath(Path.Combine(baseDirectory, DefaultFileName));
        return new AppSettingsPathResult(defaultPath, IsExplicit: false);
    }

    private static string? TryGetConfigArg(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is FlagName && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (arg.StartsWith(FlagPrefix, StringComparison.Ordinal))
            {
                return arg[FlagPrefix.Length..];
            }
        }

        return null;
    }
}

/// <summary>
/// Absolute settings path (<see cref="Path"/>) and whether it came from <c>--config</c> (<see cref="IsExplicit"/>).
/// </summary>
public readonly record struct AppSettingsPathResult(string Path, bool IsExplicit);
