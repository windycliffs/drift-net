# Drift

> A set of .NET libraries / NuGet packages for message processing and work scheduling.

Drift is a collection of focused, composable libraries. The first package,
[`WindyCliffs.Drift`](src/Drift/README.md), defines the core contracts that the
rest of the suite builds on, along with an in-memory message queue.

## Packages

| Package | Description |
| --- | --- |
| `WindyCliffs.Drift` | Message processing and work scheduling — core contracts and an in-memory message queue. |

## Build & test

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```

## Documentation

- [Usage](docs/USAGE.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Contributing](CONTRIBUTING.md)

## License

MIT — see [LICENSE](LICENSE).
