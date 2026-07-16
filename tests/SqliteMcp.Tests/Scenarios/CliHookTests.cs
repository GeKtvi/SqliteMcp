using Microsoft.Extensions.Logging.Abstractions;
using SqliteMcp.Tests.Infrastructure;

namespace SqliteMcp.Tests.Scenarios;

public class CliHookTests
{
    [Test]
    public async Task OpenDb_RunsBeforeThenAfter_OnNewConnection()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dbPath = harness.DbPath("app.db");

        harness.Lifecycle.OpenDb(dbPath, "main");

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([
                (HookEventKind.Open, HookPhase.Before),
                (HookEventKind.Open, HookPhase.After)
            ]);
        await Assert.That(hooks.Calls[0].Context.ConnectionKey).IsEqualTo("main");
        await Assert.That(hooks.Calls[0].Context.DbPath).IsEqualTo(Path.GetFullPath(dbPath));
    }

    [Test]
    public async Task OpenDb_SkipsHooks_WhenConnectionReused()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dbPath = harness.DbPath("app.db");

        harness.Lifecycle.OpenDb(dbPath, "main");
        hooks.Clear();
        harness.Lifecycle.OpenDb(dbPath, "main");

        await Assert.That(hooks.Calls).IsEmpty();
    }

    [Test]
    public async Task OpenDb_OpenWinsDuringBefore_StillRunsAfter()
    {
        // Simulates another thread opening the same key between Before and the create:
        // Before must still be paired with After (old code skipped After when WasReused).
        var connections = new SqliteConnectionManager();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "sqlite-mcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var dbPath = Path.Combine(tempDirectory, "race.db");
            var recording = new RecordingCliHookRunner();
            var hooks = new NestedOpenDuringBeforeHookRunner(connections, dbPath, "main", recording);
            var lifecycle = new DatabaseLifecycleTools(connections, hooks);

            lifecycle.OpenDb(dbPath, "main");

            await Assert.That(recording.Calls.Select(c => (c.EventKind, c.Phase)))
                .IsEquivalentTo([
                    (HookEventKind.Open, HookPhase.Before),
                    (HookEventKind.Open, HookPhase.After)
                ]);
            await Assert.That(connections.ListConnections()).Count().IsEqualTo(1);
        }
        finally
        {
            connections.Dispose();
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Test]
    public async Task OpenDb_ConcurrentSameKey_BeforeAfterCountsMatch()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dbPath = harness.DbPath("shared.db");

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => harness.Lifecycle.OpenDb(dbPath, "shared")))
            .ToArray();
        await Task.WhenAll(tasks);

        var beforeCount = hooks.Calls.Count(c => c is { EventKind: HookEventKind.Open, Phase: HookPhase.Before });
        var afterCount = hooks.Calls.Count(c => c is { EventKind: HookEventKind.Open, Phase: HookPhase.After });
        await Assert.That(afterCount).IsEqualTo(beforeCount);
        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }

    /// <summary>
    /// On first Open.Before, opens the same key via the connection manager (as a racing thread would).
    /// </summary>
    private sealed class NestedOpenDuringBeforeHookRunner(
        SqliteConnectionManager connections,
        string path,
        string connectionKey,
        ICliHookRunner inner) : ICliHookRunner
    {
        private int _beforeDepth;

        public void Run(HookEventKind eventKind, HookPhase phase, HookContext context)
        {
            inner.Run(eventKind, phase, context);

            if (eventKind == HookEventKind.Open
                && phase == HookPhase.Before
                && Interlocked.CompareExchange(ref _beforeDepth, 1, 0) == 0)
            {
                _ = connections.Open(path, connectionKey);
            }
        }
    }

    [Test]
    public async Task CloseDb_RunsBeforeThenAfter()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        var dbPath = harness.DbPath("app.db");
        harness.Lifecycle.OpenDb(dbPath, "main");
        hooks.Clear();

        harness.Lifecycle.CloseDb("main");

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([
                (HookEventKind.Close, HookPhase.Before),
                (HookEventKind.Close, HookPhase.After)
            ]);
        await Assert.That(hooks.Calls[0].Context.ConnectionKey).IsEqualTo("main");
    }

    [Test]
    public async Task CloseAll_RunsCloseAllThenPerConnectionCloseHooks()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateEmpty(hooks);
        harness.Lifecycle.OpenDb(harness.DbPath("a.db"), "a");
        harness.Lifecycle.OpenDb(harness.DbPath("b.db"), "b");
        hooks.Clear();

        harness.Lifecycle.CloseAll();

        var phases = hooks.Calls.Select(c => (c.EventKind, c.Phase)).ToList();
        await Assert.That(phases.Count).IsEqualTo(6);
        await Assert.That(phases[0]).IsEqualTo((HookEventKind.CloseAll, HookPhase.Before));
        await Assert.That(phases[1]).IsEqualTo((HookEventKind.Close, HookPhase.Before));
        await Assert.That(phases[2]).IsEqualTo((HookEventKind.Close, HookPhase.After));
        await Assert.That(phases[3]).IsEqualTo((HookEventKind.Close, HookPhase.Before));
        await Assert.That(phases[4]).IsEqualTo((HookEventKind.Close, HookPhase.After));
        await Assert.That(phases[5]).IsEqualTo((HookEventKind.CloseAll, HookPhase.After));
        await Assert.That(hooks.Calls[0].Context.ClosedKeys).IsEqualTo("a,b");
        await Assert.That(hooks.Calls[1].Context.ConnectionKey).IsEqualTo("a");
        await Assert.That(hooks.Calls[3].Context.ConnectionKey).IsEqualTo("b");
    }

    [Test]
    public async Task CloseAll_ConcurrentCalls_DoNotThrowAndReleaseAllLocks()
    {
        using var harness = McpToolHarness.CreateEmpty();
        harness.Lifecycle.OpenDb(harness.DbPath("a.db"), "a");
        harness.Lifecycle.OpenDb(harness.DbPath("b.db"), "b");

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => harness.Lifecycle.CloseAll()))
            .ToArray();
        await Task.WhenAll(tasks);

        await Assert.That(harness.Connections.ListConnections()).IsEmpty();
    }

    [Test]
    public async Task Query_RunsBeforeThenAfter()
    {
        var hooks = new RecordingCliHookRunner();
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: hooks);
        SampleSchemas.CreateUsersTable(harness);
        hooks.Clear();

        harness.Query.Query("SELECT 1");

        await Assert.That(hooks.Calls.Select(c => (c.EventKind, c.Phase)))
            .IsEquivalentTo([
                (HookEventKind.Query, HookPhase.Before),
                (HookEventKind.Query, HookPhase.After)
            ]);
        await Assert.That(hooks.Calls[0].Context.Sql).IsEqualTo("SELECT 1");
        await Assert.That(hooks.Calls[0].Context.ConnectionKey).IsEqualTo("default");
    }

    [Test]
    public async Task ApplyPlaceholders_TruncatesLongSql()
    {
        var result = CliHookRunner.ApplyPlaceholders(
            "echo {sql}",
            new HookContext { Sql = new string('x', 2500) });

        await Assert.That(result).IsEqualTo("echo " + new string('x', 2000));
    }

    [Test]
    public async Task CliHookRunner_NonZeroExit_DoesNotThrowFromTool()
    {
        var options = new TestOptionsMonitor<HookOptions>(new HookOptions
        {
            Query = new HookEventOptions
            {
                Before = OperatingSystem.IsWindows()
                    ? "exit /b 1"
                    : "exit 1"
            }
        });
        var runner = new CliHookRunner(options, NullLogger<CliHookRunner>.Instance);
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var json = harness.Query.Query("SELECT 1");

        await Assert.That(json).Contains("1");
    }

    [Test]
    public async Task CliHookRunner_Timeout_DoesNotThrowFromTool()
    {
        var options = new TestOptionsMonitor<HookOptions>(new HookOptions
        {
            Timeout = TimeSpan.FromMilliseconds(200),
            Query = new HookEventOptions
            {
                Before = OperatingSystem.IsWindows()
                    ? "timeout /t 5 /nobreak >nul"
                    : "sleep 5"
            }
        });
        var runner = new CliHookRunner(options, NullLogger<CliHookRunner>.Instance);
        using var harness = McpToolHarness.CreateWithDefaultPath(hooks: runner);
        SampleSchemas.CreateUsersTable(harness);

        var json = harness.Query.Query("SELECT 1");

        await Assert.That(json).Contains("1");
    }

    [Test]
    public async Task CliHookRunner_PicksUpHookOptionsChange_WithoutRestart()
    {
        var markerPath = Path.Combine(Path.GetTempPath(), "sqlite-mcp-hook-reload-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var monitor = new TestOptionsMonitor<HookOptions>(new HookOptions
            {
                Query = new HookEventOptions { Before = "" }
            });
            var runner = new CliHookRunner(monitor, NullLogger<CliHookRunner>.Instance);

            runner.Run(HookEventKind.Query, HookPhase.Before, new HookContext { Sql = "SELECT 1" });
            await Assert.That(File.Exists(markerPath)).IsFalse();

            monitor.Set(new HookOptions
            {
                Query = new HookEventOptions
                {
                    Before = HookTestCommands.WriteFixedTextToFile(markerPath, "reloaded")
                }
            });

            runner.Run(HookEventKind.Query, HookPhase.Before, new HookContext { Sql = "SELECT 1" });

            await Assert.That(File.Exists(markerPath)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(markerPath)).IsEqualTo("reloaded");
        }
        finally
        {
            try
            {
                File.Delete(markerPath);
            }
            catch (IOException)
            {
            }
        }
    }
}
