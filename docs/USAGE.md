# Usage

> Detailed usage guide for Drift — the public API, common scenarios, and
> examples. Replace this stub as the project takes shape.

## WindyCliffs.Drift

Add a reference to the package:

```
dotnet add package WindyCliffs.Drift
```

The message-processing contracts live under the `WindyCliffs.Drift.Messaging`
namespace.

### The message

A **message** carries **metadata** and a **payload**:

- **Metadata** (`IMessageMetadata`) is the information consumed by the generic
  processing layer, independent of the payload type. It carries the identifier
  (`Id`), the message type (`MessageType`), an opaque concurrency token
  (`Version`, for example a storage ETag), the creation time (`CreatedAt`), the
  last-modified time (`LastModifiedAt`), an optional expiration time (`ExpiresAt`,
  `null` when the message does not expire), an optional visibility time
  (`InvisibleBefore`, `null` when the message is visible immediately), and
  free-form `Tags`.
- **Payload** is the information consumed by specific message-processor
  implementations, exposed as the covariant `TPayload` on `IMessage<out TPayload>`.

```csharp
using WindyCliffs.Drift.Messaging;

void Handle(IMessage<OrderPlaced> message)
{
    IMessageMetadata metadata = message.Metadata; // generic layer
    OrderPlaced payload = message.Payload;        // specific processor
}
```

The non-generic `IMessage` exposes only `Metadata`, so the generic processing
layer can handle any message without knowing its payload type.

### The message queue

`IMessageQueue<TPayload>` is a worker queue over messages:

- `PutAsync(id, payload, options)` enqueues a payload under a caller-supplied id;
  the queue assigns the `Version` and `CreatedAt` and returns the stored message.
- `TryGetAsync(id)` reads a message by id (regardless of visibility), or `null`.
- `TakeAsync(count)` reads up to `count` currently-visible messages in insertion
  order. This is a non-exclusive, non-destructive read — two callers may see the
  same message.
- `LeaseAsync(message, leaseDuration)` claims a message for exclusive
  processing, returning an `IMessageLease<TPayload>` or `null` if it could not be
  claimed.
- `EstimateCountAsync()` returns an approximate message count.

The lease (an `IAsyncDisposable`) carries the operations that are valid only
while the message is held — `UpdateAsync`, `RenewAsync`, `ReleaseAsync`, and
`RemoveAsync`. Disposing the lease releases it if it is still held.

```csharp
using WindyCliffs.Drift.Messaging;

IMessageQueue<OrderPlaced> queue = new InMemoryMessageQueue<OrderPlaced>();

await queue.PutAsync(order.Id, order, new MessagePutOptions("order.placed"));

foreach (var candidate in await queue.TakeAsync(10))
{
    await using var lease = await queue.LeaseAsync(candidate, TimeSpan.FromMinutes(1));
    if (lease is null)
    {
        continue; // claimed by another worker
    }

    Process(lease.Message.Payload);
    await lease.RemoveAsync();
}
```

`InMemoryMessageQueue<TPayload>` is a non-durable, single-process implementation
of `IMessageQueue<TPayload>` suitable for tests and local development. It reads
time from `SystemClock.Instance` by default; pass an `IClock` (from the
`WindyCliffs.Clock` package) — for example a `MockClock` — to control time in
tests.
