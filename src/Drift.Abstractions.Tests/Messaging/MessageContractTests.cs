namespace WindyCliffs.Drift.Tests.Messaging;

using System;
using WindyCliffs.Drift.Messaging;
using Xunit;

public class MessageContractTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed record Metadata(string MessageType, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt = null)
        : IMessageMetadata;

    private sealed record Message<TPayload>(IMessageMetadata Metadata, TPayload Payload)
        : IMessage<TPayload>
        where TPayload : notnull;

    [Fact]
    public void MessageExposesMetadataAndPayload()
    {
        var message = new Message<string>(new Metadata("order.placed", Timestamp), "order-42");

        Assert.Equal("order.placed", message.Metadata.MessageType);
        Assert.Equal(Timestamp, message.Metadata.CreatedAt);
        Assert.Equal("order-42", message.Payload);
    }

    [Fact]
    public void GenericMessageIsUsableThroughNonGenericMetadataSurface()
    {
        IMessage message = new Message<int>(new Metadata("counter.incremented", Timestamp), 7);

        Assert.Equal("counter.incremented", message.Metadata.MessageType);
    }

    [Fact]
    public void ExpiresAtDefaultsToNullAndCanBeSet()
    {
        var expiresAt = Timestamp.AddMinutes(5);

        IMessageMetadata nonExpiring = new Metadata("heartbeat", Timestamp);
        IMessageMetadata expiring = new Metadata("session.token", Timestamp, expiresAt);

        Assert.Null(nonExpiring.ExpiresAt);
        Assert.Equal(expiresAt, expiring.ExpiresAt);
    }

    [Fact]
    public void PayloadIsCovariant()
    {
        IMessage<string> derived = new Message<string>(new Metadata("note.created", Timestamp), "hello");

        IMessage<object> covariant = derived;

        Assert.Equal("hello", covariant.Payload);
    }
}
