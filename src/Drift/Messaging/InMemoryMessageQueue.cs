namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindyCliffs.Clock;

/// <summary>
/// An in-process <see cref="IMessageQueue{TPayload}"/> backed by an in-memory store
/// ordered by <see cref="IMessageMetadata.LastModifiedAt"/> (least-recently-modified
/// first). Suitable for tests, local development, and single-process scenarios;
/// state is not durable and does not survive a process restart. Thread-safe.
/// </summary>
/// <typeparam name="TPayload">The payload type carried by the queued messages.</typeparam>
public sealed class InMemoryMessageQueue<TPayload>(IClock clock) : IMessageQueue<TPayload>
    where TPayload : notnull
{
    // Orders messages by last-modified time (oldest first), breaking ties by id so the
    // comparer is a total order over distinct messages.
    private static readonly IComparer<Entry> ByLastModified = Comparer<Entry>.Create(static (a, b) =>
    {
        var byTime = a.LastModifiedAt.CompareTo(b.LastModifiedAt);
        return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
    });

    private readonly IClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SortedConcurrentDictionary<string, Entry> store = new(ByLastModified);

    /// <summary>Creates a queue that reads the current time from <see cref="SystemClock.Instance"/>.</summary>
    public InMemoryMessageQueue()
        : this(SystemClock.Instance)
    {
    }

    /// <inheritdoc />
    public Task<IMessage<TPayload>> PutAsync(string id, TPayload payload, MessagePutOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(options);

        var now = this.clock.UtcNow;
        var entry = new Entry(id, options.MessageType, NewVersion(), now, now, options.ExpiresAt, options.InvisibleBefore, options.Tags, payload, null);
        if (!this.store.TryAdd(id, entry))
        {
            throw new InvalidOperationException($"A message with id '{id}' already exists.");
        }

        return Task.FromResult(Snapshot(entry));
    }

    /// <inheritdoc />
    public Task<IMessage<TPayload>?> TryGetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Task.FromResult<IMessage<TPayload>?>(this.store.TryGetValue(id, out var entry) ? Snapshot(entry) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IMessage<TPayload>>> TakeAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be greater than zero.");
        }

        var now = this.clock.UtcNow;
        IReadOnlyList<IMessage<TPayload>> visible = this.store
            .Take(e => IsVisible(e, now), count)
            .Select(Snapshot)
            .ToList();
        return Task.FromResult(visible);
    }

    /// <inheritdoc />
    public Task<IMessageLease<TPayload>?> LeaseAsync(IMessage<TPayload> message, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfNonPositive(leaseDuration);

        var now = this.clock.UtcNow;
        var leasedUntil = now + leaseDuration;
        var token = Guid.NewGuid();
        if (this.store.TryUpdate(
                message.Metadata.Id,
                e => e.Version == message.Metadata.Version && IsVisible(e, now)
                    ? e with { LeaseToken = token, InvisibleBefore = leasedUntil, Version = NewVersion(), LastModifiedAt = now }
                    : null,
                out var leased))
        {
            IMessageLease<TPayload> lease = new Lease(this, leased.Id, token, Snapshot(leased), leasedUntil);
            return Task.FromResult<IMessageLease<TPayload>?>(lease);
        }

        // Unknown/foreign id, a stale snapshot, or currently invisible — cannot be claimed.
        return Task.FromResult<IMessageLease<TPayload>?>(null);
    }

    /// <inheritdoc />
    public Task<long> EstimateCountAsync(CancellationToken ct = default) => Task.FromResult((long)this.store.Count);

    private static string NewVersion() => Guid.NewGuid().ToString("n");

    private static void ThrowIfNonPositive(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be positive.");
        }
    }

    private static bool IsVisible(Entry e, DateTimeOffset now) =>
        (e.InvisibleBefore is null || now >= e.InvisibleBefore)
        && (e.ExpiresAt is null || now < e.ExpiresAt);

    private static IMessage<TPayload> Snapshot(Entry e) =>
        new MessageSnapshot(
            new MetadataSnapshot(e.Id, e.MessageType, e.Version, e.CreatedAt, e.LastModifiedAt, e.ExpiresAt, e.InvisibleBefore, e.Tags),
            e.Payload);

    private sealed record Entry(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore,
        IReadOnlyList<string> Tags,
        TPayload Payload,
        Guid? LeaseToken);

    private sealed record MetadataSnapshot(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore,
        IReadOnlyList<string> Tags) : IMessageMetadata;

    private sealed record MessageSnapshot(IMessageMetadata Metadata, TPayload Payload) : IMessage<TPayload>;

    private sealed class Lease(
        InMemoryMessageQueue<TPayload> queue,
        string id,
        Guid token,
        IMessage<TPayload> message,
        DateTimeOffset leasedUntil) : IMessageLease<TPayload>
    {
        private readonly InMemoryMessageQueue<TPayload> queue = queue;
        private readonly string id = id;
        private readonly Guid token = token;

        public IMessage<TPayload> Message { get; private set; } = message;

        public DateTimeOffset LeasedUntil { get; private set; } = leasedUntil;

        public Task UpdateAsync(MessageUpdate<TPayload> update, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(update);

            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.TryUpdate(this.id, e => this.IsHeldBy(e, now) ? Apply(e, update, now) : null, out var updated))
            {
                throw NotHeld();
            }

            this.Message = Snapshot(updated);
            return Task.CompletedTask;
        }

        public Task RenewAsync(TimeSpan leaseDuration, CancellationToken ct = default)
        {
            ThrowIfNonPositive(leaseDuration);

            var now = this.queue.clock.UtcNow;
            var leasedUntil = now + leaseDuration;
            if (!this.queue.store.TryUpdate(
                    this.id,
                    e => this.IsHeldBy(e, now) ? e with { InvisibleBefore = leasedUntil, Version = NewVersion(), LastModifiedAt = now } : null,
                    out var updated))
            {
                throw NotHeld();
            }

            this.LeasedUntil = leasedUntil;
            this.Message = Snapshot(updated);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken ct = default)
        {
            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.TryUpdate(
                    this.id,
                    e => this.IsHeldBy(e, now) ? e with { LeaseToken = null, InvisibleBefore = null, Version = NewVersion(), LastModifiedAt = now } : null,
                    out _))
            {
                throw NotHeld();
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken ct = default)
        {
            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.RemoveIf(this.id, e => this.IsHeldBy(e, now)))
            {
                throw NotHeld();
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // Idempotent safety net: release the lease if it is still held, else no-op.
            var now = this.queue.clock.UtcNow;
            _ = this.queue.store.TryUpdate(
                this.id,
                e => this.IsHeldBy(e, now) ? e with { LeaseToken = null, InvisibleBefore = null, Version = NewVersion(), LastModifiedAt = now } : null,
                out _);
            return ValueTask.CompletedTask;
        }

        private static InvalidOperationException NotHeld() => new("The lease is no longer held.");

        private static Entry Apply(Entry entry, MessageUpdate<TPayload> update, DateTimeOffset now) =>
            entry with
            {
                Payload = update.Payload is { } payload ? payload : entry.Payload,
                ExpiresAt = update.ExpiresAt ?? entry.ExpiresAt,
                InvisibleBefore = update.InvisibleBefore ?? entry.InvisibleBefore,
                Tags = update.Tags ?? entry.Tags,
                Version = NewVersion(),
                LastModifiedAt = now,
            };

        private bool IsHeldBy(Entry entry, DateTimeOffset now) => entry.LeaseToken == this.token && now < this.LeasedUntil;
    }
}
