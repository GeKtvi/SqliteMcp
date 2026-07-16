namespace SqliteMcp.Hooks;

public interface ICliHookRunner
{
    void Run(HookEventKind eventKind, HookPhase phase, HookContext context);
}
