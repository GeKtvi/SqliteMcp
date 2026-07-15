using System.ComponentModel;
using ModelContextProtocol.Server;
using SqliteMcp.Sql;

namespace SqliteMcp.Tools;

/// <summary>
/// MCP tools for listing tables and reading schema metadata.
/// </summary>
[McpServerToolType]
public sealed class SchemaTools(SqliteConnectionManager connections)
{
    /// <summary>Lists user tables (excludes <c>sqlite_%</c> system tables).</summary>
    [McpServerTool(Name = "list_tables"), Description(
        "List all user tables in the SQLite database (excludes system tables).")]
    public string ListTables(
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var tables = SqliteCommandRunner.Query(
            entry.Connection,
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """);

        if (tables.Count == 0)
        {
            return SqliteCommandRunner.ToJson(new
            {
                message = "No tables found in database",
                connectionKey = entry.Key,
                dbPath = entry.Path,
                exists = File.Exists(entry.Path),
                size = File.Exists(entry.Path) ? new FileInfo(entry.Path).Length : 0
            });
        }

        return SqliteCommandRunner.ToJson(tables);
    }

    /// <summary>Returns <c>PRAGMA table_info</c> for a validated table name.</summary>
    [McpServerTool(Name = "get_table_schema"), Description(
        "Get the schema information for a specific table including column details.")]
    public string GetTableSchema(
        [Description("Name of the table")] string tableName,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        IdentifierGuard.ValidateTableName(entry.Connection, tableName);
        var schema = SqliteCommandRunner.Query(
            entry.Connection,
            $"PRAGMA table_info({IdentifierGuard.QuoteIdentifier(tableName)})");
        return SqliteCommandRunner.ToJson(schema);
    }
}
