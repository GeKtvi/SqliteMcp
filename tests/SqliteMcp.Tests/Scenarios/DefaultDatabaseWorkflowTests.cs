using SqliteMcp.Tests.Infrastructure;

namespace SqliteMcp.Tests.Scenarios;

public class DefaultDatabaseWorkflowTests
{
    [Test]
    public async Task LazyOpen_OnFirstToolCall()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();

        await Assert.That(harness.Connections.ListConnections()).IsEmpty();

        var result = harness.Schema.ListTables();
        var root = JsonTestExtensions.ParseJson(result).RootElement;
        await Assert.That(root.TryGetProperty("message", out _)).IsTrue();

        await Assert.That(harness.Connections.ListConnections()).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ExploreAppDb()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var tablesJson = JsonTestExtensions.ParseJson(harness.Schema.ListTables()).RootElement;
        await Assert.That(tablesJson.GetArrayLength()).IsEqualTo(2);

        var schemaJson = JsonTestExtensions.ParseJson(harness.Schema.GetTableSchema("orders")).RootElement;
        await Assert.That(schemaJson.GetArrayLength()).IsEqualTo(2);

        var rowsJson = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords(
                "orders",
                JsonTestExtensions.Object(("status", "pending")),
                limit: 10)).RootElement;

        await Assert.That(rowsJson.GetArrayLength()).IsEqualTo(1);
        await Assert.That(rowsJson[0].GetString("status")).IsEqualTo("pending");
    }

    [Test]
    public async Task DebugWithQuery()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var rowsJson = JsonTestExtensions.ParseJson(
            harness.Query.Query(
                "SELECT name FROM order_items WHERE order_id = ?",
                JsonTestExtensions.Array(1L))).RootElement;

        await Assert.That(rowsJson.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task SeedAndFixData()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var createJson = JsonTestExtensions.ParseJson(
            harness.Crud.CreateRecord(
                "users",
                JsonTestExtensions.Object(("name", "Alice"), ("email", "alice@test.com")))).RootElement;
        await Assert.That(createJson.GetInt64("insertedId")).IsEqualTo(1);

        var updateJson = JsonTestExtensions.ParseJson(
            harness.Crud.UpdateRecords(
                "users",
                JsonTestExtensions.Object(("email", "alice@example.com")),
                JsonTestExtensions.Object(("name", "Alice")))).RootElement;
        await Assert.That(updateJson.GetInt64("rowsAffected")).IsEqualTo(1);

        var deleteJson = JsonTestExtensions.ParseJson(
            harness.Crud.DeleteRecords(
                "users",
                JsonTestExtensions.Object(("name", "Alice")))).RootElement;
        await Assert.That(deleteJson.GetInt64("rowsAffected")).IsEqualTo(1);
    }

    [Test]
    public async Task DbInfoReportsMetadata()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var infoJson = JsonTestExtensions.ParseJson(harness.Lifecycle.DbInfo()).RootElement;
        var expectedPath = Path.GetFullPath(harness.DbPath());

        await Assert.That(infoJson.GetString("dbPath")).IsEqualTo(expectedPath);
        await Assert.That(infoJson.GetBoolean("exists")).IsTrue();
        await Assert.That(infoJson.GetInt64("size")).IsGreaterThan(0);
        await Assert.That(infoJson.GetInt64("tableCount")).IsEqualTo(2);
        await Assert.That(infoJson.GetBoolean("isOpen")).IsTrue();
    }

    [Test]
    [Arguments(1, 0, 1)]
    [Arguments(2, 0, 2)]
    [Arguments(1, 1, 1)]
    public async Task ReadRecords_LimitAndOffset(long limit, long offset, int expectedCount)
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateBugHuntSchema(harness);

        var rowsJson = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("orders", limit: limit, offset: offset)).RootElement;

        await Assert.That(rowsJson.GetArrayLength()).IsEqualTo(expectedCount);
    }

    [Test]
    public async Task Query_NonSelect_ReturnsChangeMetadata()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath();
        SampleSchemas.CreateUsersTable(harness);

        var result = JsonTestExtensions.ParseJson(
            harness.Query.Query(
                "INSERT INTO users (name, email) VALUES (?, ?)",
                JsonTestExtensions.Array("QueryUser", "query@test.com"))).RootElement;

        await Assert.That(result.GetInt64("changes")).IsEqualTo(1);
        await Assert.That(result.GetInt64("lastInsertRowId")).IsEqualTo(1);
        await Assert.That(result.TryGetProperty("name", out _)).IsFalse();
    }
}
