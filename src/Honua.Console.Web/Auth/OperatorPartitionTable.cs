using System.Collections.Concurrent;

namespace Honua.Console.Web.Auth;

/// <summary>
/// A bounded, per-operator partition map shared by the operator-scoped profile and account-session
/// stores (honua-console#279 PA-237). Those stores previously kept one backing partition per operator in
/// an unbounded <see cref="ConcurrentDictionary{TKey,TValue}"/> that was never pruned, so every operator
/// that ever signed in — including short-lived edge-forwarded identities that never return — leaked its
/// partition for the life of the process.
///
/// This table mirrors the bound the #305-era BFF cookie-jar store established
/// (<c>ConsoleServerSessionClientStore</c>: a hard capacity plus idle-lifetime pruning): partitions are
/// evicted explicitly on sign-out (<see cref="Evict"/>), pruned when idle past
/// <see cref="IdleLifetime"/> (aligned with the 8h auth-cookie lifetime), and capped at
/// <see cref="MaximumOperators"/> with least-recently-used eviction so a burst of distinct operators
/// cannot grow the map without limit. Reads/writes touch the partition so an active operator is never a
/// prune/eviction candidate.
/// </summary>
internal sealed class OperatorPartitionTable<TPartition>
{
    /// <summary>Hard cap on live operator partitions, matching the BFF cookie-jar store's bound.</summary>
    internal const int MaximumOperators = 2_048;

    /// <summary>Idle window after which an untouched partition is pruned, aligned with the 8h cookie.</summary>
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

    /// <summary>Current live partition count (test/diagnostic surface).</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns the operator's partition, creating it from <paramref name="factory"/> on first use. Every
    /// call refreshes the partition's last-access stamp and opportunistically prunes idle/over-capacity
    /// partitions.
    /// </summary>
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

    /// <summary>Removes the operator's partition (invoked on sign-out). No-op if absent.</summary>
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
