using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SqliteMcp.Sql;

/// <summary>
/// Validates and quotes SQL identifiers for safe dynamic CRUD against user-named tables/columns.
/// </summary>
public static class IdentifierGuard
{
    /// <summary>
    /// Quotes an identifier with double quotes, escaping embedded quotes.
    /// </summary>
    public static string QuoteIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// Ensures <paramref name="tableName"/> exists as a user table in <c>sqlite_master</c>.
    /// </summary>
    public static void ValidateTableName(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name = $name
            """;
        cmd.Parameters.AddWithValue("$name", tableName);
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull)
        {
            throw new InvalidOperationException($"Table \"{tableName}\" does not exist");
        }
    }

    /// <summary>
    /// Ensures every name in <paramref name="columnNames"/> exists on <paramref name="tableName"/>.
    /// </summary>
    public static void ValidateColumnNames(
        SqliteConnection connection,
        string tableName,
        IEnumerable<string> columnNames)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            valid.Add(reader.GetString(1));
        }

        foreach (var col in columnNames)
        {
            if (!valid.Contains(col))
            {
                throw new InvalidOperationException(
                    $"Column \"{col}\" does not exist in table \"{tableName}\"");
            }
        }
    }

    /// <summary>
    /// Parses a required JSON object into column → CLR value pairs for CRUD tools.
    /// </summary>
    public static Dictionary<string, object?> ParseRecord(JsonElement element, string paramName)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{paramName} must be a JSON object.", paramName);
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonValueToClr(property.Value);
        }

        return result;
    }

    /// <summary>
    /// Parses an optional JSON object; returns null when the element is missing or null.
    /// </summary>
    public static Dictionary<string, object?>? ParseOptionalRecord(JsonElement? element, string paramName)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        return ParseRecord(element.Value, paramName);
    }

    /// <summary>
    /// Maps a JSON value to a CLR type suitable for SQLite parameter binding.
    /// </summary>
    public static object? JsonValueToClr(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var l) => l,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    /// <summary>
    /// Assigns a CLR value to a SQLite parameter, converting null to <see cref="DBNull"/>.
    /// </summary>
    public static void BindParameter(SqliteParameter parameter, object? value)
    {
        parameter.Value = value ?? DBNull.Value;
    }
}
