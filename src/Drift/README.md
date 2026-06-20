# WindyCliffs.Drift

Message processing and work scheduling for .NET — the core contracts plus an
in-memory message queue. The base of the
[Drift](https://github.com/windycliffs/drift-net) suite.

## Install

```
dotnet add package WindyCliffs.Drift
```

## Usage

The message-processing contracts live under the `WindyCliffs.Drift.Messaging`
namespace. A **message** (`IMessage`) carries metadata as properties — consumed by
the generic processing layer — and a payload retrieved with `GetPayload<TPayload>()`,
consumed by specific processors:

```csharp
using WindyCliffs.Drift.Messaging;

void Handle(IMessage message)
{
    // Metadata is available directly, independent of the payload type.
    Console.WriteLine($"{message.MessageType} created at {message.CreatedAt}");

    if (message.ExpiresAt is { } expiry && expiry < DateTimeOffset.UtcNow)
    {
        return; // message has expired
    }

    // The strongly-typed payload is consumed by the specific processor.
    Process(message.GetPayload<OrderPlaced>());
}
```

`IMessage` exposes `Id`, `MessageType`, `Version` (an opaque concurrency token such
as an ETag), `CreatedAt`, `LastModifiedAt`, optional `ExpiresAt` and
`InvisibleBefore` (visibility timeout / scheduled delivery), `Tags`, and
`GetPayload<TPayload>()`.

### Message queue

`IMessageQueue` models a worker queue: `PutAsync` enqueues a message — a delegate
configures it through an `IMessageBuilder` (message type, payload, and any optional
expiry, visibility time, or tags) — `TryGetAsync` reads one by id, `TakeAsync` reads
currently-visible candidates (a non-exclusive read), and `LeaseAsync` claims one
for exclusive processing. The returned `IMessageLease` (an `IAsyncDisposable`)
exposes `UpdateAsync` (configure properties through a builder, or the payload-bearing
overload to also replace the payload), `RenewAsync`, `ReleaseAsync`, and
`RemoveAsync`, so those operations are possible only while the message is leased.
`EstimateCountAsync` returns an approximate depth.

`InMemoryMessageQueue` stores payloads in serialized form, so it takes an
`IMessagePayloadSerializer` (e.g. a MessagePack- or JSON-based implementation):

```csharp
using WindyCliffs.Drift.Messaging;

IMessagePayloadSerializer serializer = /* your serializer */;
IMessageQueue queue = new InMemoryMessageQueue(serializer);

await queue.PutAsync(order.Id, order, static (o, builder) =>
    builder.SetMessageType("order.placed").SetPayload(o));

foreach (var candidate in await queue.TakeAsync(10))
{
    await using var lease = await queue.LeaseAsync(candidate, TimeSpan.FromMinutes(1));
    if (lease is null)
    {
        continue; // another worker claimed it first
    }

    Process(lease.Message.GetPayload<OrderPlaced>());
    await lease.RemoveAsync();
}
```

`InMemoryMessageQueue` is a non-durable, single-process implementation suitable for
tests and local development. By default it reads time from `SystemClock.Instance`;
pass an `IClock` (from the
[WindyCliffs.Clock](https://www.nuget.org/packages/WindyCliffs.Clock) package)
alongside the serializer to control time in tests.

> See the [repository](https://github.com/windycliffs/drift-net) for the latest API.

## License

MIT
