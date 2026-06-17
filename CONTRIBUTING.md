# Contributing

Thanks for contributing to `Drift`. This guide covers building, testing,
code style, and how the project is versioned and released.

## Building and testing

All build artefacts live under `src/`, which is the build root — run every
`dotnet` command from there so `global.json` (the pinned SDK) is honoured:

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```

The build must be **warning-free** and all tests must pass on every target
framework before a change is merged. Pull requests are validated automatically by
`.github/workflows/pull-request.yml`.

## Code style

C# style is defined in `src/.editorconfig` and enforced during the build
(`EnforceCodeStyleInBuild` with warnings treated as errors). Notable enforced
conventions:

- Namespaces are **file-scoped** where the language version permits (C# 10+).
- Members are qualified with **`this.`**.
- `using` directives go **inside** the namespace.
- Types and non-field members are **PascalCase with no underscores**.

Keep new code consistent with the surrounding files.

## Versioning

The version is **not** edited in the `.csproj`. It is assembled in
`src/Directory.Build.props` from three properties:

```xml
<MajorVersion>…</MajorVersion>
<MinorVersion>…</MinorVersion>
<Revision>…</Revision>
```

(The above shows the structure only — the authoritative current values live in
[`src/Directory.Build.props`](src/Directory.Build.props).) These drive `Version`,
`AssemblyVersion`, and `FileVersion`. To change the published version, bump the
appropriate property there.

Apply these rules when deciding what to bump:

1. **No breaking changes** — the public API stays backward-compatible. Bump:
   - **Revision** for bug fixes and other small, low-risk changes; or
   - **Minor** for new, additive functionality (and reset `Revision` to `0`).

   Use judgement about the scope of the change to choose between the two.
2. **Breaking changes** — any change that is not backward-compatible for
   consumers (removing or renaming public members, changing signatures or
   observable behaviour). Increment **Major** (and reset `Minor` and `Revision`
   to `0`).

> A breaking change to a published library must be confirmed with the human
> maintainer before it is made. See [AGENTS.md](AGENTS.md).

### Pre-1.0 versioning (temporary)

While `MajorVersion` is `0` (pre-release), a breaking change does **not**
increment the Major version. Keep Major at `0` and bump **Minor** instead
(resetting `Revision` to `0`) — **unless the maintainer explicitly instructs a
Major bump**. This overrides rule 2 above for as long as the project is pre-1.0.

**Remove this entire subsection once the version reaches `1.0.0`**, after which
the standard rule 2 (breaking → Major) applies.

Whenever you bump the version, add a new `## [X.Y.Z]` section at the top of
[CHANGELOG.md](CHANGELOG.md), with the version matching the one set in
`src/Directory.Build.props`.

## Package README (published libraries only)

The NuGet package ships a README rendered on nuget.org. It is a separate file
from the repository-root `README.md` (which targets GitHub readers):

- **Location:** `src/Drift.Abstractions/README.md`.
- **How it is included:** the library `.csproj` sets
  `<PackageReadmeFile>README.md</PackageReadmeFile>` and packs the file via a
  `<None Include="README.md" Pack="true" PackagePath="/" />` item. The file must
  exist at build time — `GeneratePackageOnBuild` packs it during `dotnet build`,
  so a missing or misnamed file fails the build with `NU5039`.
- **When to update:** keep it current whenever the public API or recommended
  usage changes. It is aimed at external consumers — keep it concise and
  example-led.

## Releasing (published libraries only)

Publishing is automated by `.github/workflows/release.yml` (added by the
`dotnet-add-nuget-release` skill). Creating a **published GitHub Release** builds
and tests from `src/`, packs the library, and pushes the package to NuGet via
**trusted publishing (OIDC)** — no long-lived API key. Before tagging a release,
make sure the version in `src/Directory.Build.props` and the `CHANGELOG.md` entry
are updated. The one-time nuget.org Trusted Publishing setup is described in that
skill and must be completed before the first release.
