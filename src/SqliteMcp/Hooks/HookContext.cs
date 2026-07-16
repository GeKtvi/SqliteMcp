namespace SqliteMcp.Hooks;

public sealed class HookContext
{
    public string? ConnectionKey { get; init; }

    public string? DbPath { get; init; }

    public string? Sql { get; init; }

    public string? ClosedKeys { get; init; }
}
