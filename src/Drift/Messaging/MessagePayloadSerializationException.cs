namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Thrown when a queue cannot serialize a payload (when a message is put or its payload
/// updated) or deserialize one (through <see cref="IMessage.GetPayload{TPayload}"/>).
/// Wraps the failure reported by the queue's <see cref="IMessagePayloadSerializer"/> so
/// callers do not depend on a specific serializer's exception types.
/// </summary>
public sealed class MessagePayloadSerializationException : MessageQueueException
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="messageId">The id of the message whose payload could not be (de)serialized.</param>
    /// <param name="payloadType">The payload type being serialized or deserialized.</param>
    /// <param name="innerException">The exception raised by the underlying serializer.</param>
    public MessagePayloadSerializationException(string message, string? messageId, Type payloadType, Exception innerException)
        : base(message, messageId, innerException) => this.PayloadType = payloadType;

    /// <summary>The payload type that was being serialized or deserialized.</summary>
    public Type PayloadType { get; }
}
