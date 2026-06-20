namespace WindyCliffs.Drift.Tests.Messaging;

using System.IO;
using MessagePack;
using MessagePack.Resolvers;
using WindyCliffs.Drift.Messaging;

/// <summary>
/// An <see cref="IMessagePayloadSerializer"/> backed by MessagePack, used to exercise
/// the queue's serialized-payload storage. The contractless resolver lets arbitrary
/// payload types serialize without MessagePack attributes.
/// </summary>
internal sealed class MessagePackPayloadSerializer : IMessagePayloadSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    public void Serialize<TPayload>(Stream stream, TPayload payload) =>
        MessagePackSerializer.Serialize(stream, payload, Options);

    public TPayload Deserialize<TPayload>(Stream stream) =>
        MessagePackSerializer.Deserialize<TPayload>(stream, Options);
}
