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
namespace. A **message** carries **metadata** (consumed by the generic
processing layer) and a **payload** (consumed by specific processor
implementations):

```csharp
using WindyCliffs.Drift.Messaging;

void Handle(IMessage<OrderPlaced> message)
{
    // Metadata is available to the generic layer, independent of payload type.
    IMessageMetadata metadata = message.Metadata;
    Console.WriteLine($"{metadata.MessageType} created at {metadata.CreatedAt}");

    if (metadata.ExpiresAt is { } expiry && expiry < DateTimeOffset.UtcNow)
    {
        return; // message has expired
    }

    // The strongly-typed payload is consumed by the specific processor.
    Process(message.Payload);
}
```

The core contracts:

- `IMessageMetadata` — `Id`, `MessageType`, `Version` (an opaque concurrency
  token such as an ETag), `CreatedAt`, an optional `ExpiresAt`, and an optional
  `InvisibleBefore` (visibility timeout / scheduled delivery).
- `IMessage` — the non-generic surface exposing `Metadata` only.
- `IMessage<out TPayload>` — adds the covariant `Payload`.

### Message queue

`IMessageQueue<TPayload>` models a worker queue: `PutAsync` enqueues a payload,
`TakeAsync` reads currently-visible candidates (a non-exclusive read), and
`LeaseAsync` claims one for exclusive processing. The returned
`IMessageLease<TPayload>` (an `IAsyncDisposable`) exposes `UpdateAsync`,
`ReleaseAsync`, and `RemoveAsync`, so those operations are possible only while
the message is leased. `EstimateCountAsync` returns an approximate depth.

```csharp
using WindyCliffs.Drift.Messaging;

IMessageQueue<OrderPlaced> queue = new InMemoryMessageQueue<OrderPlaced>();

await queue.PutAsync(order, new MessagePutOptions("order.placed"));

foreach (var candidate in await queue.TakeAsync(10))
{
    await using var lease = await queue.LeaseAsync(candidate, TimeSpan.FromMinutes(1));
    if (lease is null)
    {
        continue; // another worker claimed it first
    }

    Process(lease.Message.Payload);
    await lease.RemoveAsync();
}
```

`InMemoryMessageQueue<TPayload>` is a non-durable, single-process implementation
suitable for tests and local development.

> See the [repository](https://github.com/windycliffs/drift-net) for the latest API.

## License

MIT
