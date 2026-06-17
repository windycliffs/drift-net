# Usage

> Detailed usage guide for Drift — the public API, common scenarios, and
> examples. Replace this stub as the project takes shape.

## WindyCliffs.Drift.Abstractions

Add a reference to the package:

```
dotnet add package WindyCliffs.Drift.Abstractions
```

The message-processing contracts live under the `WindyCliffs.Drift.Messaging`
namespace.

### The message

A **message** carries **metadata** and a **payload**:

- **Metadata** (`IMessageMetadata`) is the information consumed by the generic
  processing layer, independent of the payload type. It carries the message type
  (`MessageType`), the creation time (`CreatedAt`), and an optional expiration
  time (`ExpiresAt`, `null` when the message does not expire).
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
