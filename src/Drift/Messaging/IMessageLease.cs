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
/// A lease represents a single worker's hold and is not thread-safe; do not share
/// one instance across concurrent callers.
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
    /// Applies a partial update to the leased message — payload, expiry, visibility
    /// time, and tags — leaving any field left unset in <paramref name="update"/>
    /// unchanged, and changes <see cref="IMessageMetadata.Version"/>. Clearing an
    /// existing expiry or visibility time is not supported in this version.
    /// </summary>
    /// <param name="update">The change to apply.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task UpdateAsync(MessageUpdate<TPayload> update, CancellationToken ct = default);

    /// <summary>
    /// Renews the lease, extending the hold by <paramref name="leaseDuration"/> from
    /// now and changing <see cref="IMessageMetadata.Version"/>. Updates
    /// <see cref="LeasedUntil"/> to the new expiry.
    /// </summary>
    /// <param name="leaseDuration">How long the lease is held from now before it expires. Must be positive.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task RenewAsync(TimeSpan leaseDuration, CancellationToken ct = default);

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
