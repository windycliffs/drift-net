# WindyCliffs.Drift.Abstractions

Core abstractions for message processing and work scheduling — the contracts the
rest of the [Drift](https://github.com/windycliffs/drift-net) suite builds on.

## Install

```
dotnet add package WindyCliffs.Drift.Abstractions
```

## Usage

The public API lives under the `WindyCliffs.Drift` namespace:

```csharp
using WindyCliffs.Drift;

var greeting = Greeter.Greet("World"); // "Hello, World!"
```

> This package currently ships a placeholder `Greeter` type while the
> abstractions take shape. See the [repository](https://github.com/windycliffs/drift-net)
> for the latest API.

## License

MIT
