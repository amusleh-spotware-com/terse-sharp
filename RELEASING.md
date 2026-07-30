# Releasing

## How versioning works

The version is **derived from git tags** by [MinVer](https://github.com/adamralph/minver). There is
no version number stored in any file — nothing to bump, nothing to forget.

| Situation | Version produced |
|---|---|
| Tag `v0.2.0` is on `HEAD` | `0.2.0` |
| 5 commits after `v0.2.0` | `0.2.1-alpha.0.5` |
| No tag at all | `0.0.0-alpha.0.<height>` |
| Tag `v0.3.0-rc.1` on `HEAD` | `0.3.0-rc.1` (a prerelease) |

Config lives in `Directory.Build.props`: `MinVerTagPrefix=v`, `MinVerDefaultPreReleaseIdentifiers=alpha.0`.

> CI must check out with `fetch-depth: 0`. A shallow clone has no tags, so MinVer would fall back to
> `0.0.0-alpha.0` and publish a wrong version. Both workflows already do this.

## Semantic versioning, applied to this project

- **MAJOR** — a shipped MCP tool is removed or renamed, a parameter becomes required, or a response
  format changes in a way an agent could have parsed. The tool surface is a public contract.
- **MINOR** — a new tool, a new optional parameter, a new field in a response.
- **PATCH** — a bug fix that changes no contract.

Adding an optional parameter is MINOR. Removing one, or changing its default, is MAJOR.

## Cutting a release

```bash
# 1. make sure main is green and CHANGELOG.md has an entry under [Unreleased]
dotnet build TerseSharp.slnx && dotnet test TerseSharp.slnx

# 2. move the [Unreleased] entries under a new version heading, commit
git commit -am "Release 0.2.0"

# 3. tag and push - this is what triggers everything
git tag v0.2.0
git push origin main --follow-tags
```

The `Release` workflow then, on the tag:

1. checks out with full history so MinVer sees the tag,
2. builds and runs **all** tests,
3. packs `TerseSharp.<version>.nupkg`,
4. **smoke-tests the real artifact** — installs the packed tool globally and runs `terse doctor`
   against the fixture solution, so a broken package cannot be published,
5. pushes to NuGet.org (skipped when the `NUGET_API_KEY` secret is absent),
6. creates a GitHub Release with auto-generated notes and the `.nupkg` attached.

A tag containing `-` (e.g. `v0.3.0-rc.1`) is marked as a GitHub prerelease automatically.

## One-time setup for NuGet publishing

Add a repository secret named `NUGET_API_KEY` (Settings → Secrets and variables → Actions) holding a
nuget.org API key scoped to the `TerseSharp` package. Until that secret exists the publish step is
skipped and the release still produces a GitHub Release with the `.nupkg` attached — so you can dry
run the whole pipeline safely.

## How users update

```bash
dotnet tool update -g TerseSharp                 # latest stable
dotnet tool update -g TerseSharp --prerelease    # latest prerelease
dotnet tool install -g TerseSharp --version 0.2.0
```

`terse doctor` prints the resolved workspace and environment; `terse --version` prints the version
the tool was built from.

## Rolling back

Packages on nuget.org cannot be deleted, only **unlisted**. If a release is bad: unlist it on
nuget.org, fix forward, and tag a new patch version. Do not re-tag an existing version — the tag is
the identity of the build.
