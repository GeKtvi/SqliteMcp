namespace SqliteMcp.Hooks;

public sealed class HookOptions
{
    public TimeSpan? Timeout { get; set; }

    public HookEventOptions Open { get; set; } = new();

    public HookEventOptions Close { get; set; } = new();

    public HookEventOptions CloseAll { get; set; } = new();

    public HookEventOptions Query { get; set; } = new();
}

public sealed class HookEventOptions
{
    public string Before { get; set; } = "";

    public string After { get; set; } = "";

    public TimeSpan? Timeout { get; set; }
}
