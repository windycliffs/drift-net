namespace WindyCliffs.Drift.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An exclusive lease on a message held by a single worker. The mutating
/// operations (update, renew, release, remove) exist only on the lease, so they can
/// be performed only while the message is leased.
/// </summary>
/// <remarks>
/// Disposing the lease is an idempotent safety net: if the lease is still held it
/// is released; otherwise disposal is a no-op. Consumers typically write
/// <c>await using var lease = await queue.LeaseAsync(...);</c>.
/// A lease represents a single worker's hold and is not thread-safe; do not share
/// one instance across concurrent callers.
/// </remarks>
public interface IMessageLease : IAsyncDisposable
{
    /// <summary>
    /// The current snapshot of the leased message. After a successful update this
    /// reflects the change, with a changed <see cref="IMessage.Version"/>.
    /// </summary>
    IMessage Message { get; }

    /// <summary>The instant at which the lease expires if not released earlier.</summary>
    DateTimeOffset LeasedUntil { get; }

    /// <summary>
    /// Applies a partial update to the leased message's properties — expiry, visibility
    /// time, and tags — leaving any field left unset in <paramref name="update"/>
    /// unchanged, without changing the payload. A new visibility time
    /// (<see cref="MessageUpdate.InvisibleBefore"/>) takes effect once the lease is
    /// released; it does not surface the message while it is still leased. Clearing an
    /// existing expiry or visibility time is not supported in this version.
    /// </summary>
    /// <param name="update">The property change to apply.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task UpdateAsync(MessageUpdate update, CancellationToken ct = default);

    /// <summary>
    /// Replaces the leased message's payload and applies the property change in
    /// <paramref name="update"/> (see <see cref="UpdateAsync(MessageUpdate, CancellationToken)"/>).
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The new payload.</param>
    /// <param name="update">The property change to apply.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task UpdateAsync<TPayload>(TPayload payload, MessageUpdate update, CancellationToken ct = default)
        where TPayload : notnull;

    /// <summary>
    /// Renews the lease, extending the hold by <paramref name="leaseDuration"/> from
    /// now and changing <see cref="IMessage.Version"/>. Updates <see cref="LeasedUntil"/>
    /// to the new expiry.
    /// </summary>
    /// <param name="leaseDuration">How long the lease is held from now before it expires. Must be positive.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="leaseDuration"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task RenewAsync(TimeSpan leaseDuration, CancellationToken ct = default);

    /// <summary>Releases the lease, making the message visible to consumers again.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task ReleaseAsync(CancellationToken ct = default);

    /// <summary>Removes the message from the queue.</summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The lease is no longer held.</exception>
    Task RemoveAsync(CancellationToken ct = default);
}
