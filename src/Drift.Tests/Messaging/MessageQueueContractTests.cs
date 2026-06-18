namespace WindyCliffs.Drift.Tests.Messaging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindyCliffs.Clock;
using WindyCliffs.Drift.Messaging;
using Xunit;

public class MessageQueueContractTests
{
    private static readonly string[] RedUrgent = ["red", "urgent"];
    private static readonly string[] Ids12 = ["1", "2"];
    private static readonly string[] Archived = ["archived"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (InMemoryMessageQueue<string> Queue, MockClock Clock) CreateQueue()
    {
        var clock = new MockClock();
        return (new InMemoryMessageQueue<string>(clock), clock);
    }

    // Advance in a single atomic step (step == interval) so no intermediate ticks fire.
    private static void Advance(MockClock clock, TimeSpan by) => clock.AdvanceBy(by, by);

    [Fact]
    public async Task PutAssignsVersionAndTimestampsAndKeepsCallerId()
    {
        var (queue, clock) = CreateQueue();

        var message = await queue.PutAsync("order-1", "order-42", new MessagePutOptions("order.placed"), Ct);

        Assert.Equal("order-1", message.Metadata.Id);
        Assert.False(string.IsNullOrEmpty(message.Metadata.Version));
        Assert.Equal("order.placed", message.Metadata.MessageType);
        Assert.Equal(clock.UtcNow, message.Metadata.CreatedAt);
        Assert.Equal(message.Metadata.CreatedAt, message.Metadata.LastModifiedAt);
        Assert.Empty(message.Metadata.Tags);
        Assert.Equal("order-42", message.Payload);
    }

    [Fact]
    public async Task PutStoresTags()
    {
        var (queue, _) = CreateQueue();

        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t") { Tags = ["red", "urgent"] }, Ct);

        Assert.Equal(RedUrgent, message.Metadata.Tags);
    }

    [Fact]
    public async Task PutWithDuplicateIdThrows()
    {
        var (queue, _) = CreateQueue();
        await queue.PutAsync("dup", "a", new MessagePutOptions("t"), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.PutAsync("dup", "b", new MessagePutOptions("t"), Ct));
    }

    [Fact]
    public async Task PutWithEmptyIdThrows()
    {
        var (queue, _) = CreateQueue();

        await Assert.ThrowsAsync<ArgumentException>(
            () => queue.PutAsync(string.Empty, "a", new MessagePutOptions("t"), Ct));
    }

    [Fact]
    public async Task TryGetReturnsMessageRegardlessOfVisibility()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);

