using System.Collections.Concurrent;

namespace Honua.Console.Web.Auth;

// The idle window matches the auth-cookie lifetime, and sign-out removes the partition immediately.
internal sealed class OperatorPartitionTable<TPartition>
{
    internal const int MaximumOperators = 2_048;

    internal static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(8);

    private sealed class Entry(TPartition partition, long lastAccessTicks)
    {
        public TPartition Partition { get; } = partition;
        public long LastAccessTicks = lastAccessTicks;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public OperatorPartitionTable(TimeProvider? timeProvider = null)
        => _timeProvider = timeProvider ?? TimeProvider.System;

    public int Count => _entries.Count;

    public TPartition GetOrAdd(string operatorKey, Func<TPartition> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentNullException.ThrowIfNull(factory);

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        var entry = _entries.GetOrAdd(operatorKey, _ => new Entry(factory(), nowTicks));
        Interlocked.Exchange(ref entry.LastAccessTicks, nowTicks);
        Prune(nowTicks);
        return entry.Partition;
    }

    public void Evict(string operatorKey)
    {
        if (!string.IsNullOrWhiteSpace(operatorKey))
        {
            _entries.TryRemove(operatorKey, out _);
        }
    }

    private void Prune(long nowTicks)
    {
        var idleThreshold = nowTicks - IdleLifetime.Ticks;
        foreach (var pair in _entries)
        {
            if (Interlocked.Read(ref pair.Value.LastAccessTicks) < idleThreshold)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        // Hard capacity backstop: if distinct operators still exceed the cap, evict the
        // least-recently-used partitions until back under the bound.
        var overflow = _entries.Count - MaximumOperators;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var pair in _entries
                     .OrderBy(pair => Interlocked.Read(ref pair.Value.LastAccessTicks))
                     .Take(overflow))
        {
            _entries.TryRemove(pair.Key, out _);
        }
    }
}
