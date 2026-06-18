namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A thread-safe dictionary that keeps its values in the order defined by an
/// injected comparer and supports atomic read-modify-write through caller-supplied
/// delegates. The values' sort positions are kept current as values are added and
/// replaced.
/// </summary>
/// <remarks>
/// Values are reference types; callers must treat returned values as read-only
/// (mutating one is undefined behaviour), which is what lets reads hand them out
/// without copying. The comparer must impose a total order — no two values that are
/// stored at the same time may compare equal, or the equal one is dropped.
/// </remarks>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type; must be a reference type.</typeparam>
internal sealed class SortedConcurrentDictionary<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, TValue> index = new();
    private readonly SortedSet<TValue> ordered;

    /// <summary>Creates a store that orders values by <paramref name="comparer"/>.</summary>
    /// <param name="comparer">A total order over the values.</param>
    public SortedConcurrentDictionary(IComparer<TValue> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        this.ordered = new SortedSet<TValue>(comparer);
    }

    /// <summary>The number of entries currently stored.</summary>
    public int Count
    {
        get
        {
            lock (this.gate)
            {
                return this.index.Count;
            }
        }
    }

    /// <summary>Adds <paramref name="value"/> under <paramref name="key"/> if the key is not already present.</summary>
    /// <returns><see langword="true"/> if added; <see langword="false"/> if the key already exists.</returns>
    public bool TryAdd(TKey key, TValue value)
    {
        lock (this.gate)
        {
            if (this.index.ContainsKey(key))
            {
                return false;
            }

            this.index[key] = value;
            this.ordered.Add(value);
            return true;
        }
    }

    /// <summary>Gets the value stored under <paramref name="key"/>.</summary>
    public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var current))
            {
                value = current;
                return true;
            }

            value = null;
            return false;
        }
    }

    /// <summary>
    /// Atomically replaces the value under <paramref name="key"/> with the result of
    /// <paramref name="transform"/>, repositioning it in the sort order. The transform
    /// runs under the lock; returning <see langword="null"/> aborts the update and
    /// leaves the value unchanged.
    /// </summary>
    /// <returns><see langword="true"/> when the value was replaced; otherwise <see langword="false"/>.</returns>
    public bool TryUpdate(TKey key, Func<TValue, TValue?> transform, [NotNullWhen(true)] out TValue? updated)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var current) && transform(current) is { } next)
            {
                this.ordered.Remove(current);
                this.ordered.Add(next);
                this.index[key] = next;
                updated = next;
                return true;
            }

            updated = null;
            return false;
        }
    }

    /// <summary>Removes the entry under <paramref name="key"/> only if <paramref name="predicate"/> holds.</summary>
    /// <returns><see langword="true"/> when the entry was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveIf(TKey key, Func<TValue, bool> predicate)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var current) && predicate(current))
            {
                this.index.Remove(key);
                this.ordered.Remove(current);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> stored values that match
    /// <paramref name="predicate"/>, in sort order. The predicate runs under the lock,
    /// so it must be fast and free of side effects.
    /// </summary>
    public IReadOnlyList<TValue> Take(Func<TValue, bool> predicate, int max)
    {
        var result = new List<TValue>();
        lock (this.gate)
        {
            foreach (var value in this.ordered)
            {
                if (result.Count >= max)
                {
                    break;
                }

                if (predicate(value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }
}
