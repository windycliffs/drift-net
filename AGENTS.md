# AGENTS.md

Guidance for AI agents working in this repository.

## What this is

`Drift` is a set of .NET libraries / NuGet packages for message processing and
work scheduling. The first package, `WindyCliffs.Drift` (built from the `Drift`
project), defines the core contracts the rest of the suite builds on, along with
an in-memory message queue. See [README.md](README.md) for an overview and quick
start.

## Repository layout

```
.
├── README.md            # Overview + quick start (for GitHub readers)
├── LICENSE              # MIT license
├── CHANGELOG.md         # Keep a Changelog history
├── CONTRIBUTING.md      # Build/test + versioning rules
├── AGENTS.md            # This file
├── docs/
│   ├── ARCHITECTURE.md  # Design and key decisions
│   └── USAGE.md         # Detailed usage
├── .github/workflows/   # CI/CD (added by the dotnet-add-pr-pipeline /
│   │                    #        dotnet-add-nuget-release skills)
│   ├── pull-request.yml # Builds + tests every PR (a required check)
│   └── release.yml      # Publishes to NuGet (publishable libraries only)
└── src/                 # Build root — all build artefacts live here
    ├── global.json              # Pins the .NET SDK band
    ├── Directory.Build.props    # Shared metadata + version assembly + quality gates
    ├── Directory.Build.targets  # Shared targets (placeholder)
    ├── Directory.Packages.props # Central Package Management
    ├── .editorconfig            # Code style (house style enforced)
    ├── repo.slnx                # Solution
    ├── Drift/                   # Production project
    └── Drift.Tests/             # xunit 3 tests
```

## Building and testing

`src/` is the build root. **Run `dotnet` from `src/`** so the pinned SDK in
`global.json` is resolved (it is found by walking up from the working directory,
so invoking `dotnet` at the repo root would silently use a different SDK):

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```

The build must be **warning-free** (warnings are treated as errors) and all
tests must pass on every target framework.

## Conventions

- **Code style** is defined in `src/.editorconfig` and enforced at build time.
  Notable, enforced rules: namespaces are **file-scoped** where the language
  version permits (C# 10+); members are **`this.`-qualified**; `using` directives
  go **inside** the namespace; types and non-field members are **PascalCase with
  no underscores**. Keep new code consistent with the surrounding files.
- The version is assembled from `<MajorVersion>`/`<MinorVersion>`/`<Revision>` in
  `src/Directory.Build.props`, following the rules in
  [CONTRIBUTING.md](CONTRIBUTING.md). Record every version change in
  `CHANGELOG.md`.
- For a published NuGet package, the package README is
  `src/Drift/README.md` (shipped in the package via
  `PackageReadmeFile` and rendered on nuget.org). It is **distinct from the
  repo-root `README.md`** (which targets GitHub readers). Keep it current
  whenever the public API or usage changes.

## Breaking changes

**Any breaking change to a published library must be confirmed with the human
user before it is made.** A breaking change is anything not backward-compatible
for consumers — removing or renaming public members, changing public signatures,
or altering observable behaviour. When a requested task would require one, stop
and get explicit confirmation first; then bump the version per
[CONTRIBUTING.md](CONTRIBUTING.md) — while `MajorVersion` is `0`, do **not** bump
Major unless explicitly told (see its "Pre-1.0 versioning" note).
