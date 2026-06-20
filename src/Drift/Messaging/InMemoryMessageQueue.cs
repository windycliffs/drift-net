namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.IO;
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
public sealed class InMemoryMessageQueue : IMessageQueue
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

    private readonly IClock clock;
    private readonly IMessagePayloadSerializer serializer;
    private readonly SortedConcurrentDictionary<string, Entry> store = new(ByLastModified);

    /// <summary>
    /// Creates a queue that stores payloads serialized with <paramref name="serializer"/>
    /// and reads the current time from <see cref="SystemClock.Instance"/>. Use the
    /// <see cref="InMemoryMessageQueue(IMessagePayloadSerializer, IClock)"/> constructor
    /// to control time in tests.
    /// </summary>
    /// <param name="serializer">Serializes message payloads to and from their stored byte form.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> is null.</exception>
    public InMemoryMessageQueue(IMessagePayloadSerializer serializer)
        : this(serializer, SystemClock.Instance)
    {
    }

    /// <summary>
    /// Creates a queue that stores payloads serialized with <paramref name="serializer"/>
    /// and reads the current time from <paramref name="clock"/>.
    /// </summary>
    /// <param name="serializer">Serializes message payloads to and from their stored byte form.</param>
    /// <param name="clock">The clock supplying timestamps and the current time for visibility.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> or <paramref name="clock"/> is null.</exception>
    public InMemoryMessageQueue(IMessagePayloadSerializer serializer, IClock clock)
    {
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public Task<IMessage> PutAsync<TInput>(string id, TInput input, Action<TInput, IMessageBuilder> builder, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(builder);

        var draft = new MessageBuilder(id, version: null, this.serializer);
        builder(input, draft);

        if (!draft.MessageTypeSet)
        {
            throw new InvalidOperationException($"A message type must be set for message '{id}'.");
        }

        if (!draft.PayloadSet)
        {
            throw new InvalidOperationException($"A payload must be set for message '{id}'.");
        }

        var now = this.clock.UtcNow;
        var entry = new Entry(id, draft.MessageType!, NewVersion(), now, now, draft.ExpiresAt, draft.InvisibleBefore, draft.Tags ?? [], draft.Payload!);
        if (!this.store.TryAdd(id, entry))
        {
            throw new InvalidOperationException($"A message with id '{id}' already exists.");
        }

        return Task.FromResult(this.Snapshot(entry));
    }

    /// <inheritdoc />
    public Task<IMessage?> TryGetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Task.FromResult<IMessage?>(this.store.TryGetValue(id, out var entry) ? this.Snapshot(entry) : null);
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
            .Select(this.Snapshot)
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
            IMessageLease lease = new Lease(this, leased.Id, token, this.Snapshot(leased), leasedUntil);
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

    private IMessage Snapshot(Entry e) =>
        new MessageSnapshot(e.Id, e.MessageType, e.Version, e.CreatedAt, e.LastModifiedAt, e.ExpiresAt, e.InvisibleBefore, e.Tags, e.Payload, this.serializer);

    private sealed record Entry(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastModifiedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore,
        IReadOnlyList<string> Tags,
        byte[] Payload,
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
        byte[] Payload,
        IMessagePayloadSerializer Serializer) : IMessage
    {
        public TPayload GetPayload<TPayload>()
        {
            using var stream = new MemoryStream(this.Payload, writable: false);
            try
            {
                return this.Serializer.Deserialize<TPayload>(stream);
            }
            catch (Exception ex) when (ex is not MessageQueueException)
            {
                throw new MessagePayloadSerializationException(
                    $"Failed to deserialize the payload of message '{this.Id}' (type '{this.MessageType}') as '{typeof(TPayload)}'.",
                    this.Id,
                    typeof(TPayload),
                    ex);
            }
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

        public Task UpdateAsync(Action<IMessage, IMessageBuilder> builder, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return this.Apply(draft => builder(this.Message, draft), applyPayload: false);
        }

        public Task UpdateAsync<TInput>(TInput input, Action<TInput, IMessage, IMessageBuilder> builder, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return this.Apply(draft => builder(input, this.Message, draft), applyPayload: true);
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
                throw this.NotHeld();
            }

            this.LeasedUntil = leasedUntil;
            this.Message = this.queue.Snapshot(updated);
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
                throw this.NotHeld();
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken ct = default)
        {
            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.RemoveIf(this.id, e => this.IsHeldBy(e, now)))
            {
                throw this.NotHeld();
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

        private MessageLeaseLostException NotHeld() => new(this.id);

        // Runs the caller's configure delegate once, outside the store lock, then
        // applies only the properties it set. A property left unset keeps its current
        // value. The payload is applied only by the payload-bearing overload.
        private Task Apply(Action<MessageBuilder> configure, bool applyPayload)
        {
            var draft = new MessageBuilder(this.Message.Id, this.Message.Version, this.queue.serializer);
            configure(draft);

            var now = this.queue.clock.UtcNow;
            if (!this.queue.store.TryUpdate(
                    this.id,
                    e => this.IsHeldBy(e, now)
                        ? e with
                        {
                            Payload = applyPayload && draft.PayloadSet ? draft.Payload! : e.Payload,
                            ExpiresAt = draft.ExpiresAtSet ? draft.ExpiresAt : e.ExpiresAt,
                            InvisibleBefore = draft.InvisibleBeforeSet ? draft.InvisibleBefore : e.InvisibleBefore,
                            Tags = draft.TagsSet ? draft.Tags! : e.Tags,
                            Version = NewVersion(),
                            LastModifiedAt = now,
                        }
                        : null,
                    out var updated))
            {
                throw this.NotHeld();
            }

            this.Message = this.queue.Snapshot(updated);
            return Task.CompletedTask;
        }

        private bool IsHeldBy(Entry entry, DateTimeOffset now) => entry.LeaseToken == this.token && now < entry.LeaseExpiresAt;
    }
}
