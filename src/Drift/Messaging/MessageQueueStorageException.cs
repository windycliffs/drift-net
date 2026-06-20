namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// Thrown when the store backing a queue fails — for example a transport, I/O, or
/// service error from a durable backend. Wraps the underlying failure so callers do not
/// depend on a specific backend's exception types.
/// </summary>
/// <remarks>
/// The in-memory <see cref="InMemoryMessageQueue"/> holds its state in process and does
/// not raise this; it exists so durable implementations of <see cref="IMessageQueue"/>
/// can report storage failures through one common type.
/// </remarks>
public sealed class MessageQueueStorageException : MessageQueueException
{
    /// <summary>Initializes a new instance.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="messageId">The id of the message being operated on, when the failure relates to one.</param>
    /// <param name="innerException">The underlying store or transport failure, when available.</param>
    public MessageQueueStorageException(string message, string? messageId = null, Exception? innerException = null)
        : base(message, messageId, innerException)
    {
    }
}
