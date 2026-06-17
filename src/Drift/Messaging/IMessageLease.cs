namespace WindyCliffs.Drift.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An exclusive lease on a message held by a single worker. The mutating
/// operations (update, release, remove) exist only on the lease, so they can be
/// performed only while the message is leased.
/// </summary>
/// <remarks>
/// Disposing the lease is an idempotent safety net: if the lease is still held it
/// is released; otherwise disposal is a no-op. Consumers typically write
/// <c>await using var lease = await queue.LeaseAsync(...);</c>.
/// </remarks>
/// <typeparam name="TPayload">The payload type.</typeparam>
public interface IMessageLease<TPayload> : IAsyncDisposable
    where TPayload : notnull
{
    /// <summary>
    /// The current snapshot of the leased message. After a successful
    /// <see cref="UpdateAsync"/> this reflects the update, with a changed
    /// <see cref="IMessageMetadata.Version"/>.
    /// </summary>
    IMessage<TPayload> Message { get; }

    /// <summary>The instant at which the lease expires if not released earlier.</summary>
    DateTimeOffset LeasedUntil { get; }

    /// <summary>
    /// Updates the leased message's payload and, optionally, its expiry, and
    /// changes <see cref="IMessageMetadata.Version"/>.
    /// </summary>
    /// <param name="update">The change to apply.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task UpdateAsync(MessageUpdate<TPayload> update, CancellationToken ct = default);

    /// <summary>
    /// Releases the lease, making the message visible to consumers again.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task ReleaseAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes the message from the queue.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task RemoveAsync(CancellationToken ct = default);
}
