namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A queue of messages carrying arbitrary payloads. Workers read candidate messages
/// with <see cref="TakeAsync"/>, then claim one exclusively with
/// <see cref="LeaseAsync"/> before processing it.
/// </summary>
public interface IMessageQueue
{
    /// <summary>
    /// Puts a payload into the queue under the caller-supplied <paramref name="id"/>.
    /// The queue assigns the initial <see cref="IMessage.Version"/> and
    /// <see cref="IMessage.CreatedAt"/> and returns the stored message.
    /// </summary>
    /// <typeparam name="TInput">The input for message generation.</typeparam>
    /// <param name="id">The unique identifier for the message. Must be non-empty.</param>
    /// <param name="input">The input for message generation.</param>
    /// <param name="builder">
    /// A delegate that configures the message builder. It must set both the message
    /// type (<see cref="IMessageBuilder.SetMessageType"/>) and the payload
    /// (<see cref="IMessageBuilder.SetPayload"/>).
    /// </param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// A message with <paramref name="id"/> already exists, or <paramref name="builder"/>
    /// left the message type or payload unset.
    /// </exception>
    Task<IMessage> PutAsync<TInput>(string id, TInput input, Action<TInput, IMessageBuilder> builder, CancellationToken ct = default);

    /// <summary>
    /// Reads the message with the given <paramref name="id"/>, regardless of its
    /// visibility, or <see langword="null"/> when no such message exists.
    /// </summary>
    /// <param name="id">The identifier of the message to read.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<IMessage?> TryGetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Reads up to <paramref name="count"/> currently-visible messages, in the queue's
    /// processing order. This is a non-exclusive, non-destructive read: it does not
    /// claim the messages, so two callers may receive the same message. Call
    /// <see cref="LeaseAsync"/> to claim a message for exclusive processing.
    /// </summary>
    /// <param name="count">The maximum number of messages to return; must be greater than zero.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<IReadOnlyList<IMessage>> TakeAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Attempts to lease <paramref name="message"/> for exclusive processing,
    /// located by its <see cref="IMessage.Id"/>. On success the message is made
    /// invisible until the lease expires and its <see cref="IMessage.Version"/> changes.
    /// </summary>
    /// <param name="message">The message to claim, typically obtained from <see cref="TakeAsync"/>.</param>
    /// <param name="leaseDuration">How long the lease is held before it expires. Must be positive.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is not positive.</exception>
    /// <returns>
    /// A lease, or <see langword="null"/> when the message could not be claimed —
    /// unknown or foreign id, a stale snapshot (its <see cref="IMessage.Version"/>
    /// no longer matches the queue's current value), already leased by another worker,
    /// currently invisible, or already removed.
    /// </returns>
    Task<IMessageLease?> LeaseAsync(IMessage message, TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>
    /// Estimates the number of messages in the queue. The result is approximate and
    /// best-effort; it may be stale by the time it is observed.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    Task<long> EstimateCountAsync(CancellationToken ct = default);
}
