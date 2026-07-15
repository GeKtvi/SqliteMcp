using Microsoft.Extensions.Configuration;
using SqliteMcp;

namespace SqliteMcp.Tests.Connection;

public class DefaultDbPathResolverTests
{
    [Test]
    [Arguments(new string[] { "--default-db", "C:/data/app.db" }, "C:/data/app.db")]
    [Arguments(new string[] { "--default-db=C:/data/app.db" }, "C:/data/app.db")]
    public async Task Resolve_FromDefaultDbFlag(string[] args, string expected)
    {
        var config = new ConfigurationBuilder().Build();
        var result = DefaultDbPathResolver.Resolve(config, args);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Resolve_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultDbPath"] = "C:/config/app.db"
            })
            .Build();

        var result = DefaultDbPathResolver.Resolve(config, []);
        await Assert.That(result).IsEqualTo("C:/config/app.db");
    }

    [Test]
    public async Task Resolve_ArgsTakePrecedenceOverConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DefaultDbPath"] = "C:/config/app.db"
            })
            .Build();

        var result = DefaultDbPathResolver.Resolve(config, ["--default-db", "C:/cli/app.db"]);
        await Assert.That(result).IsEqualTo("C:/cli/app.db");
    }

    [Test]
    [Arguments("--verbose")]
    public async Task Resolve_ReturnsNullWhenUnconfigured(string arg)
    {
        var config = new ConfigurationBuilder().Build();
        var result = DefaultDbPathResolver.Resolve(config, [arg]);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Resolve_IgnoresPositionalPathWithoutFlag()
    {
        var config = new ConfigurationBuilder().Build();
        var result = DefaultDbPathResolver.Resolve(config, ["C:/data/app.db"]);
        await Assert.That(result).IsNull();
    }
}
