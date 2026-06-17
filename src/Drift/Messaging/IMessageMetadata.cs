namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Information about a message consumed by the generic processing layer,
/// independent of the message payload.
/// </summary>
public interface IMessageMetadata
{
    /// <summary>
    /// The queue-assigned unique identifier of the message. Opaque and
    /// implementation-defined; implementations must return a non-empty value.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// A discriminator identifying the kind of message, used by the generic
    /// processing layer to route the message to the right processor. The format
    /// is implementation-defined; implementations must return a non-empty value.
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// An opaque optimistic-concurrency token (for example a storage ETag), managed
    /// by the queue and changed on every mutation (lease, update, release). Treat it
    /// as an opaque value — compare for equality, do not parse or order it. Used to
    /// detect concurrent modification and lost-lease races.
    /// </summary>
    string Version { get; }

    /// <summary>The instant at which the message was created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The instant after which the message should be considered expired, or
    /// <see langword="null"/> when the message does not expire.
    /// </summary>
    DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// The instant before which the message is invisible to consumers (used for
    /// scheduled delivery and lease visibility timeouts), or <see langword="null"/>
    /// when the message is visible immediately. A message is invisible while the
    /// current time is earlier than this value.
    /// </summary>
    DateTimeOffset? InvisibleBefore { get; }
}
