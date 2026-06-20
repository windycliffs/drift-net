namespace WindyCliffs.Drift.Messaging;

using System;
using System.Collections.Generic;

/// <summary>
/// Configures the properties of a message when it is put into an
/// <see cref="IMessageQueue"/> or updated through an <see cref="IMessageLease"/>.
/// Only the properties whose setters are called take effect; on an update, any
/// property left unset keeps its current value. The setters return the same builder
/// so calls can be chained.
/// </summary>
public interface IMessageBuilder
{
    /// <summary>
    /// The identifier of the message being built. Supplied by the caller on
    /// <see cref="IMessageQueue.PutAsync"/> and carried through on an update.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The current <see cref="IMessage.Version"/> of the message being updated, or
    /// <see langword="null"/> while a message is being put into the queue for the
    /// first time (it has no version yet).
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Sets the instant before which the message is invisible to consumers, or
    /// <see langword="null"/> to make it visible immediately. See
    /// <see cref="IMessage.InvisibleBefore"/>.
    /// </summary>
    /// <param name="value">The visibility time, or <see langword="null"/> for immediate visibility.</param>
    IMessageBuilder SetInvisibleBefore(DateTimeOffset? value);

    /// <summary>
    /// Sets the instant after which the message expires, or <see langword="null"/>
    /// when the message does not expire. See <see cref="IMessage.ExpiresAt"/>.
    /// </summary>
    /// <param name="value">The expiry instant, or <see langword="null"/> for no expiry.</param>
    IMessageBuilder SetExpiresAt(DateTimeOffset? value);

    /// <summary>
    /// Sets the message's tags, replacing any existing tags. See
    /// <see cref="IMessage.Tags"/>.
    /// </summary>
    /// <param name="tags">The non-null labels to attach. Must not contain null elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains a null element.</exception>
    IMessageBuilder SetTags(IReadOnlyList<string> tags);

    /// <summary>
    /// Sets the message-type discriminator. See <see cref="IMessage.MessageType"/>.
    /// This is set when a message is put into the queue; it is immutable thereafter.
    /// </summary>
    /// <param name="messageType">The message-type discriminator. Must be non-empty.</param>
    /// <exception cref="ArgumentException"><paramref name="messageType"/> is null or empty.</exception>
    IMessageBuilder SetMessageType(string messageType);

    /// <summary>
    /// Sets the message's payload. Read back with <see cref="IMessage.GetPayload{TPayload}"/>.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The payload to store.</param>
    IMessageBuilder SetPayload<TPayload>(TPayload payload);
}
