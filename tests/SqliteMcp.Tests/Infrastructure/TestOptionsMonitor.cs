using Microsoft.Extensions.Options;

namespace SqliteMcp.Tests.Infrastructure;

/// <summary>
/// Mutable <see cref="IOptionsMonitor{T}"/> for tests (supports CurrentValue updates and OnChange).
/// </summary>
internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];
    private T _currentValue = value;

    public T CurrentValue => _currentValue;

    public T Get(string? name) => _currentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new ListenerSubscription(this, listener);
    }

    public void Set(T value)
    {
        _currentValue = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, Options.DefaultName);
        }
    }

    private sealed class ListenerSubscription(
        TestOptionsMonitor<T> owner,
        Action<T, string?> listener) : IDisposable
    {
        public void Dispose() => owner._listeners.Remove(listener);
    }
}
