using Microsoft.Extensions.Configuration;

namespace SqliteMcp;

/// <summary>
/// Resolves the default database path from CLI arguments or configuration.
/// </summary>
public static class DefaultDbPathResolver
{
    private const string FlagName = "--default-db";
    private const string FlagPrefix = "--default-db=";

    /// <summary>
    /// Resolves default DB path from <c>--default-db</c> or <c>DefaultDbPath</c> config.
    /// </summary>
    public static string? Resolve(IConfiguration configuration, string[] args)
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

        var fromConfig = configuration["DefaultDbPath"];
        return string.IsNullOrWhiteSpace(fromConfig) ? null : fromConfig;
    }
}
