using SqliteMcp.Tests.Infrastructure;
using TUnit.Core.Enums;

namespace SqliteMcp.Tests.Scenarios;

public class NarrativeWorkflowTests
{
    [Test]
    public async Task BugHunt_InLocalApp()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var tables = JsonTestExtensions.ParseJson(harness.Schema.ListTables()).RootElement;
        await Assert.That(tables.GetArrayLength()).IsGreaterThan(0);

        var schema = JsonTestExtensions.ParseJson(harness.Schema.GetTableSchema("orders")).RootElement;
        await Assert.That(schema.GetArrayLength()).IsEqualTo(2);

        var pendingOrders = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords(
                "orders",
                JsonTestExtensions.Object(("status", "pending")),
                limit: 10)).RootElement;
        await Assert.That(pendingOrders.GetArrayLength()).IsEqualTo(1);

        var orderId = pendingOrders[0].GetInt64("id");
        var items = JsonTestExtensions.ParseJson(
            harness.Query.Query(
                "SELECT * FROM order_items WHERE order_id = ?",
                JsonTestExtensions.Array(orderId))).RootElement;
        await Assert.That(items.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task MainPlusCache_TwoDatabases()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("main.db");
        SampleSchemas.CreateUsersTable(harness);

        harness.Lifecycle.OpenDb(harness.DbPath("cache.db"), "cache");
        SampleSchemas.CreateSessionsTable(harness, "cache");

        var sessionCount = JsonTestExtensions.ParseJson(
            harness.Query.Query("SELECT COUNT(*) AS count FROM sessions", connectionKey: "cache")).RootElement;
        await Assert.That(sessionCount[0].GetInt64("count")).IsEqualTo(2);

        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Carol"), ("email", "carol@test.com")));

        var users = JsonTestExtensions.ParseJson(harness.Crud.ReadRecords("users")).RootElement;
        await Assert.That(users.GetArrayLength()).IsEqualTo(1);
    }

    [Test, RunOn(OS.Windows)]
    public async Task DeploymentUnlock_ReplaceDbFile()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("main.db");
        SampleSchemas.CreateUsersTable(harness);
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "OldUser"), ("email", "old@test.com")));

        var dbPath = harness.DbPath("main.db");
        harness.Lifecycle.CloseAll();

        File.Delete(dbPath);

        using var replacement = McpToolHarness.CreateEmpty();
        replacement.Lifecycle.OpenDb(dbPath);
        SampleSchemas.CreateUsersTable(replacement);
        replacement.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "NewUser"), ("email", "new@test.com")));
        replacement.Lifecycle.CloseDb();

        harness.Lifecycle.OpenDb(dbPath);
        var rows = JsonTestExtensions.ParseJson(harness.Crud.ReadRecords("users")).RootElement;

        await Assert.That(rows.GetArrayLength()).IsEqualTo(1);
        await Assert.That(rows[0].GetString("name")).IsEqualTo("NewUser");
    }
}
