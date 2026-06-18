namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindyCliffs.Clock;

/// <summary>
/// An in-process <see cref="IMessageQueue"/> backed by an in-memory store ordered by
/// <see cref="IMessage.LastModifiedAt"/> (least-recently-modified first). Suitable for
/// tests, local development, and single-process scenarios; state is not durable and
/// does not survive a process restart. Thread-safe.
/// </summary>
public sealed class InMemoryMessageQueue(IClock clock) : IMessageQueue
{
    // Orders messages by last-modified time (oldest first), breaking ties by id.
    // The id tie-break is essential: SortedSet treats values that compare equal as
    // duplicates and silently drops the later one, so without it two messages sharing
    // a LastModifiedAt would be lost.
    private static readonly IComparer<Entry> ByLastModified = Comparer<Entry>.Create(static (a, b) =>
    {
        var byTime = a.LastModifiedAt.CompareTo(b.LastModifiedAt);
        return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
    });

    private readonly IClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SortedConcurrentDictionary<string, Entry> store = new(ByLastModified);

    /// <summary>
    /// Creates a queue that reads the current time from <see cref="SystemClock.Instance"/>.
    /// Use the <see cref="InMemoryMessageQueue(IClock)"/> constructor to control time in tests.
    /// </summary>
    public InMemoryMessageQueue()
        : this(SystemClock.Instance)
    {
    }

    /// <inheritdoc />
    public Task<IMessage> PutAsync<TPayload>(string id, TPayload payload, MessagePutOptions options, CancellationToken ct = default)
        where TPayload : notnull
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(options);

        var now = this.clock.UtcNow;
        var entry = new Entry(id, options.MessageType, NewVersion(), now, now, options.ExpiresAt, options.InvisibleBefore, options.Tags, payload);
        if (!this.store.TryAdd(id, entry))
        {
            throw new InvalidOperationException($"A message with id '{id}' already exists.");
        }

        return Task.FromResult(Snapshot(entry));
    }

    /// <inheritdoc />
    public Task<IMessage?> TryGetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Task.FromResult<IMessage?>(this.store.TryGetValue(id, out var entry) ? Snapshot(entry) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IMessage>> TakeAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be greater than zero.");
        }

        var now = this.clock.UtcNow;
        IReadOnlyList<IMessage> visible = this.store
            .Take(e => IsVisible(e, now), count)
            .Select(Snapshot)
            .ToList();
        return Task.FromResult(visible);
    }

    /// <inheritdoc />
    public Task<IMessageLease?> LeaseAsync(IMessage message, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfNonPositive(leaseDuration);

        var now = this.clock.UtcNow;
        var leasedUntil = now + leaseDuration;
        var token = Guid.NewGuid();
        if (this.store.TryUpdate(
                message.Id,
                e => e.Version == message.Version && IsVisible(e, now)
                    ? e with { LeaseToken = token, LeaseExpiresAt = leasedUntil, Version = NewVersion(), LastModifiedAt = now }
                    : null,
                out var leased))
        {
            IMessageLease lease = new Lease(this, leased.Id, token, Snapshot(leased), leasedUntil);
            return Task.FromResult<IMessageLease?>(lease);
        }

        // Unknown/foreign id, a stale snapshot, or currently invisible — cannot be claimed.
        return Task.FromResult<IMessageLease?>(null);
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

    // A message is visible when it is not currently leased, not awaiting a scheduled
    // visibility time, and not expired. The lease hold is tracked separately from
    // InvisibleBefore so a caller-supplied InvisibleBefore can never surface an
    // actively-leased message.
    private static bool IsVisible(Entry e, DateTimeOffset now) =>
        (e.LeaseToken is null || now >= e.LeaseExpiresAt)
        && (e.InvisibleBefore is null || now >= e.InvisibleBefore)
        && (e.ExpiresAt is null || now < e.ExpiresAt);

    private static IMessage Snapshot(Entry e) =>
        new MessageSnapshot(e.Id, e.MessageType, e.Version, e.CreatedAt, e.LastModifiedAt, e.ExpiresAt, e.InvisibleBefore, e.Tags, e.Payload);

    private sealed record Entry(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore,
        IReadOnlyList<string> Tags,
        object Payload,
        Guid? LeaseToken = null,
        DateTimeOffset? LeaseExpiresAt = null);

    private sealed record MessageSnapshot(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore,
        IReadOnlyList<string> Tags,
        object Payload) : IMessage
    {
        public TPayload GetPayload<TPayload>()
            where TPayload : notnull
        {
            if (this.Payload is TPayload typed)
            {
                return typed;
            }

            throw new InvalidCastException(
                $"Message '{this.Id}' (type '{this.MessageType}') carries a payload of type " +
                $"'{this.Payload.GetType()}'; it cannot be read as '{typeof(TPayload)}'.");
        }
    }

    private sealed class Lease(
        InMemoryMessageQueue queue,
        string id,
        Guid token,
        IMessage message,
        DateTimeOffset leasedUntil) : IMessageLease
    {
        private readonly InMemoryMessageQueue queue = queue;
        private readonly string id = id;
        private readonly Guid token = token;

        public IMessage Message { get; private set; } = message;

        public DateTimeOffset LeasedUntil { get; private set; } = leasedUntil;

        public Task UpdateAsync(MessageUpdate update, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            return this.Apply(update, payload: null);
        }

        public Task UpdateAsync<TPayload>(TPayload payload, MessageUpdate update, CancellationToken ct = default)
            where TPayload : notnull
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(update);
            return this.Apply(update, payload);
        }

        public Task RenewAsync(TimeSpan leaseDuration, CancellationToken ct = default)
        {
            ThrowIfNonPositive(leaseDuration);

            var now = this.queue.clock.UtcNow;
            var leasedUntil = now + leaseDuration;
            if (!this.queue.store.TryUpdate(
                    this.id,
                    e => this.IsHeldBy(e, now) ? e with { LeaseExpiresAt = leasedUntil, Version = NewVersion(), LastModifiedAt = now } : null,
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
                    e => this.IsHeldBy(e, now) ? e with { LeaseToken = null, LeaseExpiresAt = null, Version = NewVersion(), LastModifiedAt = now } : null,
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
                e => this.IsHeldBy(e, now) ? e with { LeaseToken = null, LeaseExpiresAt = null, Version = NewVersion(), LastModifiedAt = now } : null,
                out _);
            return ValueTask.CompletedTask;
        }

        private static InvalidOperationException NotHeld() => new("The lease is no longer held.");

        private Task Apply(MessageUpdate update, object? payload)
        {
            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.TryUpdate(
                    this.id,
                    e => this.IsHeldBy(e, now)
                        ? e with
                        {
                            Payload = payload ?? e.Payload,
                            ExpiresAt = update.ExpiresAt ?? e.ExpiresAt,
                            InvisibleBefore = update.InvisibleBefore ?? e.InvisibleBefore,
                            Tags = update.Tags ?? e.Tags,
                            Version = NewVersion(),
                            LastModifiedAt = now,
                        }
                        : null,
                    out var updated))
            {
                throw NotHeld();
            }

            this.Message = Snapshot(updated);
            return Task.CompletedTask;
        }

        private bool IsHeldBy(Entry entry, DateTimeOffset now) => entry.LeaseToken == this.token && now < entry.LeaseExpiresAt;
    }
}
