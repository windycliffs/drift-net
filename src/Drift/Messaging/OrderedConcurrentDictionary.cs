namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// A thread-safe dictionary that preserves insertion order and supports atomic
/// read-modify-write through caller-supplied delegates. Values are reference types;
/// callers must treat returned values as read-only (mutating one is undefined
/// behaviour), which is what lets reads hand them out without copying.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type; must be a reference type.</typeparam>
internal sealed class OrderedConcurrentDictionary<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, LinkedListNode<TValue>> index = new();
    private readonly LinkedList<TValue> order = new();

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

            this.index[key] = this.order.AddLast(value);
            return true;
        }
    }

    /// <summary>Gets the value stored under <paramref name="key"/>.</summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var node))
            {
                value = node.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    /// <summary>
    /// Atomically replaces the value under <paramref name="key"/> with the result of
    /// <paramref name="transform"/>. The transform runs under the lock; returning
    /// <see langword="null"/> aborts the update and leaves the value unchanged.
    /// </summary>
    /// <returns><see langword="true"/> when the value was replaced; otherwise <see langword="false"/>.</returns>
    public bool TryUpdate(TKey key, Func<TValue, TValue?> transform, out TValue updated)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var node) && transform(node.Value) is { } next)
            {
                node.Value = next;
                updated = next;
                return true;
            }

            updated = default!;
            return false;
        }
    }

    /// <summary>Removes the entry under <paramref name="key"/> only if <paramref name="predicate"/> holds.</summary>
    /// <returns><see langword="true"/> when the entry was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveIf(TKey key, Func<TValue, bool> predicate)
    {
        lock (this.gate)
        {
            if (this.index.TryGetValue(key, out var node) && predicate(node.Value))
            {
                this.order.Remove(node);
                this.index.Remove(key);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns up to <paramref name="max"/> stored values that match
    /// <paramref name="predicate"/>, in insertion order. The predicate runs under the
    /// lock, so it must be fast and free of side effects.
    /// </summary>
    public IReadOnlyList<TValue> Take(Func<TValue, bool> predicate, int max)
    {
        var result = new List<TValue>();
        lock (this.gate)
        {
            foreach (var value in this.order)
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
