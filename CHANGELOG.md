# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0]

### Added
- Initial project scaffolding.
- `WindyCliffs.Drift` library.
- Core message-processing abstractions in the `WindyCliffs.Drift.Messaging`
  namespace: `IMessage` — a non-generic message carrying metadata as properties
  (id, message type, opaque `Version` concurrency token, creation and last-modified
  times, optional expiration time, optional `InvisibleBefore` visibility time, and
  tags) plus a `GetPayload<TPayload>()` accessor for the payload.
- Message queue abstraction `IMessageQueue` (`PutAsync` under a caller-supplied id,
  get-by-id, take, lease, estimate count) and the lease handle `IMessageLease`
  (update properties, or the payload-bearing overload to also replace the payload,
  renew, release, remove — valid only while leased). Messages are configured through
  an `IMessageBuilder` delegate at put and update time.
- `IMessagePayloadSerializer`, the abstraction a queue uses to store payloads in
  serialized form (serialize to / deserialize from a `Stream`).
- A queue-specific exception hierarchy rooted at `MessageQueueException`:
  `MessagePayloadSerializationException` (wraps a serializer failure on put/update/get),
  `MessageQueueStorageException` (wraps a backing-store failure in durable
  implementations), and `MessageLeaseLostException` (a lease operation found the lease no
  longer held). Argument-validation mistakes keep using the standard `ArgumentException`
  family.
- `InMemoryMessageQueue`, a non-durable single-process implementation of the queue,
  backed by a `SortedConcurrentDictionary` ordered by last-modified time, reading
  time through the `WindyCliffs.Clock` `IClock` abstraction and storing payloads as
  bytes via an injected `IMessagePayloadSerializer`.

### Changed
- Target `net8.0` (LTS) for broad consumer compatibility (consumable by net8/9/10).