        Assert.Null(await queue.TryGetAsync("missing", Ct));
        Assert.Equal("a", (await queue.TryGetAsync("m", Ct))?.Payload);

        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        Assert.NotNull(await queue.TryGetAsync("m", Ct)); // still gettable while invisibly leased
    }

    [Fact]
    public async Task TakeReturnsUpToCountInInsertionOrderAndIsNonExclusive()
    {
        var (queue, _) = CreateQueue();
        await queue.PutAsync("1", "a", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("2", "b", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("3", "c", new MessagePutOptions("t"), Ct);

        var first = await queue.TakeAsync(2, Ct);
        Assert.Equal(Ids12, first.Select(m => m.Metadata.Id));

        // Non-destructive read: a second Take still sees the same messages.
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
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);

        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);

        Assert.NotNull(lease);
        Assert.Empty(await queue.TakeAsync(10, Ct));
        Assert.Null(await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct));
    }

    [Fact]
    public async Task LeaseOfUnknownMessageReturnsNull()
    {
        var (queue, _) = CreateQueue();
        var foreign = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var (otherQueue, _) = CreateQueue();

        Assert.Null(await otherQueue.LeaseAsync(foreign, TimeSpan.FromMinutes(5), Ct));
    }

    [Fact]
    public async Task LeaseWithStaleSnapshotReturnsNull()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        await lease.ReleaseAsync(Ct); // visible again, but the version has moved on

        Assert.Null(await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct));
    }

    [Fact]
    public async Task UpdateWithPayloadChangesPayloadAndVersion()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var versionBefore = lease.Message.Metadata.Version;

        await lease.UpdateAsync(new MessageUpdate<string> { Payload = "b" }, Ct);

        Assert.Equal("b", lease.Message.Payload);
        Assert.NotEqual(versionBefore, lease.Message.Metadata.Version);
    }

    [Fact]
    public async Task UpdateWithoutPayloadLeavesPayloadButChangesProperties()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var newExpiry = clock.UtcNow.AddHours(1);

        await lease.UpdateAsync(new MessageUpdate<string> { ExpiresAt = newExpiry, Tags = ["archived"] }, Ct);

        Assert.Equal("a", lease.Message.Payload); // payload untouched
        Assert.Equal(newExpiry, lease.Message.Metadata.ExpiresAt);
        Assert.Equal(Archived, lease.Message.Metadata.Tags);
    }

    [Fact]
    public async Task UpdateRefreshesLastModifiedAt()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        Advance(clock, TimeSpan.FromMinutes(1));

        await lease.UpdateAsync(new MessageUpdate<string> { Payload = "b" }, Ct);

        Assert.Equal(message.Metadata.CreatedAt.AddMinutes(1), lease.Message.Metadata.LastModifiedAt);
    }

    [Fact]
    public async Task RenewExtendsTheLease()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var t0 = message.Metadata.CreatedAt;
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(lease);

        Advance(clock, TimeSpan.FromSeconds(30)); // still within the original 1-minute lease
        await lease.RenewAsync(TimeSpan.FromMinutes(5), Ct);
        Assert.Equal(t0.AddSeconds(30).AddMinutes(5), lease.LeasedUntil);

        Advance(clock, TimeSpan.FromMinutes(2)); // past the original expiry, within the renewed one
        Assert.Empty(await queue.TakeAsync(10, Ct)); // still leased
        await lease.UpdateAsync(new MessageUpdate<string> { Payload = "b" }, Ct); // still held
        Assert.Equal("b", lease.Message.Payload);
    }

    [Fact]
    public async Task ReleaseMakesMessageVisibleAgain()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.ReleaseAsync(Ct);

        Assert.Single(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task RemoveDeletesMessage()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.RemoveAsync(Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct));
        Assert.Null(await queue.TryGetAsync("m", Ct));
        Assert.Equal(0L, await queue.EstimateCountAsync(Ct));
    }

    [Fact]
    public async Task MutatingAfterReleaseThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        await lease.ReleaseAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync(new MessageUpdate<string> { Payload = "b" }, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.RenewAsync(TimeSpan.FromMinutes(1), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ReleaseAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.RemoveAsync(Ct));
    }

    [Fact]
    public async Task DisposeReleasesHeldLease()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);

        await using (var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct))
        {
            Assert.NotNull(lease);
            Assert.Empty(await queue.TakeAsync(10, Ct));
        }

        Assert.Single(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task ScheduledMessageIsInvisibleUntilClockAdvances()
    {
        var (queue, clock) = CreateQueue();
        await queue.PutAsync("m", "a", new MessagePutOptions("t") { InvisibleBefore = clock.UtcNow.AddMinutes(1) }, Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct));

        Advance(clock, TimeSpan.FromMinutes(2));

        Assert.Single(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task LeaseExpiryRestoresVisibilityAndInvalidatesLease()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(lease);
        Assert.Empty(await queue.TakeAsync(10, Ct));

        Advance(clock, TimeSpan.FromMinutes(2)); // past lease expiry

        Assert.Single(await queue.TakeAsync(10, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync(new MessageUpdate<string> { Payload = "b" }, Ct));
    }

    [Fact]
    public async Task EstimateCountReflectsQueuedMessages()
    {
        var (queue, _) = CreateQueue();
        await queue.PutAsync("1", "a", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("2", "b", new MessagePutOptions("t"), Ct);

        Assert.Equal(2L, await queue.EstimateCountAsync(Ct));
    }

    [Fact]
    public async Task UpdateWithoutPayloadAppliesDefaultForValueTypePayload()
    {
        // Documented limitation: a value-type payload has no null sentinel, so omitting
        // it in the update applies default(TPayload) rather than leaving it unchanged.
        var clock = new MockClock();
        var queue = new InMemoryMessageQueue<int>(clock);
        var message = await queue.PutAsync("m", 5, new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.UpdateAsync(new MessageUpdate<int> { ExpiresAt = clock.UtcNow.AddHours(1) }, Ct);

        Assert.Equal(0, lease.Message.Payload);
    }

    [Fact]
    public async Task LeaseWithNonPositiveDurationThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.LeaseAsync(message, TimeSpan.Zero, Ct));
    }

    [Fact]
    public async Task RenewWithNonPositiveDurationThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => lease.RenewAsync(TimeSpan.Zero, Ct));
    }

    [Fact]
    public void PutOptionsRejectEmptyMessageType() =>
        Assert.Throws<ArgumentException>(() => new MessagePutOptions(string.Empty));

    [Fact]
    public void PutOptionsRejectNullTagElement() =>
        Assert.Throws<ArgumentException>(() => new MessagePutOptions("t") { Tags = ["ok", null!] });

    [Fact]
    public void MessageUpdateRejectsNullTagElement() =>
        Assert.Throws<ArgumentException>(() => new MessageUpdate<string> { Tags = [null!] });
}
