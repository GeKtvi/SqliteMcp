using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;
using SqliteMcp.Json;

namespace SqliteMcp.Sql;

/// <summary>
/// Executes parameterized SQL and serializes results for MCP tool responses (AOT-safe).
/// </summary>
public static class SqliteCommandRunner
{
    private static readonly JsonSerializerOptions NodeWriteOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Serializes a fixed response DTO using source-generated metadata.</summary>
    public static string ToJson<T>(T value, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Serialize(value, typeInfo);

    /// <summary>Serializes a <see cref="JsonNode"/> tree without reflection-based type discovery.</summary>
    public static string ToJson(JsonNode node) => node.ToJsonString(NodeWriteOptions);

    /// <summary>
    /// Runs a query and returns rows as a JSON array of objects.
    /// </summary>
    public static JsonArray Query(
        SqliteConnection connection,
        string sql,
        IReadOnlyList<object?>? values = null)
    {
        using var cmd = CreateCommand(connection, sql, values);
        var rows = new JsonArray();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = ToJsonNode(reader.IsDBNull(i) ? null : reader.GetValue(i));
            }

            rows.Add((JsonNode)row);
        }

        return rows;
    }

    /// <summary>
    /// Runs a non-query statement and returns affected row count plus <c>last_insert_rowid()</c>.
    /// </summary>
    public static DmlResult Execute(
        SqliteConnection connection,
        string sql,
        IReadOnlyList<object?>? values = null)
    {
        using var cmd = CreateCommand(connection, sql, values);
        var changes = cmd.ExecuteNonQuery();

        long lastInsertRowId = 0;
        using (var idCmd = connection.CreateCommand())
        {
            idCmd.CommandText = "SELECT last_insert_rowid();";
            var result = idCmd.ExecuteScalar();
            if (result is not null and not DBNull)
            {
                lastInsertRowId = Convert.ToInt64(result);
            }
        }

        return new DmlResult(changes, lastInsertRowId);
    }

    /// <summary>
    /// Heuristic: whether the SQL should be treated as a result-set query vs a non-query.
    /// </summary>
    public static bool LooksLikeSelect(string sql)
    {
        var trimmed = sql.AsSpan().TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        byte or sbyte or short or ushort or int or uint or long or ulong => JsonValue.Create(Convert.ToInt64(value)),
        float or double or decimal => JsonValue.Create(Convert.ToDouble(value)),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        DateTime dt => JsonValue.Create(dt),
        DateTimeOffset dto => JsonValue.Create(dto),
        Guid g => JsonValue.Create(g.ToString()),
        _ => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
    };

    /// <summary>
    /// Builds a command with <c>?</c> placeholders rewritten to <c>$p0</c>, <c>$p1</c>, …
    /// </summary>
    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        IReadOnlyList<object?>? values)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = RewritePlaceholders(sql, values?.Count ?? 0);

        if (values is null)
        {
            return cmd;
        }

        for (var i = 0; i < values.Count; i++)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = $"$p{i}";
            IdentifierGuard.BindParameter(parameter, values[i]);
            cmd.Parameters.Add(parameter);
        }

        return cmd;
    }

    private static string RewritePlaceholders(string sql, int valueCount)
    {
        if (!sql.Contains('?', StringComparison.Ordinal))
        {
            return sql;
        }

        var parts = sql.Split('?');
        var placeholderCount = parts.Length - 1;
        if (placeholderCount != valueCount)
        {
            throw new InvalidOperationException(
                $"SQL has {placeholderCount} placeholder(s) but {valueCount} value(s) were provided.");
        }

        var rewritten = parts[0];
        for (var i = 0; i < placeholderCount; i++)
        {
            rewritten += $"$p{i}" + parts[i + 1];
        }

        return rewritten;
    }
}
