namespace WindyCliffs.Drift.Tests.Messaging;

using System;
using System.Collections.Generic;
using WindyCliffs.Drift.Messaging;
using Xunit;

public class MessageContractTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed record Metadata(
        string MessageType,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt = null,
        string Id = "test-id",
        string Version = "v1",
        DateTimeOffset? InvisibleBefore = null,
        DateTimeOffset LastModifiedAt = default) : IMessageMetadata
    {
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

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

    [Fact]
    public void MetadataExposesIdVersionAndInvisibleBefore()
    {
        var invisibleBefore = Timestamp.AddSeconds(30);
        IMessageMetadata metadata = new Metadata(
            "order.placed",
            Timestamp,
            Id: "msg-1",
            Version: "etag-4",
            InvisibleBefore: invisibleBefore);

        Assert.Equal("msg-1", metadata.Id);
        Assert.Equal("etag-4", metadata.Version);
        Assert.Equal(invisibleBefore, metadata.InvisibleBefore);
    }

    [Fact]
    public void InvisibleBeforeDefaultsToNull()
    {
        IMessageMetadata metadata = new Metadata("heartbeat", Timestamp);

        Assert.Null(metadata.InvisibleBefore);
    }
}
