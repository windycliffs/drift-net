# WindyCliffs.Drift.Abstractions

Core abstractions for message processing and work scheduling — the contracts the
rest of the [Drift](https://github.com/windycliffs/drift-net) suite builds on.

## Install

```
dotnet add package WindyCliffs.Drift.Abstractions
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

- `IMessageMetadata` — `MessageType`, `CreatedAt`, and an optional `ExpiresAt`.
- `IMessage` — the non-generic surface exposing `Metadata` only.
- `IMessage<out TPayload>` — adds the covariant `Payload`.

> See the [repository](https://github.com/windycliffs/drift-net) for the latest API.

## License

MIT
