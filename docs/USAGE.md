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

A **message** (`IMessage`) carries its **metadata** as properties and a **payload**
retrieved with `GetPayload<TPayload>()`:

- **Metadata** is the information consumed by the generic processing layer,
  independent of the payload type: the identifier (`Id`), the message type
  (`MessageType`), an opaque concurrency token (`Version`, for example a storage
  ETag), the creation time (`CreatedAt`), the last-modified time (`LastModifiedAt`),
  an optional expiration time (`ExpiresAt`, `null` when the message does not
  expire), an optional visibility time (`InvisibleBefore`, `null` when the message
  is visible immediately), and free-form `Tags`.
- **Payload** is the information consumed by specific message-processor
  implementations, retrieved with `GetPayload<TPayload>()` (which throws
  `InvalidCastException` if the payload is not of the requested type).

```csharp
using WindyCliffs.Drift.Messaging;

void Handle(IMessage message)
{
    var type = message.MessageType;             // generic layer
    var payload = message.GetPayload<OrderPlaced>(); // specific processor
}
```

`IMessage` is non-generic, so the generic processing layer can handle any message
without knowing its payload type.

### The message queue

`IMessageQueue` is a worker queue over messages:

- `PutAsync<TInput>(id, input, builder)` enqueues a message under a caller-supplied
  id. A delegate configures it through an `IMessageBuilder` — it must set the message
  type and payload, and may set an expiry, a visibility time, and tags. The queue
  assigns the `Version` and `CreatedAt` and returns the stored message.
- `TryGetAsync(id)` reads a message by id (regardless of visibility), or `null`.
- `TakeAsync(count)` reads up to `count` currently-visible messages in the queue's
  processing order. This is a non-exclusive, non-destructive read — two callers may
  see the same message.
- `LeaseAsync(message, leaseDuration)` claims a message for exclusive processing,
  returning an `IMessageLease` or `null` if it could not be claimed.
- `EstimateCountAsync()` returns an approximate message count.

The lease (an `IAsyncDisposable`) carries the operations that are valid only while
the message is held — `UpdateAsync` (configure properties through a builder, leaving
the payload untouched, or the payload-bearing overload to also replace it),
`RenewAsync`, `ReleaseAsync`, and `RemoveAsync`. Disposing the lease releases it if
it is still held.

`InMemoryMessageQueue` stores payloads in serialized form, so it takes an
`IMessagePayloadSerializer` — supply one that serializes a payload to a `Stream` and
reads it back (e.g. a MessagePack- or JSON-based implementation):

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
        continue; // claimed by another worker
    }

    Process(lease.Message.GetPayload<OrderPlaced>());
    await lease.RemoveAsync();
}
```

`InMemoryMessageQueue` is a non-durable, single-process implementation of
`IMessageQueue` suitable for tests and local development. It reads time from
`SystemClock.Instance` by default; pass an `IClock` (from the `WindyCliffs.Clock`
package) — for example a `MockClock` — alongside the serializer to control time in
tests.

### Errors

Queue-specific failures derive from `MessageQueueException`, so you can catch that to
handle any of them, or catch a specific type:

- `MessagePayloadSerializationException` — a payload could not be serialized (put or
  update) or deserialized (`GetPayload`). It wraps the serializer's own exception as
  `InnerException` and carries the `MessageId` and `PayloadType`.
- `MessageLeaseLostException` — a lease operation (update, renew, release, remove) ran
  after the lease was lost: it expired, was already released or removed, or the message
  was claimed elsewhere.
- `MessageQueueStorageException` — the backing store failed; raised by durable
  implementations (the in-memory queue keeps its state in process and does not throw it).

Caller mistakes — a null id, a null builder, a non-positive lease duration, or a builder
that does not set the message type and payload — surface as the standard
`ArgumentException` / `InvalidOperationException` types, not as `MessageQueueException`.
