namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An in-process <see cref="IMessageQueue{TPayload}"/> backed by an in-memory
/// dictionary. Suitable for tests, local development, and single-process
/// scenarios; state is not durable and does not survive a process restart.
/// Thread-safe via a single lock.
/// </summary>
/// <typeparam name="TPayload">The payload type carried by the queued messages.</typeparam>
public sealed class InMemoryMessageQueue<TPayload>(Func<DateTimeOffset> clock) : IMessageQueue<TPayload>
    where TPayload : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new();
    private readonly Func<DateTimeOffset> clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private long nextId;
    private long nextVersion;

    /// <summary>Creates a queue that reads the current time from the system UTC clock.</summary>
    public InMemoryMessageQueue()
        : this(static () => DateTimeOffset.UtcNow)
    {
    }

    /// <inheritdoc />
    public Task<IMessage<TPayload>> PutAsync(TPayload payload, MessagePutOptions options, CancellationToken ct = default)
    {
        lock (this.gate)
        {
            var id = "msg-" + (++this.nextId).ToString(CultureInfo.InvariantCulture);
            var entry = new Entry
            {
                Id = id,
                MessageType = options.MessageType,
                CreatedAt = this.clock(),
                Payload = payload,
                Version = this.NewVersion(),
                ExpiresAt = options.ExpiresAt,
                InvisibleBefore = options.InvisibleBefore,
            };
            this.entries[id] = entry;
            return Task.FromResult(Snapshot(entry));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IMessage<TPayload>>> TakeAsync(int count, CancellationToken ct = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be greater than zero.");
        }

        lock (this.gate)
        {
            var now = this.clock();
            IReadOnlyList<IMessage<TPayload>> visible = this.entries.Values
                .Where(e => IsVisible(e, now))
                .Take(count)
                .Select(Snapshot)
                .ToList();
            return Task.FromResult(visible);
        }
    }

    /// <inheritdoc />
    public Task<IMessageLease<TPayload>?> LeaseAsync(IMessage<TPayload> message, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        lock (this.gate)
        {
            var now = this.clock();
            if (!this.entries.TryGetValue(message.Metadata.Id, out var entry)
                || entry.Version != message.Metadata.Version
                || !IsVisible(entry, now))
            {
                // Unknown/foreign id, a stale snapshot (the message has since changed),
                // or the message is currently invisible — cannot be claimed.
                return Task.FromResult<IMessageLease<TPayload>?>(null);
            }

            var token = Guid.NewGuid();
            var leasedUntil = now + leaseDuration;
            entry.LeaseToken = token;
            entry.InvisibleBefore = leasedUntil;
            entry.Version = this.NewVersion();
            IMessageLease<TPayload> lease = new Lease(this, entry.Id, token, Snapshot(entry), leasedUntil);
            return Task.FromResult<IMessageLease<TPayload>?>(lease);
        }
    }

    /// <inheritdoc />
    public Task<long> EstimateCountAsync(CancellationToken ct = default)
    {
        lock (this.gate)
        {
            return Task.FromResult((long)this.entries.Count);
        }
    }

    private string NewVersion() => "v" + (++this.nextVersion).ToString(CultureInfo.InvariantCulture);

    private static bool IsVisible(Entry e, DateTimeOffset now) =>
        (e.InvisibleBefore is null || now >= e.InvisibleBefore)
        && (e.ExpiresAt is null || now < e.ExpiresAt);

    private static IMessage<TPayload> Snapshot(Entry e) =>
        new MessageSnapshot(
            new MetadataSnapshot(e.Id, e.MessageType, e.Version, e.CreatedAt, e.ExpiresAt, e.InvisibleBefore),
            e.Payload);

    private sealed class Entry
    {
        public required string Id { get; init; }

        public required string MessageType { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required TPayload Payload { get; set; }

        public required string Version { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public DateTimeOffset? InvisibleBefore { get; set; }

        public Guid? LeaseToken { get; set; }
    }

    private sealed record MetadataSnapshot(
        string Id,
        string MessageType,
        string Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? InvisibleBefore) : IMessageMetadata;

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
        private bool held = true;

        public IMessage<TPayload> Message { get; private set; } = message;

        public DateTimeOffset LeasedUntil { get; } = leasedUntil;

        public Task UpdateAsync(MessageUpdate<TPayload> update, CancellationToken ct = default)
        {
            lock (this.queue.gate)
            {
                var entry = this.RequireHeld();
                entry.Payload = update.Payload;
                if (update.ExpiresAt is not null)
                {
                    entry.ExpiresAt = update.ExpiresAt;
                }

                entry.Version = this.queue.NewVersion();
                this.Message = Snapshot(entry);
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken ct = default)
        {
            lock (this.queue.gate)
            {
                var entry = this.RequireHeld();
                entry.InvisibleBefore = null;
                entry.LeaseToken = null;
                entry.Version = this.queue.NewVersion();
                this.held = false;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken ct = default)
        {
            lock (this.queue.gate)
            {
                _ = this.RequireHeld();
                this.queue.entries.Remove(this.id);
                this.held = false;
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (this.queue.gate)
            {
                if (this.held
                    && this.queue.clock() < this.LeasedUntil
                    && this.queue.entries.TryGetValue(this.id, out var entry)
                    && entry.LeaseToken == this.token)
                {
                    entry.InvisibleBefore = null;
                    entry.LeaseToken = null;
                    entry.Version = this.queue.NewVersion();
                }

                this.held = false;
            }

            return ValueTask.CompletedTask;
        }

        private Entry RequireHeld()
        {
            if (!this.held || this.queue.clock() >= this.LeasedUntil)
            {
                throw new InvalidOperationException("The lease is no longer held.");
            }

            if (!this.queue.entries.TryGetValue(this.id, out var entry) || entry.LeaseToken != this.token)
            {
                throw new InvalidOperationException("The lease is no longer held.");
            }

            return entry;
        }
    }
}
