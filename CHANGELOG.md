# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0]

### Added
- Initial project scaffolding.
- `WindyCliffs.Drift` library.
- Core message-processing abstractions in the `WindyCliffs.Drift.Messaging`
  namespace: `IMessageMetadata` (id, message type, opaque `Version` concurrency
  token, creation time, optional expiration time, optional `InvisibleBefore`
  visibility time), `IMessage` (metadata-only surface for the generic processing
  layer), and the covariant `IMessage<TPayload>` (payload for specific
  processors).
- Message queue abstraction `IMessageQueue<TPayload>` (put, take, lease, estimate
  count) and the lease handle `IMessageLease<TPayload>` (update, release, remove —
  valid only while leased), with `MessagePutOptions` and `MessageUpdate<TPayload>`.
- `InMemoryMessageQueue<TPayload>`, a non-durable single-process implementation
  of the queue.

### Changed
- Target `net8.0` (LTS) for broad consumer compatibility (consumable by net8/9/10).
