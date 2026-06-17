namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Options supplied when putting a payload into an <see cref="IMessageQueue{TPayload}"/>.
/// The queue assigns the message identity (<see cref="IMessageMetadata.Id"/>,
/// <see cref="IMessageMetadata.Version"/>, <see cref="IMessageMetadata.CreatedAt"/>).
/// </summary>
/// <param name="MessageType">
/// The message-type discriminator stored on <see cref="IMessageMetadata.MessageType"/>.
/// Must be non-empty.
/// </param>
/// <exception cref="ArgumentException"><paramref name="MessageType"/> is null or empty.</exception>
public sealed record MessagePutOptions(string MessageType)
{
    /// <summary>The message-type discriminator. Must be non-empty.</summary>
    public string MessageType { get; init; } = NonEmpty(MessageType);

    /// <summary>
    /// The instant after which the message expires, or <see langword="null"/> when
    /// the message does not expire. Stored on <see cref="IMessageMetadata.ExpiresAt"/>.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The instant before which the message is invisible to consumers (an initial
    /// delay or scheduled-delivery time), or <see langword="null"/> to make the
    /// message visible immediately. Stored on <see cref="IMessageMetadata.InvisibleBefore"/>.
    /// </summary>
    public DateTimeOffset? InvisibleBefore { get; init; }

    private static string NonEmpty(string messageType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        return messageType;
    }
}
