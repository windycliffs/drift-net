namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A queue of messages carrying a <typeparamref name="TPayload"/>. Workers read
/// candidate messages with <see cref="TakeAsync"/>, then claim one exclusively
/// with <see cref="LeaseAsync"/> before processing it.
/// </summary>
/// <typeparam name="TPayload">The payload type carried by the queued messages.</typeparam>
public interface IMessageQueue<TPayload>
    where TPayload : notnull
{
    /// <summary>
    /// Puts a payload into the queue. The queue assigns the message identity
    /// (<see cref="IMessageMetadata.Id"/>, an initial <see cref="IMessageMetadata.Version"/>,
    /// and <see cref="IMessageMetadata.CreatedAt"/>) and returns the stored message.
    /// </summary>
    /// <param name="payload">The payload to enqueue.</param>
    /// <param name="options">Metadata supplied by the caller (message type, expiry, initial visibility).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<IMessage<TPayload>> PutAsync(TPayload payload, MessagePutOptions options, CancellationToken ct = default);

    /// <summary>
    /// Reads up to <paramref name="count"/> currently-visible messages. This is a
    /// non-exclusive, non-destructive read: it does not claim the messages, so two
    /// callers may receive the same message. Call <see cref="LeaseAsync"/> to claim
    /// a message for exclusive processing.
    /// </summary>
    /// <param name="count">The maximum number of messages to return; must be greater than zero.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<IReadOnlyList<IMessage<TPayload>>> TakeAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Attempts to lease <paramref name="message"/> for exclusive processing,
    /// located by its <see cref="IMessageMetadata.Id"/>. On success the message is
    /// made invisible until the lease expires and its <see cref="IMessageMetadata.Version"/>
    /// changes.
    /// </summary>
    /// <param name="message">The message to claim, typically obtained from <see cref="TakeAsync"/>.</param>
    /// <param name="leaseDuration">How long the lease is held before it expires.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A lease, or <see langword="null"/> when the message could not be claimed —
    /// unknown or foreign id, a stale snapshot (its <see cref="IMessageMetadata.Version"/>
    /// no longer matches the queue's current value), already leased by another worker,
    /// currently invisible, or already removed.
    /// </returns>
    Task<IMessageLease<TPayload>?> LeaseAsync(IMessage<TPayload> message, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Estimates the number of messages in the queue. The result is approximate and
    /// best-effort; it may be stale by the time it is observed.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<long> EstimateCountAsync(CancellationToken ct = default);
}
