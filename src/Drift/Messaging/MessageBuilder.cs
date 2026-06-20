namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// The default <see cref="IMessageBuilder"/>: an in-memory accumulator that records
/// which properties a caller's configure delegate sets. A queue reads the
/// <c>*Set</c> flags to tell a deliberate change from a property left untouched.
/// </summary>
internal sealed class MessageBuilder(string id, string? version) : IMessageBuilder
{
    /// <inheritdoc />
    public string Id { get; } = id;

    /// <inheritdoc />
    public string? Version { get; } = version;

    public string? MessageType { get; private set; }

    public bool MessageTypeSet { get; private set; }

    public object? Payload { get; private set; }

    public bool PayloadSet { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool ExpiresAtSet { get; private set; }

    public DateTimeOffset? InvisibleBefore { get; private set; }

    public bool InvisibleBeforeSet { get; private set; }

    public IReadOnlyList<string>? Tags { get; private set; }

    public bool TagsSet { get; private set; }

    /// <inheritdoc />
    public IMessageBuilder SetMessageType(string messageType)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        this.MessageType = messageType;
        this.MessageTypeSet = true;
        return this;
    }

    /// <inheritdoc />
    public IMessageBuilder SetPayload<TPayload>(TPayload payload)
    {
        this.Payload = payload;
        this.PayloadSet = true;
        return this;
    }

    /// <inheritdoc />
    public IMessageBuilder SetExpiresAt(DateTimeOffset? value)
    {
        this.ExpiresAt = value;
        this.ExpiresAtSet = true;
        return this;
    }

    /// <inheritdoc />
    public IMessageBuilder SetInvisibleBefore(DateTimeOffset? value)
    {
        this.InvisibleBefore = value;
        this.InvisibleBeforeSet = true;
        return this;
    }

    /// <inheritdoc />
    public IMessageBuilder SetTags(IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        this.Tags = TagValidation.EnsureNoNullElements(tags, nameof(tags))!;
        this.TagsSet = true;
        return this;
    }
}
