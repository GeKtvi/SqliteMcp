using SqliteMcp.Tests.Infrastructure;

namespace SqliteMcp.Tests.Scenarios;

public class SafetyGuardTests
{
    [Test]
    [Arguments("update_records")]
    [Arguments("delete_records")]
    public async Task EmptyConditions_Rejected(string toolName)
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Alice"), ("email", "alice@test.com")));

        if (toolName == "update_records")
        {
            await Assert.That(() => harness.Crud.UpdateRecords(
                    "users",
                    JsonTestExtensions.Object(("email", "x@test.com")),
                    JsonTestExtensions.EmptyObject()))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("conditions must not be empty");
        }
        else
        {
            await Assert.That(() => harness.Crud.DeleteRecords(
                    "users",
                    JsonTestExtensions.EmptyObject()))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("conditions must not be empty");
        }
    }

    [Test]
    [Arguments("not_a_table", "status", "Table")]
    [Arguments("orders", "not_a_column", "Column")]
    public async Task InvalidTableOrColumn_Rejected(string table, string column, string expectedFragment)
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var filterColumn = table == "orders" ? column : "status";
        await Assert.That(() => harness.Crud.ReadRecords(
                table,
                JsonTestExtensions.Object((filterColumn, "pending"))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(expectedFragment);
    }

    [Test]
    public async Task OpenDb_SameKeyDifferentPath_Throws()
    {
        using var harness = McpToolHarness.CreateEmpty();
        harness.Lifecycle.OpenDb(harness.DbPath("a.db"), "shared");

        await Assert.That(() => harness.Lifecycle.OpenDb(harness.DbPath("b.db"), "shared"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already open");
    }

    [Test]
    public async Task OpenDb_SameKeySamePath_Reuses()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var path = harness.DbPath("a.db");

        var first = JsonTestExtensions.ParseJson(harness.Lifecycle.OpenDb(path, "shared")).RootElement;
        var second = JsonTestExtensions.ParseJson(harness.Lifecycle.OpenDb(path, "shared")).RootElement;

        await Assert.That(first.GetString("connectionKey")).IsEqualTo("shared");
        await Assert.That(second.GetString("path")).IsEqualTo(first.GetString("path"));
        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CloseDb_WhenNotOpen_Throws()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();

        await Assert.That(() => harness.Lifecycle.CloseDb())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("No open connection");
    }

    [Test]
    public async Task UpdateRecords_EmptyData_Throws()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        await Assert.That(() => harness.Crud.UpdateRecords(
                "users",
                JsonTestExtensions.EmptyObject(),
                JsonTestExtensions.Object(("name", "Alice"))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("data must contain at least one column");
    }

    [Test]
    public async Task CreateRecord_EmptyData_Throws()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        await Assert.That(() => harness.Crud.CreateRecord("users", JsonTestExtensions.EmptyObject()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("data must contain at least one column");
    }

    [Test]
    [Arguments(-1)]
    [Arguments(1.5)]
    public async Task ReadRecords_InvalidLimit_Throws(double limit)
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        await Assert.That(() => harness.Crud.ReadRecords("orders", limit: limit))
            .Throws<ArgumentException>()
            .WithMessageContaining("limit must be a non-negative integer");
    }

    [Test]
    public async Task ReadRecords_OffsetWithoutLimit_IsIgnored()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var rowsJson = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("orders", offset: 1)).RootElement;

        await Assert.That(rowsJson.GetArrayLength()).IsEqualTo(2);
    }
}
