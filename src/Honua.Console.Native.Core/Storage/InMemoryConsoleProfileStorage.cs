namespace Honua.Console.Native.Core.Storage;

public sealed class InMemoryConsoleProfileStorage : IConsoleProfileStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _values.TryGetValue(key, out var value);
            return ValueTask.FromResult(value);
        }
    }

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _values[key] = value;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _values.Remove(key);
        }

        return ValueTask.CompletedTask;
    }
}
