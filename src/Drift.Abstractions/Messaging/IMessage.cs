namespace WindyCliffs.Drift.Messaging;

/// <summary>
/// A message flowing through the processing pipeline. The non-generic surface
/// exposes only the <see cref="Metadata"/> consumed by the generic processing
/// layer, independent of the payload type.
/// </summary>
public interface IMessage
{
    /// <summary>Metadata consumed by the generic processing layer.</summary>
    IMessageMetadata Metadata { get; }
}

/// <summary>
/// A message carrying a strongly-typed <typeparamref name="TPayload"/> consumed
/// by specific message-processor implementations.
/// </summary>
/// <typeparam name="TPayload">
/// The payload type carried by the message. Constrained to non-null: a message
/// always carries a payload.
/// </typeparam>
public interface IMessage<out TPayload> : IMessage
    where TPayload : notnull
{
    /// <summary>The payload consumed by specific processor implementations.</summary>
    TPayload Payload { get; }
}
