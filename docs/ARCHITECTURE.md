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
  concurrency token (`Version`), the creation and last-modified times, an optional
  expiration time, an optional visibility time, and free-form tags. Modelled by
  `IMessageMetadata` in the `WindyCliffs.Drift.Messaging` namespace. Metadata is a
  separate interface from `IMessage` deliberately: it is the cohesive "view" the
  generic layer consumes, and it can be carried or inspected independently of a
  typed message (for example by the queue's storage and monitoring surfaces).
- **Payload** — the information consumed by *specific* implementations of message
  processors. Modelled as the covariant type parameter of `IMessage<out TPayload>`.

This split is reflected in the contracts: the non-generic `IMessage` exposes only
`Metadata`, allowing the generic processing layer to handle any message uniformly,
while `IMessage<TPayload>` adds the strongly-typed `Payload` that specific
processors operate on.

## The message queue

`IMessageQueue<TPayload>` is a worker queue over messages. A message is put under
a caller-supplied id (`PutAsync`) and can be read back by id (`TryGetAsync`).
Workers read candidates with `TakeAsync` (a non-exclusive read), then claim one
with `LeaseAsync`, which hands back an `IMessageLease<TPayload>`. The mutating
operations — `UpdateAsync`, `RenewAsync`, `ReleaseAsync`, `RemoveAsync` — live on
the lease, so they are expressible only while the message is leased; this encodes
the "only one worker processes a message" invariant in the type system rather than
at runtime. The lease is an `IAsyncDisposable` whose disposal releases the message
if it is still held.

Exclusivity is built on two metadata fields: `Version` (an opaque
optimistic-concurrency token, compared for equality) and `InvisibleBefore` (a
visibility timeout). Leasing makes a message invisible until the lease expires;
releasing makes it visible again; renewing extends the window.

`InMemoryMessageQueue<TPayload>` is a non-durable, single-process implementation
used for tests and local development. It reads time through the `IClock`
abstraction (from the `WindyCliffs.Clock` package, defaulting to
`SystemClock.Instance`), and keeps its state in an internal
`OrderedConcurrentDictionary` — a thread-safe, key-addressable, insertion-ordered
store — so the queue type itself only carries the queue semantics.
