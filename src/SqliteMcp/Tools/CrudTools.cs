using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SqliteMcp.Json;
using SqliteMcp.Sql;

namespace SqliteMcp.Tools;

/// <summary>
/// MCP tools for validated create/read/update/delete against named tables.
/// </summary>
[McpServerToolType]
public sealed class CrudTools(SqliteConnectionManager connections)
{
    /// <summary>Inserts one row after validating table and column names.</summary>
    [McpServerTool(Name = "create_record"), Description(
        "Insert a new record into a table with specified data.")]
    public string CreateRecord(
        [Description("Name of the table")] string table,
        [Description("Record data as key-value pairs (column → value)")] JsonElement data,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var record = IdentifierGuard.ParseRecord(data, nameof(data));
        if (record.Count == 0)
        {
            throw new InvalidOperationException("data must contain at least one column.");
        }

        IdentifierGuard.ValidateTableName(entry.Connection, table);
        IdentifierGuard.ValidateColumnNames(entry.Connection, table, record.Keys);

        var columns = record.Keys.ToList();
        var placeholders = string.Join(", ", columns.Select(_ => "?"));
        var quotedColumns = string.Join(", ", columns.Select(IdentifierGuard.QuoteIdentifier));
        var sql =
            $"INSERT INTO {IdentifierGuard.QuoteIdentifier(table)} ({quotedColumns}) VALUES ({placeholders})";

        var result = SqliteCommandRunner.Execute(
            entry.Connection,
            sql,
            [.. columns.Select(c => record[c])]);

        return SqliteCommandRunner.ToJson(
            new CreateRecordResult("Record created successfully", result.LastInsertRowId),
            AppJsonContext.Default.CreateRecordResult);
    }

    /// <summary>Selects rows with optional equality filters, limit, and offset.</summary>
    [McpServerTool(Name = "read_records"), Description(
        "Read records from a table with optional equality conditions, limit, and offset.")]
    public string ReadRecords(
        [Description("Name of the table")] string table,
        [Description("Equality filters (column = value), AND-combined")] JsonElement? conditions = null,
        [Description("Maximum number of rows to return")] double? limit = null,
        [Description("Number of rows to skip (requires limit)")] double? offset = null,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        IdentifierGuard.ValidateTableName(entry.Connection, table);

        var sql = $"SELECT * FROM {IdentifierGuard.QuoteIdentifier(table)}";
        var values = new List<object?>();
        var filter = IdentifierGuard.ParseOptionalRecord(conditions, nameof(conditions));

        if (filter is { Count: > 0 })
        {
            IdentifierGuard.ValidateColumnNames(entry.Connection, table, filter.Keys);
            var where = string.Join(" AND ", filter.Keys.Select(col =>
            {
                values.Add(filter[col]);
                return $"{IdentifierGuard.QuoteIdentifier(col)} = ?";
            }));
            sql += $" WHERE {where}";
        }

        if (limit is not null)
        {
            if (limit < 0 || limit != Math.Floor(limit.Value))
            {
                throw new ArgumentException("limit must be a non-negative integer.");
            }

            sql += $" LIMIT {(long)limit.Value}";
            if (offset is not null)
            {
                if (offset < 0 || offset != Math.Floor(offset.Value))
                {
                    throw new ArgumentException("offset must be a non-negative integer.");
                }

                sql += $" OFFSET {(long)offset.Value}";
            }
        }

        var rows = SqliteCommandRunner.Query(entry.Connection, sql, values);
        return SqliteCommandRunner.ToJson(rows);
    }

    /// <summary>Updates rows; refuses empty conditions to avoid full-table updates.</summary>
    [McpServerTool(Name = "update_records"), Description(
        "Update records in a table based on specified conditions. " +
        "WARNING: conditions must be non-empty to prevent accidental full-table updates.")]
    public string UpdateRecords(
        [Description("Name of the table")] string table,
        [Description("New column values")] JsonElement data,
        [Description("Equality filters (column = value), AND-combined; must not be empty")] JsonElement conditions,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var record = IdentifierGuard.ParseRecord(data, nameof(data));
        var filter = IdentifierGuard.ParseRecord(conditions, nameof(conditions));

        if (record.Count == 0)
        {
            throw new InvalidOperationException("data must contain at least one column.");
        }

        if (filter.Count == 0)
        {
            throw new InvalidOperationException(
                "conditions must not be empty. Refusing to update all rows.");
        }

        IdentifierGuard.ValidateTableName(entry.Connection, table);
        IdentifierGuard.ValidateColumnNames(
            entry.Connection,
            table,
            record.Keys.Concat(filter.Keys));

        var values = new List<object?>();
        var setParts = new List<string>();
        foreach (var (col, value) in record)
        {
            setParts.Add($"{IdentifierGuard.QuoteIdentifier(col)} = ?");
            values.Add(value);
        }

        var whereParts = new List<string>();
        foreach (var (col, value) in filter)
        {
            whereParts.Add($"{IdentifierGuard.QuoteIdentifier(col)} = ?");
            values.Add(value);
        }

        var sql =
            $"UPDATE {IdentifierGuard.QuoteIdentifier(table)} SET {string.Join(", ", setParts)} " +
            $"WHERE {string.Join(" AND ", whereParts)}";

        var result = SqliteCommandRunner.Execute(entry.Connection, sql, values);
        return SqliteCommandRunner.ToJson(
            new RowsAffectedResult("Records updated successfully", result.Changes),
            AppJsonContext.Default.RowsAffectedResult);
    }

    /// <summary>Deletes rows; refuses empty conditions to avoid full-table deletes.</summary>
    [McpServerTool(Name = "delete_records"), Description(
        "Delete records from a table based on specified conditions. " +
        "WARNING: conditions must be non-empty to prevent accidental full-table deletes.")]
    public string DeleteRecords(
        [Description("Name of the table")] string table,
        [Description("Equality filters (column = value), AND-combined; must not be empty")] JsonElement conditions,
        [Description("Connection key. Omit to use the default database.")] string? connectionKey = null)
    {
        var entry = connections.GetConnection(connectionKey);
        var filter = IdentifierGuard.ParseRecord(conditions, nameof(conditions));

        if (filter.Count == 0)
        {
            throw new InvalidOperationException(
                "conditions must not be empty. Refusing to delete all rows.");
        }

        IdentifierGuard.ValidateTableName(entry.Connection, table);
        IdentifierGuard.ValidateColumnNames(entry.Connection, table, filter.Keys);

        var values = new List<object?>();
        var whereParts = filter.Keys.Select(col =>
        {
            values.Add(filter[col]);
            return $"{IdentifierGuard.QuoteIdentifier(col)} = ?";
        });

        var sql =
            $"DELETE FROM {IdentifierGuard.QuoteIdentifier(table)} WHERE {string.Join(" AND ", whereParts)}";

        var result = SqliteCommandRunner.Execute(entry.Connection, sql, values);
        return SqliteCommandRunner.ToJson(
            new RowsAffectedResult("Records deleted successfully", result.Changes),
            AppJsonContext.Default.RowsAffectedResult);
    }
}
