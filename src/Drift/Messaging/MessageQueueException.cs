namespace WindyCliffs.Drift.Messaging;

using System;

/// <summary>
/// The base type for errors raised by an <see cref="IMessageQueue"/>. Catch this to
/// handle any queue-specific failure, or catch a derived type for a specific category.
/// </summary>
/// <remarks>
/// Caller and argument-validation mistakes (for example a null id, a null builder, or a
/// non-positive lease duration) are reported with the standard <see cref="ArgumentException"/>
/// family rather than this hierarchy, so they are not hidden behind a queue exception.
/// </remarks>
public abstract class MessageQueueException : Exception
{
    /// <summary>Initializes a new instance with a message and optional context.</summary>
    /// <param name="message">A description of the failure.</param>
    /// <param name="messageId">The id of the message the failure relates to, when known.</param>
    /// <param name="innerException">The underlying failure, when this wraps a lower-level exception.</param>
    protected MessageQueueException(string message, string? messageId = null, Exception? innerException = null)
        : base(message, innerException) => this.MessageId = messageId;

    /// <summary>
    /// The id of the message this failure relates to, or <see langword="null"/> when the
    /// failure is not tied to a single message.
    /// </summary>
    public string? MessageId { get; }
}
