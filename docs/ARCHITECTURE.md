# Architecture

> Describe the design of Drift — the main components, key decisions, and how they
> fit together. Replace this stub as the project takes shape.

Drift is organised as a set of independent NuGet packages. `Drift` sits at the
base of the dependency graph and defines the contracts (interfaces and core
types) for message processing and work scheduling that the other packages
implement and depend upon, along with an in-memory message queue.

## The message

The central concept of message processing is the **message**. A message carries
two things:

- **Metadata** — the information consumed by the *generic* processing layer,
  independent of the payload. It includes the identifier, message type, an opaque
  concurrency token (`Version`), the creation time, an optional expiration time,
  and an optional visibility time. Modelled by `IMessageMetadata` in the
  `WindyCliffs.Drift.Messaging` namespace.
- **Payload** — the information consumed by *specific* implementations of message
  processors. Modelled as the covariant type parameter of `IMessage<out TPayload>`.

This split is reflected in the contracts: the non-generic `IMessage` exposes only
`Metadata`, allowing the generic processing layer to handle any message uniformly,
while `IMessage<TPayload>` adds the strongly-typed `Payload` that specific
processors operate on.

## The message queue

`IMessageQueue<TPayload>` is a worker queue over messages. Workers read
candidates with `TakeAsync` (a non-exclusive read), then claim one with
`LeaseAsync`, which hands back an `IMessageLease<TPayload>`. The mutating
operations — `UpdateAsync`, `ReleaseAsync`, `RemoveAsync` — live on the lease, so
they are expressible only while the message is leased; this encodes the
"only one worker processes a message" invariant in the type system rather than at
runtime. The lease is an `IAsyncDisposable` whose disposal releases the message
if it is still held.

Exclusivity is built on two metadata fields: `Version` (an opaque
optimistic-concurrency token, compared for equality) and `InvisibleBefore` (a
visibility timeout). Leasing makes a message invisible until the lease expires;
releasing makes it visible again. `InMemoryMessageQueue<TPayload>` is a
non-durable, single-process implementation used for tests and local development.
