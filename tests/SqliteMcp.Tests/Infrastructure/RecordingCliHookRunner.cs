namespace SqliteMcp.Tests.Infrastructure;

public sealed record RecordedHookCall(HookEventKind EventKind, HookPhase Phase, HookContext Context);

public sealed class RecordingCliHookRunner : ICliHookRunner
{
    private readonly List<RecordedHookCall> _calls = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<RecordedHookCall> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public void Run(HookEventKind eventKind, HookPhase phase, HookContext context)
    {
        lock (_lock)
        {
            _calls.Add(new RecordedHookCall(eventKind, phase, context));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _calls.Clear();
        }
    }
}

public sealed class NoOpCliHookRunner : ICliHookRunner
{
    public static NoOpCliHookRunner Instance { get; } = new();

    public void Run(HookEventKind eventKind, HookPhase phase, HookContext context)
    {
    }
}
