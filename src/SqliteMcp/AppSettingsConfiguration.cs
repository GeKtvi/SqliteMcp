using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;

namespace SqliteMcp;

/// <summary>
/// Replaces the host's default appsettings JSON sources with a single resolved file.
/// </summary>
internal static class AppSettingsConfiguration
{
    public static void Apply(HostApplicationBuilder builder, AppSettingsPathResult settings)
    {
        for (var i = builder.Configuration.Sources.Count - 1; i >= 0; i--)
        {
            if (builder.Configuration.Sources[i] is JsonConfigurationSource { Path: { } path }
                && path.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            {
                builder.Configuration.Sources.RemoveAt(i);
            }
        }

        builder.Configuration.AddJsonFile(
            settings.Path,
            optional: !settings.IsExplicit,
            reloadOnChange: true);
    }
}
