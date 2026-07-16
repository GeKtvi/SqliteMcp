namespace SqliteMcp.Tests.Connection;

public class AppSettingsPathResolverTests
{
    [Test]
    public async Task Resolve_WithoutFlag_UsesBaseDirectoryAppsettings()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "sqlite-mcp-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var result = AppSettingsPathResolver.Resolve([], baseDirectory: baseDir, currentDirectory: Path.GetTempPath());
            await Assert.That(result.IsExplicit).IsFalse();
            await Assert.That(result.Path).IsEqualTo(Path.GetFullPath(Path.Combine(baseDir, "appsettings.json")));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Test]
    public async Task Resolve_AbsoluteConfigFlag_UsesThatPath()
    {
        var configFile = Path.Combine(Path.GetTempPath(), "sqlite-mcp-abs-" + Guid.NewGuid().ToString("N") + ".json");
        var result = AppSettingsPathResolver.Resolve(
            ["--config", configFile],
            baseDirectory: Path.GetTempPath(),
            currentDirectory: Path.GetTempPath());

        await Assert.That(result.IsExplicit).IsTrue();
        await Assert.That(result.Path).IsEqualTo(Path.GetFullPath(configFile));
    }

    [Test]
    public async Task Resolve_ConfigEqualsForm_UsesThatPath()
    {
        var configFile = Path.Combine(Path.GetTempPath(), "sqlite-mcp-eq-" + Guid.NewGuid().ToString("N") + ".json");
        var result = AppSettingsPathResolver.Resolve(
            [$"--config={configFile}"],
            baseDirectory: Path.GetTempPath(),
            currentDirectory: Path.GetTempPath());

        await Assert.That(result.IsExplicit).IsTrue();
        await Assert.That(result.Path).IsEqualTo(Path.GetFullPath(configFile));
    }

    [Test]
    public async Task Resolve_RelativeConfigFlag_UsesCurrentDirectory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "sqlite-mcp-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cwd);
        try
        {
            var result = AppSettingsPathResolver.Resolve(
                ["--config", "appsettings.json"],
                baseDirectory: Path.GetTempPath(),
                currentDirectory: cwd);

            await Assert.That(result.IsExplicit).IsTrue();
            await Assert.That(result.Path).IsEqualTo(Path.GetFullPath(Path.Combine(cwd, "appsettings.json")));
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
    public async Task Resolve_RelativeNestedPath_UsesCurrentDirectory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "sqlite-mcp-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cwd);
        try
        {
            var result = AppSettingsPathResolver.Resolve(
                ["--config", Path.Combine("config", "hooks.json")],
                baseDirectory: Path.GetTempPath(),
                currentDirectory: cwd);

            await Assert.That(result.Path)
                .IsEqualTo(Path.GetFullPath(Path.Combine(cwd, "config", "hooks.json")));
        }
        finally
        {
            Directory.Delete(cwd, recursive: true);
        }
    }

    [Test]
#pragma warning disable CDT1003 // TUnit fluent assertions must be awaited; they are not Task until awaited
    public async Task Resolve_EmptyConfigValue_Throws()
    {
        await Assert.That(() => AppSettingsPathResolver.Resolve(
                ["--config", ""],
                baseDirectory: Path.GetTempPath(),
                currentDirectory: Path.GetTempPath()))
            .Throws<ArgumentException>()
            .WithMessageContaining("--config");
    }
#pragma warning restore CDT1003

    [Test]
    public async Task Resolve_ConfigTakesPrecedenceOverDefaultLocation()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "sqlite-mcp-base-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(Path.GetTempPath(), "sqlite-mcp-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(cwd);
        try
        {
            var result = AppSettingsPathResolver.Resolve(
                ["--default-db", "x.db", "--config", "project.json"],
                baseDirectory: baseDir,
                currentDirectory: cwd);

            await Assert.That(result.IsExplicit).IsTrue();
            await Assert.That(result.Path).IsEqualTo(Path.GetFullPath(Path.Combine(cwd, "project.json")));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
            Directory.Delete(cwd, recursive: true);
        }
    }
}
