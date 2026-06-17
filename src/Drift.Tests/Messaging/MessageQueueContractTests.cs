namespace WindyCliffs.Drift.Tests.Messaging;

using System;
using System.Threading;
using System.Threading.Tasks;
using WindyCliffs.Drift.Messaging;
using Xunit;

public class MessageQueueContractTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (InMemoryMessageQueue<string> Queue, TestClock Clock) CreateQueue()
    {
        var clock = new TestClock(Start);
        return (new InMemoryMessageQueue<string>(() => clock.Now), clock);
    }

    [Fact]
    public async Task PutAssignsIdentityAndReturnsStoredMessage()
    {
        var (queue, _) = CreateQueue();

        var message = await queue.PutAsync("order-42", new MessagePutOptions("order.placed"), Ct);

        Assert.False(string.IsNullOrEmpty(message.Metadata.Id));
        Assert.False(string.IsNullOrEmpty(message.Metadata.Version));
        Assert.Equal("order.placed", message.Metadata.MessageType);
        Assert.Equal(Start, message.Metadata.CreatedAt);
        Assert.Equal("order-42", message.Payload);
    }

    [Fact]
    public async Task TakeReturnsUpToCountAndIsNonExclusive()
    {
        var (queue, _) = CreateQueue();
        await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("b", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("c", new MessagePutOptions("t"), Ct);

        Assert.Equal(2, (await queue.TakeAsync(2, Ct)).Count);

        // Non-destructive read: a second Take still sees the same messages.
        Assert.Equal(3, (await queue.TakeAsync(10, Ct)).Count);
        Assert.Equal(3, (await queue.TakeAsync(10, Ct)).Count);
    }

    [Fact]
    public async Task TakeWithNonPositiveCountThrows()
    {
        var (queue, _) = CreateQueue();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.TakeAsync(0, Ct));
    }

    [Fact]
    public async Task LeaseHidesMessageAndSecondLeaseReturnsNull()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);

        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);

        Assert.NotNull(lease);
        Assert.Empty(await queue.TakeAsync(10, Ct));                       // hidden while leased
        Assert.Null(await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct)); // exclusivity
    }

    [Fact]
    public async Task LeaseOfUnknownMessageReturnsNull()
    {
        var (queue, _) = CreateQueue();
        var foreign = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var (otherQueue, _) = CreateQueue();

        Assert.Null(await otherQueue.LeaseAsync(foreign, TimeSpan.FromMinutes(5), Ct));
    }

    [Fact]
    public async Task UpdateChangesPayloadAndVersionThenSurvivesRelease()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var versionBeforeUpdate = lease.Message.Metadata.Version;

        await lease.UpdateAsync(new MessageUpdate<string>("b"), Ct);

        Assert.Equal("b", lease.Message.Payload);
        Assert.NotEqual(versionBeforeUpdate, lease.Message.Metadata.Version);

        await lease.ReleaseAsync(Ct);
        var visible = await queue.TakeAsync(10, Ct);
        Assert.Equal("b", Assert.Single(visible).Payload);
    }

    [Fact]
    public async Task ReleaseMakesMessageVisibleAgain()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.ReleaseAsync(Ct);

        Assert.Single(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task RemoveDeletesMessage()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.RemoveAsync(Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct));
        Assert.Equal(0L, await queue.EstimateCountAsync(Ct));
    }

    [Fact]
    public async Task MutatingAfterReleaseThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        await lease.ReleaseAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync(new MessageUpdate<string>("b"), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ReleaseAsync(Ct));
    }

    [Fact]
    public async Task DisposeReleasesHeldLease()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);

        await using (var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct))
        {
            Assert.NotNull(lease);
            Assert.Empty(await queue.TakeAsync(10, Ct)); // hidden while leased
        }

        Assert.Single(await queue.TakeAsync(10, Ct)); // released on dispose
    }

    [Fact]
    public async Task ScheduledMessageIsInvisibleUntilClockAdvances()
    {
        var (queue, clock) = CreateQueue();
        await queue.PutAsync("a", new MessagePutOptions("t") { InvisibleBefore = Start.AddMinutes(1) }, Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct));

        clock.Now = Start.AddMinutes(2);

        Assert.Single(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task LeaseExpiryRestoresVisibilityAndInvalidatesLease()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(lease);
        Assert.Empty(await queue.TakeAsync(10, Ct)); // hidden while leased

        clock.Now = Start.AddMinutes(2); // past lease expiry

        Assert.Single(await queue.TakeAsync(10, Ct)); // visible again
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync(new MessageUpdate<string>("b"), Ct));
    }

    [Fact]
    public async Task EstimateCountReflectsQueuedMessages()
    {
        var (queue, _) = CreateQueue();
        await queue.PutAsync("a", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("b", new MessagePutOptions("t"), Ct);

        Assert.Equal(2L, await queue.EstimateCountAsync(Ct));
    }

    [Fact]
    public async Task LeaseWithStaleSnapshotReturnsNull()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("a", new MessagePutOptions("t"), Ct); // snapshot at original version
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        await lease.ReleaseAsync(Ct); // visible again, but the version has moved on

        // Leasing with the now-stale original snapshot must fail (optimistic concurrency).
        Assert.Null(await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct));
    }

    [Fact]
    public void PutOptionsRejectEmptyMessageType()
    {
        Assert.Throws<ArgumentException>(() => new MessagePutOptions(string.Empty));
    }
}
