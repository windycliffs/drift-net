namespace WindyCliffs.Drift.Messaging;

/// <summary>
/// Thrown by a lease operation (update, renew, release, remove) when the lease is no
/// longer held — because it expired, was already released or removed, or the message
/// was claimed by another worker. The exclusive hold the operation requires is gone.
/// </summary>
public sealed class MessageLeaseLostException : MessageQueueException
{
    /// <summary>Initializes a new instance for the message whose lease was lost.</summary>
    /// <param name="messageId">The id of the message whose lease is no longer held.</param>
    public MessageLeaseLostException(string messageId)
        : base($"The lease on message '{messageId}' is no longer held.", messageId)
    {
    }
}
