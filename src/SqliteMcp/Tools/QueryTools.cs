using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SqliteMcp.Json;
using SqliteMcp.Sql;

namespace SqliteMcp.Tools;

/// <summary>
/// MCP tool for executing raw SQL with optional parameters.
/// </summary>
[McpServerToolType]
public sealed class QueryTools(SqliteConnectionManager connections)
{
    /// <summary>Runs SQL; SELECT-like statements return rows, others return change metadata.</summary>
    [McpServerTool(Name = "query"), Description(
        "Execute a raw SQL statement against the database with optional parameter values. " +
        "SELECT/PRAGMA/WITH return rows; other statements return changes and lastInsertRowId.")]
    public string Query(
        [Description("The SQL statement to execute")] string sql,
        [Description("Optional parameter values for ? placeholders")] JsonElement? values = null,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var bound = ParseValues(values);

        if (SqliteCommandRunner.LooksLikeSelect(sql))
        {
            var rows = SqliteCommandRunner.Query(entry.Connection, sql, bound);
            return SqliteCommandRunner.ToJson(rows);
        }

        var result = SqliteCommandRunner.Execute(entry.Connection, sql, bound);
        return SqliteCommandRunner.ToJson(result, AppJsonContext.Default.DmlResult);
    }

    /// <summary>Converts an optional JSON array of SQL parameter values to CLR objects.</summary>
    private static List<object?>? ParseValues(JsonElement? values)
    {
        if (values is null || values.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (values.Value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("values must be a JSON array.", nameof(values));
        }

        var list = new List<object?>();
        foreach (var item in values.Value.EnumerateArray())
        {
            list.Add(IdentifierGuard.JsonValueToClr(item));
        }

        return list;
    }
}
