using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqliteMcp.Tests.Infrastructure;
using TUnit.Core.Enums;

namespace SqliteMcp.Tests.Scenarios;

public class CliHookEdgeCaseTests
{
    private static CliHookRunner CreateRunner(HookOptions options) =>
        new(new TestOptionsMonitor<HookOptions>(options), NullLogger<CliHookRunner>.Instance);

    // --- Critical: hang / MCP I/O ---

    [Test]
    public async Task CliHookRunner_LargeStdout_CompletesWithoutHang()
    {
        var runner = CreateRunner(new HookOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            Query = new HookEventOptions { Before = HookTestCommands.FloodStdout(10_000) }
        });
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var queryTask = Task.Run(() => harness.Query.Query("SELECT 1"));
        var json = await queryTask.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(json).Contains("1");
    }

    [Test]
    public async Task CliHookRunner_ChildStdout_DoesNotBreakToolJson()
    {
        var runner = CreateRunner(new HookOptions
        {
            Query = new HookEventOptions
            {
                Before = OperatingSystem.IsWindows()
                    ? "echo leaked-to-child-stdout"
                    : "echo leaked-to-child-stdout"
            }
        });
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var json = harness.Query.Query("SELECT 1");
        var root = JsonTestExtensions.ParseJson(json).RootElement;

        await Assert.That(root.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Array);
        await Assert.That(json).DoesNotContain("leaked-to-child-stdout");
    }

    [Test]
    public async Task OpenDb_FailsAfterOpenBefore_RunsBeforeOnly()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dirPath = harness.DbPath("is-a-directory");
        Directory.CreateDirectory(dirPath);

        await Assert.That(() => harness.Lifecycle.OpenDb(dirPath, "bad"))
            .Throws<SqliteException>();

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([(HookEventKind.Open, HookPhase.Before)]);
        await Assert.That(harness.Connections.ListConnections()).IsEmpty();
    }

    [Test]
    public async Task Query_InvalidSql_RunsBeforeOnly()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        await Assert.That(() => harness.Query.Query("SELECT FROM totally_invalid_syntax"))
            .Throws<SqliteException>();

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([(HookEventKind.Query, HookPhase.Before)]);
    }

    // --- Placeholders / shell ---

    [Test]
    public async Task OpenDb_DbPathWithSpaces_PlaceholderPassedCorrectly()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dbPath = harness.DbPath(Path.Combine("my db", "app.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        harness.Lifecycle.OpenDb(dbPath, "main");

        await Assert.That(hooks.Calls[0].Context.DbPath).IsEqualTo(Path.GetFullPath(dbPath));
    }

    [Test]
    public async Task CliHookRunner_DbPathWithSpaces_WrittenByHook()
    {
        var traceFile = Path.Combine(Path.GetTempPath(), "sqlite-mcp-hook-" + Guid.NewGuid().ToString("N"), "open-trace.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(traceFile)!);
        try
        {
            var runner = CreateRunner(new HookOptions
            {
                Open = new HookEventOptions
                {
                    After = HookTestCommands.WriteTextToFile(traceFile)
                }
            });
            using var harness = McpToolHarness.CreateEmpty(hooks: runner);
            var dbPath = harness.DbPath(Path.Combine("my db", "app.db"));
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            harness.Lifecycle.OpenDb(dbPath, "main");

            await Assert.That(File.Exists(traceFile)).IsTrue();
            await Assert.That(File.ReadAllText(traceFile)).IsEqualTo(Path.GetFullPath(dbPath));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(traceFile)!))
            {
                Directory.Delete(Path.GetDirectoryName(traceFile)!, recursive: true);
            }
        }
    }

    [Test]
    public async Task ApplyPlaceholders_SqlWithSpecialCharacters_SubstitutesInCommand()
    {
        const string sql = "SELECT 'he said \"hi\"' AS x, 'a & b' AS y;\n-- comment";
        var result = CliHookRunner.ApplyPlaceholders(
            "audit:{sql}",
            new HookContext { Sql = sql });

        await Assert.That(result).IsEqualTo($"audit:{sql}");
    }

    [Test]
    public async Task Query_SqlWithSpecialCharacters_PlaceholderPassedCorrectly()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        const string sql = "SELECT 'he said \"hi\"' AS x, 'a & b' AS y;\n-- comment";
        harness.Query.Query(sql);

        await Assert.That(hooks.Calls[0].Context.Sql).IsEqualTo(sql);
    }

    [Test]
    public async Task CloseAll_ConnectionKeyWithComma_InClosedKeys()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        harness.Lifecycle.OpenDb(harness.DbPath("comma.db"), "a,b");
        harness.Lifecycle.OpenDb(harness.DbPath("plain.db"), "c");
        hooks.Clear();

        harness.Lifecycle.CloseAll();

        await Assert.That(hooks.Calls[0].Context.ClosedKeys).IsEqualTo("a,b,c");
    }

    [Test]
    public async Task CloseAll_NoOpenConnections_StillRunsCloseAllHooks()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);

        harness.Lifecycle.CloseAll();

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([
                (HookEventKind.CloseAll, HookPhase.Before),
                (HookEventKind.CloseAll, HookPhase.After)
            ]);
        await Assert.That(hooks.Calls[0].Context.ClosedKeys).IsEqualTo("");
    }

    // --- Lifecycle ---

    [Test]
    public async Task LazyDefaultOpen_DoesNotRunOpenHooks()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        harness.Query.Query("SELECT 1");

        await Assert.That(hooks.Calls.Any(c => c.EventKind == HookEventKind.Open)).IsFalse();
        await Assert.That(hooks.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CloseDb_WhenNotOpen_SkipsHooksAndThrows()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);

        await Assert.That(() => harness.Lifecycle.CloseDb())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("No open connection");

        await Assert.That(hooks.Calls).IsEmpty();
    }

    [Test]
    public async Task CloseAll_DeleteHookOnLockedFile_StillCompletes()
    {
        var runner = CreateRunner(new HookOptions
        {
            Close = new HookEventOptions { Before = HookTestCommands.TryDeleteFile() }
        });
        using var harness = McpToolHarness.CreateEmpty(hooks: runner);
        var dbPath = harness.DbPath("locked.db");
        harness.Lifecycle.OpenDb(dbPath, "main");

        var json = harness.Lifecycle.CloseAll();
        var root = JsonTestExtensions.ParseObject(json);

        await Assert.That(root.GetString("message")).Contains("Closed");
        await Assert.That(harness.Connections.ListConnections()).IsEmpty();
    }

    [Test]
    public async Task CliHookRunner_QuoteForShSingleQuoted_WrapsCommand()
    {
        var quoted = CliHookRunner.QuoteForShSingleQuoted("printf '%s' '/tmp/my db/app.db'");

        await Assert.That(quoted.StartsWith('\'')).IsTrue();
        await Assert.That(quoted.EndsWith('\'')).IsTrue();
        await Assert.That(quoted).Contains("my db/app.db");
    }

    [Test]
    public async Task CloseDb_RemovesConnectionFromRegistry()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var dbPath = harness.DbPath("lock-test.db");
        harness.Lifecycle.OpenDb(dbPath, "main");

        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);

        harness.Lifecycle.CloseDb("main");

        await Assert.That(harness.Connections.ListConnections()).IsEmpty();
    }

    [Test, RunOn(OS.Windows)]
    public async Task CloseDb_FileLockedBeforeClose_UnlockedAfter()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var dbPath = harness.DbPath("lock-test.db");
        harness.Lifecycle.OpenDb(dbPath, "main");

        await Assert.That(() => File.Move(dbPath, dbPath + ".moved", overwrite: true))
            .Throws<IOException>();

        harness.Lifecycle.CloseDb("main");

        File.Move(dbPath, dbPath + ".moved", overwrite: true);
        await Assert.That(File.Exists(dbPath + ".moved")).IsTrue();
    }

    // --- Concurrency / ordering ---

    [Test]
    public async Task Query_ParallelCalls_AllHooksFire()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => harness.Query.Query("SELECT 1")))
            .ToArray();
        await Task.WhenAll(tasks);

        await Assert.That(hooks.Calls.Count(c => c is { EventKind: HookEventKind.Query, Phase: HookPhase.Before }))
            .IsEqualTo(10);
        await Assert.That(hooks.Calls.Count(c => c is { EventKind: HookEventKind.Query, Phase: HookPhase.After }))
            .IsEqualTo(10);
    }

    [Test]
    public async Task CloseAll_ClosesConnectionsInKeyOrder()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        harness.Lifecycle.OpenDb(harness.DbPath("z.db"), "z");
        harness.Lifecycle.OpenDb(harness.DbPath("a.db"), "a");
        harness.Lifecycle.OpenDb(harness.DbPath("m.db"), "m");
        hooks.Clear();

        harness.Lifecycle.CloseAll();

        var closeBeforeKeys = hooks.Calls
            .Where(c => c is { EventKind: HookEventKind.Close, Phase: HookPhase.Before })
            .Select(c => c.Context.ConnectionKey!)
            .ToList();

        await Assert.That(string.Join(",", closeBeforeKeys)).IsEqualTo("a,m,z");
    }

    // --- Timeout ---

    [Test]
    public async Task CliHookRunner_PerEventTimeout_OverridesGlobal()
    {
        var runner = CreateRunner(new HookOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            Query = new HookEventOptions
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                Before = HookTestCommands.SleepSeconds(5)
            }
        });
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var sw = Stopwatch.StartNew();
        var json = harness.Query.Query("SELECT 1");
        sw.Stop();

        await Assert.That(json).Contains("1");
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task CliHookRunner_ZeroTimeout_KillsSlowHook()
    {
        var runner = CreateRunner(new HookOptions
        {
            Timeout = TimeSpan.Zero,
            Query = new HookEventOptions { Before = HookTestCommands.SleepSeconds(5) }
        });
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var sw = Stopwatch.StartNew();
        var json = harness.Query.Query("SELECT 1");
        sw.Stop();

        await Assert.That(json).Contains("1");
        await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task CliHookRunner_DetachedChild_DoesNotHangQuery()
    {
        var runner = CreateRunner(new HookOptions
        {
            Timeout = TimeSpan.FromMilliseconds(500),
            Query = new HookEventOptions
            {
                Before = HookTestCommands.SpawnDetachedSleep(30)
            }
        });
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var queryTask = Task.Run(() => harness.Query.Query("SELECT 1"));
        var json = await queryTask.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(json).Contains("1");
    }

    // --- CRUD gap ---

    [Test]
    public async Task CrudTools_DoNotRunHooks()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Alice"), ("email", "alice@test.com")));
        harness.Crud.UpdateRecords(
            "users",
            JsonTestExtensions.Object(("email", "new@test.com")),
            JsonTestExtensions.Object(("name", "Alice")));

        await Assert.That(hooks.Calls).IsEmpty();
    }
}
