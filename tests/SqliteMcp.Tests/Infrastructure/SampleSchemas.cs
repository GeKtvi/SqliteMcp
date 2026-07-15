using SqliteMcp.Sql;

namespace SqliteMcp.Tests.Infrastructure;

/// <summary>
/// SQL seeds for common scenario databases.
/// </summary>
public static class SampleSchemas
{
    public static void CreateBugHuntSchema(McpToolHarness harness)
    {
        var entry = harness.Connections.GetConnection(null);
        SqliteCommandRunner.Execute(entry.Connection, """
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                status TEXT NOT NULL
            )
            """);
        SqliteCommandRunner.Execute(entry.Connection, """
            CREATE TABLE order_items (
                id INTEGER PRIMARY KEY,
                order_id INTEGER NOT NULL,
                name TEXT NOT NULL
            )
            """);
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO orders (status) VALUES ('pending')");
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO orders (status) VALUES ('shipped')");
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO order_items (order_id, name) VALUES (1, 'widget')");
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO order_items (order_id, name) VALUES (1, 'gadget')");
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO order_items (order_id, name) VALUES (2, 'bolt')");
    }

    public static void CreateUsersTable(McpToolHarness harness, string? connectionKey = null)
    {
        var entry = harness.Connections.GetConnection(connectionKey);
        SqliteCommandRunner.Execute(entry.Connection, """
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT
            )
            """);
    }

    public static void CreateSessionsTable(McpToolHarness harness, string connectionKey)
    {
        var entry = harness.Connections.GetConnection(connectionKey);
        SqliteCommandRunner.Execute(entry.Connection, """
            CREATE TABLE sessions (
                id INTEGER PRIMARY KEY,
                token TEXT NOT NULL
            )
            """);
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO sessions (token) VALUES ('abc')");
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO sessions (token) VALUES ('def')");
    }

    public static void SeedNamedDb(McpToolHarness harness, string dbPath, string connectionKey, string marker)
    {
        harness.Lifecycle.OpenDb(dbPath, connectionKey);
        var entry = harness.Connections.GetConnection(connectionKey);
        SqliteCommandRunner.Execute(entry.Connection, """
            CREATE TABLE env_marker (
                id INTEGER PRIMARY KEY,
                label TEXT NOT NULL
            )
            """);
        SqliteCommandRunner.Execute(entry.Connection, "INSERT INTO env_marker (label) VALUES (?)", [marker]);
    }
}
