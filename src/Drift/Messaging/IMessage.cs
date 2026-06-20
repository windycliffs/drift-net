namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// A message flowing through the processing pipeline. Its properties are the
/// metadata consumed by the generic processing layer; <see cref="GetPayload{TPayload}"/>
/// exposes the payload consumed by specific message-processor implementations.
/// </summary>
public interface IMessage
{
    /// <summary>
    /// The unique identifier of the message, supplied by the caller when the message
    /// is put into the queue. Opaque; non-empty.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// A discriminator identifying the kind of message, used by the generic
    /// processing layer to route the message to the right processor. The format
    /// is implementation-defined; non-empty.
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// An opaque optimistic-concurrency token (for example a storage ETag), managed
    /// by the queue and changed on every mutation (lease, update, renew, release).
    /// Treat it as an opaque value — compare for equality, do not parse or order it.
    /// </summary>
    string Version { get; }

    /// <summary>The instant at which the message was created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The instant at which the message was last modified. A message is modified when
    /// it is leased, updated, renewed, or released; this value equals
    /// <see cref="CreatedAt"/> only until the first such operation.
    /// </summary>
    DateTimeOffset LastModifiedAt { get; }

    /// <summary>
    /// The instant after which the message should be considered expired, or
    /// <see langword="null"/> when the message does not expire.
    /// </summary>
    DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// The instant before which the message is invisible to consumers — used for
    /// scheduled or deferred delivery — or <see langword="null"/> when the message is
    /// visible immediately. A message is invisible while the current time is earlier
    /// than this value. (An active lease independently hides a message regardless of
    /// this value.)
    /// </summary>
    DateTimeOffset? InvisibleBefore { get; }

    /// <summary>
    /// Free-form, non-null labels attached to the message. Assigned when the message
    /// is created and replaceable when it is updated. Empty when the message has no
    /// tags; never <see langword="null"/>.
    /// </summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>Returns the payload typed as <typeparamref name="TPayload"/>.</summary>
    /// <typeparam name="TPayload">The expected payload type.</typeparam>
    /// <remarks>
    /// The exception thrown when the stored payload cannot be provided as
    /// <typeparamref name="TPayload"/> is defined by the queue implementation — for a
    /// queue that stores payloads in serialized form, by its payload serializer.
    /// </remarks>
    TPayload GetPayload<TPayload>();
}
