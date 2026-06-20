namespace WindyCliffs.Drift.Messaging;

using System.IO;

/// <summary>
/// Serializes message payloads to and from the byte form in which a queue stores them.
/// A queue that keeps payloads serialized uses this to write a payload when a message
/// is put or updated and to read it back through <see cref="IMessage.GetPayload{TPayload}"/>.
/// Implementations must be thread-safe, must not block on asynchronous work, and must
/// treat the stream passed to <see cref="Deserialize{TPayload}"/> as read-only.
/// </summary>
public interface IMessagePayloadSerializer
{
    /// <summary>Writes <paramref name="payload"/> to <paramref name="stream"/>.</summary>
    /// <typeparam name="TPayload">The payload type, as known at the call site.</typeparam>
    /// <param name="stream">The stream to write the serialized payload to.</param>
    /// <param name="payload">The payload to serialize.</param>
    void Serialize<TPayload>(Stream stream, TPayload payload);

    /// <summary>Reads a <typeparamref name="TPayload"/> from <paramref name="stream"/>.</summary>
    /// <typeparam name="TPayload">The payload type to deserialize.</typeparam>
    /// <param name="stream">The stream to read the serialized payload from.</param>
    /// <returns>The deserialized payload.</returns>
    TPayload Deserialize<TPayload>(Stream stream);
}
