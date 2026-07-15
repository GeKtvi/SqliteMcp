using SqliteMcp.Tests.Infrastructure;

namespace SqliteMcp.Tests.Scenarios;

public class ExplicitConnectionWorkflowTests
{
    [Test]
    public async Task UnkeyedToolWithoutDefault_Throws()
    {
        using var harness = McpToolHarness.CreateEmpty();

        await Assert.That(() => harness.Connections.GetConnection(null))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("No default database");
    }

    [Test]
    public async Task OpenDbWithKey_ThenAllToolsWork()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var reportPath = harness.DbPath("report.db");
        harness.Lifecycle.OpenDb(reportPath, "report");

        harness.Query.Query(
            """
            CREATE TABLE metrics (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                value REAL NOT NULL
            )
            """,
            connectionKey: "report");

        var tables = JsonTestExtensions.ParseJson(
            harness.Schema.ListTables(connectionKey: "report")).RootElement;
        await Assert.That(tables.GetArrayLength()).IsEqualTo(1);

        var schema = JsonTestExtensions.ParseJson(
            harness.Schema.GetTableSchema("metrics", connectionKey: "report")).RootElement;
        await Assert.That(schema.GetArrayLength()).IsEqualTo(3);

        harness.Crud.CreateRecord(
            "metrics",
            JsonTestExtensions.Object(("name", "latency"), ("value", 42.5)),
            connectionKey: "report");

        var rows = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords(
                "metrics",
                JsonTestExtensions.Object(("name", "latency")),
                connectionKey: "report")).RootElement;
        await Assert.That(rows.GetArrayLength()).IsEqualTo(1);

        var info = JsonTestExtensions.ParseJson(
            harness.Lifecycle.DbInfo(connectionKey: "report")).RootElement;
        await Assert.That(info.GetString("connectionKey")).IsEqualTo("report");
    }

    [Test]
    public async Task MissingKey_Throws()
    {
        using var harness = McpToolHarness.CreateEmpty();

        await Assert.That(() => harness.Schema.ListTables(connectionKey: "nonexistent"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("open_db");
    }
}
