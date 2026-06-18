namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Options supplied when putting a payload into an <see cref="IMessageQueue"/>.
/// The caller supplies the <see cref="IMessage.Id"/>; the queue assigns the
/// <see cref="IMessage.Version"/> and <see cref="IMessage.CreatedAt"/>.
/// </summary>
/// <param name="MessageType">
/// The message-type discriminator stored on <see cref="IMessage.MessageType"/>.
/// Must be non-empty.
/// </param>
/// <exception cref="ArgumentException"><paramref name="MessageType"/> is null or empty.</exception>
public sealed record MessagePutOptions(string MessageType)
{
    private readonly IReadOnlyList<string> tags = [];

    /// <summary>The message-type discriminator. Must be non-empty.</summary>
    public string MessageType { get; init; } = NonEmpty(MessageType);

    /// <summary>
    /// The instant after which the message expires, or <see langword="null"/> when
    /// the message does not expire. Stored on <see cref="IMessage.ExpiresAt"/>.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The instant before which the message is invisible to consumers (an initial
    /// delay or scheduled-delivery time), or <see langword="null"/> to make the
    /// message visible immediately. Stored on <see cref="IMessage.InvisibleBefore"/>.
    /// </summary>
    public DateTimeOffset? InvisibleBefore { get; init; }

    /// <summary>
    /// Non-null labels attached to the message, stored on <see cref="IMessage.Tags"/>.
    /// Defaults to an empty list. Must not contain null elements.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => this.tags;
        init => this.tags = NonNullTags(value);
    }

    private static string NonEmpty(string messageType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        return messageType;
    }

    private static IReadOnlyList<string> NonNullTags(IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return TagValidation.EnsureNoNullElements(tags, nameof(tags))!;
    }
}
