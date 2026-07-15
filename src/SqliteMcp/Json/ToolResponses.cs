using System.Text.Json;
using System.Text.Json.Serialization;
namespace SqliteMcp.Json;

public sealed record OpenDbResult(
    string Message,
    string ConnectionKey,
    string Path,
    bool IsDefault);

public sealed record CloseDbResult(
    string Message,
    string ConnectionKey,
    string Path);

public sealed record ClosedConnectionInfo(
    string ConnectionKey,
    string Path);

public sealed record CloseAllResult(
    string Message,
    IReadOnlyList<ClosedConnectionInfo> Closed);

public sealed record ConnectionInfo(
    string ConnectionKey,
    string Path,
    bool IsDefault);

public sealed record ListConnectionsResult(
    string? DefaultPath,
    IReadOnlyList<ConnectionInfo> Connections);

public sealed record DbInfoResult(
    string ConnectionKey,
    string DbPath,
    bool Exists,
    long Size,
    DateTimeOffset? LastModified,
    long TableCount,
    bool IsOpen);

public sealed record EmptyTablesResult(
    string Message,
    string ConnectionKey,
    string DbPath,
    bool Exists,
    long Size);

public sealed record CreateRecordResult(
    string Message,
    long InsertedId);

public sealed record RowsAffectedResult(
    string Message,
    int RowsAffected);

public sealed record DmlResult(
    int Changes,
    long LastInsertRowId);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenDbResult))]
[JsonSerializable(typeof(CloseDbResult))]
[JsonSerializable(typeof(CloseAllResult))]
[JsonSerializable(typeof(ClosedConnectionInfo))]
[JsonSerializable(typeof(IReadOnlyList<ClosedConnectionInfo>))]
[JsonSerializable(typeof(ConnectionInfo))]
[JsonSerializable(typeof(IReadOnlyList<ConnectionInfo>))]
[JsonSerializable(typeof(ListConnectionsResult))]
[JsonSerializable(typeof(DbInfoResult))]
[JsonSerializable(typeof(EmptyTablesResult))]
[JsonSerializable(typeof(CreateRecordResult))]
[JsonSerializable(typeof(RowsAffectedResult))]
[JsonSerializable(typeof(DmlResult))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(double?))]
internal partial class AppJsonContext : JsonSerializerContext;
