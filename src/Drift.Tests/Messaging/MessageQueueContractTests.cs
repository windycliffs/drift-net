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
    private static readonly string[] Ab = ["a", "b"];
    private static readonly string[] Ba = ["b", "a"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (InMemoryMessageQueue Queue, MockClock Clock) CreateQueue()
    {
        var clock = new MockClock();
        return (new InMemoryMessageQueue(clock), clock);
    }

    // Advance in a single atomic step (step == interval) so no intermediate ticks fire.
    private static void Advance(MockClock clock, TimeSpan by) => clock.AdvanceBy(by, by);

    [Fact]
    public async Task PutAssignsVersionAndTimestampsAndKeepsCallerId()
    {
        var (queue, clock) = CreateQueue();

        var message = await queue.PutAsync("order-1", "order-42", new MessagePutOptions("order.placed"), Ct);

        Assert.Equal("order-1", message.Id);
        Assert.False(string.IsNullOrEmpty(message.Version));
        Assert.Equal("order.placed", message.MessageType);
        Assert.Equal(clock.UtcNow, message.CreatedAt);
        Assert.Equal(message.CreatedAt, message.LastModifiedAt);
        Assert.Empty(message.Tags);
        Assert.Equal("order-42", message.GetPayload<string>());
    }

    [Fact]
    public async Task GetPayloadReturnsTypedPayload()
    {
        var (queue, _) = CreateQueue();

        var message = await queue.PutAsync("m", "hello", new MessagePutOptions("t"), Ct);

        Assert.Equal("hello", message.GetPayload<string>());
    }

    [Fact]
    public async Task GetPayloadWithWrongTypeThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "hello", new MessagePutOptions("t"), Ct);

        Assert.Throws<InvalidCastException>(() => message.GetPayload<int>());
    }

    [Fact]
    public async Task PutStoresTags()
    {
        var (queue, _) = CreateQueue();

        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t") { Tags = ["red", "urgent"] }, Ct);

        Assert.Equal(RedUrgent, message.Tags);
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
        Assert.Equal("a", (await queue.TryGetAsync("m", Ct))?.GetPayload<string>());

        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        Assert.NotNull(await queue.TryGetAsync("m", Ct)); // still gettable while invisibly leased
    }

    [Fact]
    public async Task TakeReturnsUpToCountInSortedOrderAndIsNonExclusive()
    {
        var (queue, _) = CreateQueue();
        // Put at the same instant, so they sort by the id tie-breaker: 1, 2, 3.
        await queue.PutAsync("1", "a", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("2", "b", new MessagePutOptions("t"), Ct);
        await queue.PutAsync("3", "c", new MessagePutOptions("t"), Ct);

        var first = await queue.TakeAsync(2, Ct);
        Assert.Equal(Ids12, first.Select(m => m.Id));

        // Non-destructive read: a second Take still sees the same messages.
        Assert.Equal(3, (await queue.TakeAsync(10, Ct)).Count);
    }

    [Fact]
    public async Task TakeOrdersByLastModifiedSoTouchedMessagesMoveToTheEnd()
    {
        var (queue, clock) = CreateQueue();
        await queue.PutAsync("a", "a", new MessagePutOptions("t"), Ct);
        Advance(clock, TimeSpan.FromMinutes(1));
        await queue.PutAsync("b", "b", new MessagePutOptions("t"), Ct);

        Assert.Equal(Ab, (await queue.TakeAsync(10, Ct)).Select(m => m.Id));

        // Touch "a" (lease + release) so its LastModifiedAt becomes the most recent.
        Advance(clock, TimeSpan.FromMinutes(1));
        var a = (await queue.TakeAsync(10, Ct)).First(m => m.Id == "a");
        var lease = await queue.LeaseAsync(a, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        await lease.ReleaseAsync(Ct);

        Assert.Equal(Ba, (await queue.TakeAsync(10, Ct)).Select(m => m.Id));
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
    public async Task LeaseWithNonPositiveDurationThrows()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => queue.LeaseAsync(message, TimeSpan.Zero, Ct));
    }

    [Fact]
    public async Task UpdateWithPayloadChangesPayloadAndVersion()
    {
        var (queue, _) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var versionBefore = lease.Message.Version;

        await lease.UpdateAsync("b", new MessageUpdate(), Ct);

        Assert.Equal("b", lease.Message.GetPayload<string>());
        Assert.NotEqual(versionBefore, lease.Message.Version);
    }

    [Fact]
    public async Task UpdatePropertiesOnlyLeavesPayloadButChangesProperties()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var newExpiry = clock.UtcNow.AddHours(1);

        await lease.UpdateAsync(new MessageUpdate { ExpiresAt = newExpiry, Tags = ["archived"] }, Ct);

        Assert.Equal("a", lease.Message.GetPayload<string>()); // payload untouched
        Assert.Equal(newExpiry, lease.Message.ExpiresAt);
        Assert.Equal(Archived, lease.Message.Tags);
    }

    [Fact]
    public async Task UpdatePropertiesOnlyLeavesValueTypePayloadUnchanged()
    {
        var clock = new MockClock();
        var queue = new InMemoryMessageQueue(clock);
        var message = await queue.PutAsync("m", 5, new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        await lease.UpdateAsync(new MessageUpdate { ExpiresAt = clock.UtcNow.AddHours(1) }, Ct);

        Assert.Equal(5, lease.Message.GetPayload<int>());
    }

    [Fact]
    public async Task UpdateWithPastInvisibleBeforeDoesNotSurfaceMessageWhileLeased()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);

        // Setting InvisibleBefore to the past while leased must NOT surface the message;
        // it is still exclusively held.
        await lease.UpdateAsync(new MessageUpdate { InvisibleBefore = clock.UtcNow.AddMinutes(-1) }, Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct));
    }

    [Fact]
    public async Task UpdatedInvisibleBeforeDefersVisibilityAfterRelease()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        var deferUntil = clock.UtcNow.AddMinutes(10);

        await lease.UpdateAsync(new MessageUpdate { InvisibleBefore = deferUntil }, Ct);
        await lease.ReleaseAsync(Ct);

        Assert.Empty(await queue.TakeAsync(10, Ct)); // still deferred after release
        Advance(clock, TimeSpan.FromMinutes(11));
        Assert.Single(await queue.TakeAsync(10, Ct)); // visible once the defer time passes
    }

    [Fact]
    public async Task UpdateRefreshesLastModifiedAt()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(5), Ct);
        Assert.NotNull(lease);
        Advance(clock, TimeSpan.FromMinutes(1));

        await lease.UpdateAsync("b", new MessageUpdate(), Ct);

        Assert.Equal(message.CreatedAt.AddMinutes(1), lease.Message.LastModifiedAt);
    }

    [Fact]
    public async Task RenewExtendsTheLease()
    {
        var (queue, clock) = CreateQueue();
        var message = await queue.PutAsync("m", "a", new MessagePutOptions("t"), Ct);
        var t0 = message.CreatedAt;
        var lease = await queue.LeaseAsync(message, TimeSpan.FromMinutes(1), Ct);
        Assert.NotNull(lease);

        Advance(clock, TimeSpan.FromSeconds(30)); // still within the original 1-minute lease
        await lease.RenewAsync(TimeSpan.FromMinutes(5), Ct);
        Assert.Equal(t0.AddSeconds(30).AddMinutes(5), lease.LeasedUntil);

        Advance(clock, TimeSpan.FromMinutes(2)); // past the original expiry, within the renewed one
        Assert.Empty(await queue.TakeAsync(10, Ct)); // still leased
        await lease.UpdateAsync("b", new MessageUpdate(), Ct); // still held
        Assert.Equal("b", lease.Message.GetPayload<string>());
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync("b", new MessageUpdate(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync(new MessageUpdate { Tags = ["x"] }, Ct));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.UpdateAsync("b", new MessageUpdate(), Ct));
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
    public void PutOptionsRejectEmptyMessageType() =>
        Assert.Throws<ArgumentException>(() => new MessagePutOptions(string.Empty));

    [Fact]
    public void PutOptionsRejectNullTagElement() =>
        Assert.Throws<ArgumentException>(() => new MessagePutOptions("t") { Tags = ["ok", null!] });

    [Fact]
    public void MessageUpdateRejectsNullTagElement() =>
        Assert.Throws<ArgumentException>(() => new MessageUpdate { Tags = [null!] });
}
