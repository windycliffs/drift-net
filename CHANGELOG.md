# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0]

### Added
- Initial project scaffolding.
- `WindyCliffs.Drift.Abstractions` library.
- Core message-processing abstractions in the `WindyCliffs.Drift.Messaging`
  namespace: `IMessageMetadata` (message type, creation time, optional
  expiration time), `IMessage` (metadata-only surface for the generic processing
  layer), and the covariant `IMessage<TPayload>` (payload for specific
  processors).

### Changed
- Target `net8.0` (LTS) for broad consumer compatibility (consumable by net8/9/10).
