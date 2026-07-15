using SqliteMcp.Tests.Infrastructure;
using TUnit.Core.Enums;

namespace SqliteMcp.Tests.Scenarios;

public class FileLockWorkflowTests
{
    [Test, RunOn(OS.Windows)]
    public async Task CloseDb_ReleasesLockForDelete()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var dbPath = harness.DbPath();
        harness.Lifecycle.CloseDb();

        File.Delete(dbPath);
        await Assert.That(File.Exists(dbPath)).IsFalse();
    }

    [Test, RunOn(OS.Windows)]
    public async Task CloseDb_ReleasesLockForMove()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var dbPath = harness.DbPath();
        var movedPath = harness.DbPath("moved.db");
        harness.Lifecycle.CloseDb();

        File.Move(dbPath, movedPath);
        await Assert.That(File.Exists(movedPath)).IsTrue();
        await Assert.That(File.Exists(dbPath)).IsFalse();
    }

    [Test, RunOn(OS.Windows)]
    public async Task CloseAll_ReleasesAllLocks()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var defaultPath = harness.DbPath();
        var analyticsPath = harness.DbPath("analytics.db");
        harness.Lifecycle.OpenDb(analyticsPath, "analytics");
        SampleSchemas.CreateSessionsTable(harness, "analytics");

        harness.Lifecycle.CloseAll();

        File.Delete(defaultPath);
        File.Delete(analyticsPath);
        await Assert.That(File.Exists(defaultPath)).IsFalse();
        await Assert.That(File.Exists(analyticsPath)).IsFalse();
    }

    [Test, RunOn(OS.Windows)]
    public async Task SwapDbFileWhileSessionAlive()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var dbPath = harness.DbPath();
        harness.Lifecycle.CloseDb();

        File.Delete(dbPath);
        harness.Lifecycle.OpenDb(dbPath);
        SampleSchemas.CreateUsersTable(harness);
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Bob"), ("email", "bob@test.com")));

        var rowsJson = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("users")).RootElement;

        await Assert.That(rowsJson.GetArrayLength()).IsEqualTo(1);
        await Assert.That(rowsJson[0].GetString("name")).IsEqualTo("Bob");
    }

    [Test]
    public async Task CloseAll_KeepsDefaultPathForLazyReopen()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var expectedPath = harness.Connections.DefaultPath;
        harness.Lifecycle.CloseAll();

        await Assert.That(harness.Connections.DefaultPath).IsEqualTo(expectedPath);
        await Assert.That(harness.Connections.ListConnections()).IsEmpty();

        var tablesJson = JsonTestExtensions.ParseJson(harness.Schema.ListTables()).RootElement;
        await Assert.That(tablesJson.GetArrayLength()).IsEqualTo(1);
        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }

    [Test, RunOn(OS.Windows)]
    public async Task CloseDb_KeyedConnection_ReleasesOnlyThatFile()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("app.db");
        SampleSchemas.CreateUsersTable(harness);

        var analyticsPath = harness.DbPath("analytics.db");
        harness.Lifecycle.OpenDb(analyticsPath, "analytics");
        SampleSchemas.CreateSessionsTable(harness, "analytics");

        harness.Lifecycle.CloseDb("analytics");

        File.Delete(analyticsPath);
        await Assert.That(File.Exists(analyticsPath)).IsFalse();

        var defaultRows = JsonTestExtensions.ParseJson(harness.Crud.ReadRecords("users")).RootElement;
        await Assert.That(defaultRows.GetArrayLength()).IsEqualTo(0);
        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }

    [Test, RunOn(OS.Windows)]
    public async Task ExternalBackup_CloseCopyReopen()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "BackupMe"), ("email", "backup@test.com")));

        var dbPath = harness.DbPath();
        var backupPath = harness.DbPath("backup-copy.db");
        harness.Lifecycle.CloseDb();

        File.Copy(dbPath, backupPath);

        harness.Lifecycle.OpenDb(dbPath);
        var liveRows = JsonTestExtensions.ParseJson(harness.Crud.ReadRecords("users")).RootElement;
        await Assert.That(liveRows[0].GetString("name")).IsEqualTo("BackupMe");

        harness.Lifecycle.OpenDb(backupPath, "backup");
        var backupRows = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("users", connectionKey: "backup")).RootElement;
        await Assert.That(backupRows[0].GetString("name")).IsEqualTo("BackupMe");
    }
}
