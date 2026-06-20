# Architecture

> Describe the design of Drift — the main components, key decisions, and how they
> fit together. Replace this stub as the project takes shape.

Drift is organised as a set of independent NuGet packages. `Drift` sits at the
base of the dependency graph and defines the contracts (interfaces and core
types) for message processing and work scheduling that the other packages
implement and depend upon, along with an in-memory message queue.

## The message

The central concept of message processing is the **message**, `IMessage` in the
`WindyCliffs.Drift.Messaging` namespace. It carries two things:

- **Metadata** — the information consumed by the *generic* processing layer,
  independent of the payload: the identifier, message type, an opaque concurrency
  token (`Version`), the creation and last-modified times, an optional expiration
  time, an optional visibility time, and free-form tags. These are exposed as
  properties directly on `IMessage`.
- **Payload** — the information consumed by *specific* implementations of message
  processors. Retrieved with `IMessage.GetPayload<TPayload>()`, so the message
  itself stays non-generic.

Keeping `IMessage` non-generic means the queue can hold heterogeneous payloads and
the generic processing layer can handle any message uniformly through the metadata
properties; a specific processor recovers its strongly-typed payload with
`GetPayload<TPayload>()` (which throws if the stored payload is a different type).

## The message queue

`IMessageQueue` is a worker queue over messages. A message is put under a
caller-supplied id (`PutAsync<TPayload>`) and can be read back by id
(`TryGetAsync`). Workers read candidates with `TakeAsync` (a non-exclusive read),
then claim one with `LeaseAsync`, which hands back an `IMessageLease`. The mutating
operations — `UpdateAsync` (properties only, or with a new payload), `RenewAsync`,
`ReleaseAsync`, `RemoveAsync` — live on the lease, so they are expressible only
while the message is leased; this encodes the "only one worker processes a message"
invariant in the type system rather than at runtime. The lease is an
`IAsyncDisposable` whose disposal releases the message if it is still held. Only the
operations that need the payload type are generic on the method (`PutAsync<TPayload>`,
`IMessageLease.UpdateAsync<TPayload>`); the interfaces themselves are not.

Exclusivity is built on two metadata fields: `Version` (an opaque
optimistic-concurrency token, compared for equality) and `InvisibleBefore` (a
visibility timeout). Leasing makes a message invisible until the lease expires;
releasing makes it visible again; renewing extends the window.

`InMemoryMessageQueue` is a non-durable, single-process implementation
used for tests and local development. It reads time through the `IClock`
abstraction (from the `WindyCliffs.Clock` package, defaulting to
`SystemClock.Instance`), and keeps its state in an internal
`SortedConcurrentDictionary` — a thread-safe, key-addressable store that keeps its
values in the order of an injected comparer — so the queue type itself only carries
the queue semantics. The queue orders messages by `LastModifiedAt`
(least-recently-modified first), so leasing, updating, or releasing a message moves
it to the back of the line.
