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
5. exchanges the GitHub OIDC token for a short-lived NuGet key and pushes to NuGet.org,
6. creates or updates the GitHub Release with auto-generated notes and the `.nupkg` attached.

A tag containing `-` (e.g. `v0.3.0-rc.1`) is marked as a GitHub prerelease automatically.

## Publishing credentials: none

Publishing uses **NuGet trusted publishing** — there is no API key anywhere, in the repo or in
Actions secrets. The job requests a GitHub OIDC token, `NuGet/login@v1` exchanges it for a key that
lives minutes, and that key pushes the package.

nuget.org validates four things against the registered policy, all of which must keep matching:

| Policy field | Value |
|---|---|
| Package owner | `AlgoDeveloper` |
| Repository owner | `amusleh-spotware-com` (id `8859902`) |
| Repository | `terse-sharp` (id `1317484693`) |
| Workflow | `release.yml` |
| Environment | `production` |

That is why the job declares `environment: production` and `permissions: id-token: write`. Renaming
the workflow file, the repository, or the environment breaks publishing until the policy is updated
on nuget.org.

> A newly created policy can start in a **7-day probation window**. If nothing is published inside
> it the policy goes inactive; you can restart the window from the nuget.org UI at any time.

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
