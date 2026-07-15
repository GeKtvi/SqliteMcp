using SqliteMcp.Tests.Infrastructure;

namespace SqliteMcp.Tests.Scenarios;

public class MultiDatabaseWorkflowTests
{
    [Test]
    public async Task AppPlusAnalytics()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("app.db");
        SampleSchemas.CreateUsersTable(harness);

        var analyticsPath = harness.DbPath("analytics.db");
        harness.Lifecycle.OpenDb(analyticsPath, "analytics");
        SampleSchemas.CreateSessionsTable(harness, "analytics");

        var appRows = JsonTestExtensions.ParseJson(harness.Crud.ReadRecords("users")).RootElement;
        var analyticsCount = JsonTestExtensions.ParseJson(
            harness.Query.Query("SELECT COUNT(*) AS count FROM sessions", connectionKey: "analytics")).RootElement;

        await Assert.That(appRows.GetArrayLength()).IsEqualTo(0);
        await Assert.That(analyticsCount[0].GetInt64("count")).IsEqualTo(2);
    }

    [Test]
    public async Task ProdVsStaging()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var prodPath = harness.DbPath("prod.db");
        var stagingPath = harness.DbPath("staging.db");

        SampleSchemas.SeedNamedDb(harness, prodPath, "prod", "production");
        SampleSchemas.SeedNamedDb(harness, stagingPath, "staging", "staging");

        var prodLabel = JsonTestExtensions.ParseJson(
            harness.Query.Query("SELECT label FROM env_marker", connectionKey: "prod")).RootElement;
        var stagingLabel = JsonTestExtensions.ParseJson(
            harness.Query.Query("SELECT label FROM env_marker", connectionKey: "staging")).RootElement;

        await Assert.That(prodLabel[0].GetString("label")).IsEqualTo("production");
        await Assert.That(stagingLabel[0].GetString("label")).IsEqualTo("staging");
    }

    [Test]
    public async Task MicroservicesKeys()
    {
        using var harness = McpToolHarness.CreateEmpty();

        foreach (var key in new[] { "users", "orders", "inventory" })
        {
            harness.Lifecycle.OpenDb(harness.DbPath($"{key}.db"), key);
            SampleSchemas.CreateUsersTable(harness, key);
            harness.Crud.CreateRecord(
                "users",
                JsonTestExtensions.Object(("name", key), ("email", $"{key}@test.com")),
                connectionKey: key);
        }

        foreach (var key in new[] { "users", "orders", "inventory" })
        {
            var rows = JsonTestExtensions.ParseJson(
                harness.Crud.ReadRecords("users", connectionKey: key)).RootElement;
            await Assert.That(rows[0].GetString("name")).IsEqualTo(key);
        }
    }

    [Test]
    public async Task MigrationSourceToTarget()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var sourcePath = harness.DbPath("source.db");
        var targetPath = harness.DbPath("target.db");

        harness.Lifecycle.OpenDb(sourcePath, "source");
        SampleSchemas.CreateUsersTable(harness, "source");
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Alice"), ("email", "alice@test.com")),
            connectionKey: "source");
        harness.Crud.CreateRecord(
            "users",
            JsonTestExtensions.Object(("name", "Bob"), ("email", "bob@test.com")),
            connectionKey: "source");

        harness.Lifecycle.OpenDb(targetPath, "target");
        SampleSchemas.CreateUsersTable(harness, "target");

        var sourceRows = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("users", connectionKey: "source")).RootElement;

        foreach (var row in sourceRows.EnumerateArray())
        {
            harness.Crud.CreateRecord(
                "users",
                JsonTestExtensions.Object(
                    ("name", row.GetString("name")),
                    ("email", row.GetString("email"))),
                connectionKey: "target");
        }

        var targetRows = JsonTestExtensions.ParseJson(
            harness.Crud.ReadRecords("users", connectionKey: "target")).RootElement;

        await Assert.That(sourceRows.GetArrayLength()).IsEqualTo(2);
        await Assert.That(targetRows.GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task ListConnections_ShowsAllOpenKeys()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("app.db");
        SampleSchemas.CreateUsersTable(harness);
        harness.Lifecycle.OpenDb(harness.DbPath("analytics.db"), "analytics");

        var listJson = JsonTestExtensions.ParseJson(harness.Lifecycle.ListConnections()).RootElement;
        var connections = listJson.GetPropertyElement("connections");

        await Assert.That(listJson.GetString("defaultPath")).IsEqualTo(Path.GetFullPath(harness.DbPath("app.db")));
        await Assert.That(connections.GetArrayLength()).IsEqualTo(2);

        var keys = connections.EnumerateArray()
            .Select(c => c.GetString("connectionKey"))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(keys).IsEquivalentTo(["analytics", "default"]);

        var defaultEntry = connections.EnumerateArray().Single(c => c.GetString("connectionKey") == "default");
        await Assert.That(defaultEntry.GetBoolean("isDefault")).IsTrue();
    }

    [Test]
    [MatrixDataSource]
    public async Task DatabaseOperations_WorkPerConnectionKey(
        [Matrix("default", "cache")] string connectionKey,
        [Matrix("count", "insert")] string operation)
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("main.db");
        SampleSchemas.CreateUsersTable(harness);
        harness.Lifecycle.OpenDb(harness.DbPath("cache.db"), "cache");
        SampleSchemas.CreateSessionsTable(harness, "cache");

        if (operation == "count")
        {
            var table = connectionKey == "cache" ? "sessions" : "users";
            var rows = JsonTestExtensions.ParseJson(
                harness.Query.Query($"SELECT COUNT(*) AS count FROM {table}", connectionKey: connectionKey)).RootElement;
            var expected = connectionKey == "cache" ? 2 : 0;
            await Assert.That(rows[0].GetInt64("count")).IsEqualTo(expected);
        }
        else
        {
            if (connectionKey == "cache")
            {
                harness.Crud.CreateRecord(
                    "sessions",
                    JsonTestExtensions.Object(("token", "xyz")),
                    connectionKey: "cache");
                var rows = JsonTestExtensions.ParseJson(
                    harness.Crud.ReadRecords("sessions", connectionKey: "cache")).RootElement;
                await Assert.That(rows.GetArrayLength()).IsEqualTo(3);
            }
            else
            {
                harness.Crud.CreateRecord(
                    "users",
                    JsonTestExtensions.Object(("name", "MainUser"), ("email", "main@test.com")));
                var rows = JsonTestExtensions.ParseJson(
                    harness.Crud.ReadRecords("users")).RootElement;
                await Assert.That(rows.GetArrayLength()).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task ListConnectionsBeforeOpen_ShowsExistingKeys()
    {
        using var harness = McpToolHarness.CreateWithDefaultPath("app.db");
        SampleSchemas.CreateUsersTable(harness);

        var beforeOpen = JsonTestExtensions.ParseJson(harness.Lifecycle.ListConnections()).RootElement;
        await Assert.That(beforeOpen.GetPropertyElement("connections").GetArrayLength()).IsEqualTo(1);

        var analyticsPath = harness.DbPath("analytics.db");
        var beforeSecondOpen = JsonTestExtensions.ParseJson(harness.Lifecycle.ListConnections()).RootElement;
        var existingKeys = beforeSecondOpen.GetPropertyElement("connections")
            .EnumerateArray()
            .Select(c => c.GetString("connectionKey"))
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That(existingKeys.Contains("analytics")).IsFalse();

        harness.Lifecycle.OpenDb(analyticsPath, "analytics");

        var afterOpen = JsonTestExtensions.ParseJson(harness.Lifecycle.ListConnections()).RootElement;
        await Assert.That(afterOpen.GetPropertyElement("connections").GetArrayLength()).IsEqualTo(2);
    }

    [Test]
    public async Task ParallelWrites_DifferentKeys_AreIsolated()
    {
        using var harness = McpToolHarness.CreateEmpty();
        var keys = new[] { "users", "orders", "inventory" };

        foreach (var key in keys)
        {
            harness.Lifecycle.OpenDb(harness.DbPath($"{key}.db"), key);
            SampleSchemas.CreateUsersTable(harness, key);
        }

        await Task.WhenAll(keys.Select(key => Task.Run(() =>
        {
            for (var i = 0; i < 5; i++)
            {
                harness.Crud.CreateRecord(
                    "users",
                    JsonTestExtensions.Object(("name", $"{key}-{i}"), ("email", $"{key}{i}@test.com")),
                    connectionKey: key);
            }
        })));

        foreach (var key in keys)
        {
            var rows = JsonTestExtensions.ParseJson(
                harness.Crud.ReadRecords("users", connectionKey: key)).RootElement;
            await Assert.That(rows.GetArrayLength()).IsEqualTo(5);
            await Assert.That(rows[0].GetString("name")).StartsWith(key);
        }
    }
}
