using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SqliteMcp.Sql;

/// <summary>
/// Executes parameterized SQL and serializes results for MCP tool responses.
/// </summary>
public static class SqliteCommandRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Serializes a value as indented camelCase JSON text.</summary>
    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>
    /// Runs a query (typically SELECT) and returns rows as dictionaries.
    /// Supports <c>?</c> placeholders rewritten to named parameters.
    /// </summary>
    public static List<Dictionary<string, object?>> Query(
        SqliteConnection connection,
        string sql,
        IReadOnlyList<object?>? values = null)
    {
        using var cmd = CreateCommand(connection, sql, values);
        var rows = new List<Dictionary<string, object?>>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Runs a non-query statement and returns affected row count plus <c>last_insert_rowid()</c>.
    /// </summary>
    public static (int Changes, long LastInsertRowId) Execute(
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

        return (changes, lastInsertRowId);
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
