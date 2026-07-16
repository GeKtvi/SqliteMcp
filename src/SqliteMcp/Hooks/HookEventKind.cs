namespace SqliteMcp.Hooks;

public enum HookEventKind
{
    Open,
    Close,
    CloseAll,
    Query
}

public enum HookPhase
{
    Before,
    After
}
