# Architecture

> Describe the design of Drift — the main components, key decisions, and how they
> fit together. Replace this stub as the project takes shape.

Drift is organised as a set of independent NuGet packages. `Drift.Abstractions`
sits at the base of the dependency graph and defines the contracts (interfaces
and core types) for message processing and work scheduling that the other
packages implement and depend upon.

## The message

The central concept of message processing is the **message**. A message carries
two things:

- **Metadata** — the information consumed by the *generic* processing layer,
  independent of the payload. It includes the message type and the time of
  creation, plus an optional expiration time. Modelled by `IMessageMetadata` in
  the `WindyCliffs.Drift.Messaging` namespace.
- **Payload** — the information consumed by *specific* implementations of message
  processors. Modelled as the covariant type parameter of `IMessage<out TPayload>`.

This split is reflected in the contracts: the non-generic `IMessage` exposes only
`Metadata`, allowing the generic processing layer to handle any message uniformly,
while `IMessage<TPayload>` adds the strongly-typed `Payload` that specific
processors operate on.
