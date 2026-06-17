namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Information about a message consumed by the generic processing layer,
/// independent of the message payload.
/// </summary>
public interface IMessageMetadata
{
    /// <summary>
    /// A discriminator identifying the kind of message, used by the generic
    /// processing layer to route the message to the right processor. The format
    /// is implementation-defined; implementations must return a non-empty value.
    /// </summary>
    string MessageType { get; }

    /// <summary>The instant at which the message was created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The instant after which the message should be considered expired, or
    /// <see langword="null"/> when the message does not expire.
    /// </summary>
    DateTimeOffset? ExpiresAt { get; }
}
