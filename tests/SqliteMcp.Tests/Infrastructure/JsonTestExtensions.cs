using System.Text.Json;

namespace SqliteMcp.Tests.Infrastructure;

/// <summary>
/// Helpers for parsing MCP tool JSON responses and building JsonElement arguments.
/// </summary>
public static class JsonTestExtensions
{
    public static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);

    public static string GetString(this JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"Property '{propertyName}' is null.");
    }

    public static long GetInt64(this JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetInt64();
    }

    public static bool GetBoolean(this JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetBoolean();
    }

    public static JsonElement GetPropertyElement(this JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName);
    }

    public static JsonElement ParseObject(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    public static JsonElement Object(params (string Key, object? Value)[] properties)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            dict[key] = value;
        }

        return JsonSerializer.SerializeToElement(dict);
    }

    public static JsonElement Array(params object?[] values)
    {
        return JsonSerializer.SerializeToElement(values);
    }

    public static JsonElement EmptyObject()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }
}
