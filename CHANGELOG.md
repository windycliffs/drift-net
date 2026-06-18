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
  token, creation and last-modified times, optional expiration time, optional
  `InvisibleBefore` visibility time, and tags), `IMessage` (metadata-only surface
  for the generic processing layer), and the covariant `IMessage<TPayload>`
  (payload for specific processors).
- Message queue abstraction `IMessageQueue<TPayload>` (put under a caller-supplied
  id, get-by-id, take, lease, estimate count) and the lease handle
  `IMessageLease<TPayload>` (update, renew, release, remove — valid only while
  leased), with `MessagePutOptions` and a fully-optional `MessageUpdate<TPayload>`.
- `InMemoryMessageQueue<TPayload>`, a non-durable single-process implementation of
  the queue, backed by an insertion-ordered concurrent store and reading time
  through the `WindyCliffs.Clock` `IClock` abstraction.

### Changed
- Target `net8.0` (LTS) for broad consumer compatibility (consumable by net8/9/10).
