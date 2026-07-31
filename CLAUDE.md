# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TerseSharp is a Roslyn-powered MCP server shipped as the .NET global tool `terse` (package id
`TerseSharp`, assembly name `terse`). It gives a coding agent semantic navigation, editing and
refactoring of a C# solution so it never has to `Read`/`Grep`/line-`Edit` a `.cs` file.

Prime directive, stated in the README and used to settle design arguments: **save tokens, increase
speed**. A tool that does not beat the built-in it replaces does not ship, and a tool without an E2E
test is not done.

## Commands

```bash
dotnet build TerseSharp.slnx
dotnet test  TerseSharp.slnx                      # unit + E2E

# one project / one test
dotnet test tests/TerseSharp.UnitTests/TerseSharp.UnitTests.csproj
dotnet test tests/TerseSharp.E2ETests/TerseSharp.E2ETests.csproj --filter "FullyQualifiedName~NavigationToolsE2ETests"

# required before a PR (CI runs both on ubuntu)
dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info
dotnet format style     TerseSharp.slnx --verify-no-changes --severity info

# run the server by hand / package it
dotnet run --project src/TerseSharp.Server -- serve --workspace fixtures/FixtureSolution/FixtureSolution.slnx
dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg
```

.NET 10 SDK required (`global.json` pins `10.0.300`). Central package management: add versions to
`Directory.Packages.props`, never inline in a `.csproj`.

**E2E tests need `TerseSharp.Server` built first, in the same configuration as the test binaries** —
`TerseServerFixture` launches `src/TerseSharp.Server/bin/<Configuration>/net10.0/terse.dll` as a real
child process over stdio and throws `build TerseSharp.Server first` if it is missing.

## Architecture

Two projects, one rule between them: **`TerseSharp.Core` holds all logic, `TerseSharp.Server` holds
only MCP plumbing.**

The tool surface is **56 tools**. `src/TerseSharp.Core` — Roslyn services, each a static class returning `Result<string>` or a
formatted string: `OutlineService`, `SourceService`, `SymbolSearch`, `ReferenceService`,
`RenameService`, `RefactorService`, `SymbolEditService`, `AnalysisService`, `DeadCodeService`,
`DiagnosticsService`, `FormatService`, `TextSearchService`, `FileService`, `XamlService`,
`XamlBindingService`, `XamlResourceGraph`, `ProjectFile`/`SolutionFile`. Supporting value types:
`SymbolReference` (short names and the query they parse into), `UsageContainer` (the declaration a
usage sits in, from syntax alone), `TestScope` (`src`/`test` per project), `XamlFiles` (the guarded
workspace walk).

`src/TerseSharp.Server` — `Program.cs` (System.CommandLine: `serve`/`install`/`uninstall`/`doctor`;
bare args default to `serve`), `McpHost` (generic host + stdio transport + `WithToolsFromAssembly`),
`Tools/*.cs` (the `[McpServerToolType]` classes), `ClientRegistrar` + `Doctor` + `SkillAsset`
(installs into `~/.claude.json` or `$CLAUDE_CONFIG_DIR`, Cursor, VS Code, Windsurf; `SKILL.md` is an
embedded resource), `DotnetRunner` (the only shell-out: `dotnet build` / `dotnet test`, deadlined).

### Request pipeline

Every tool method is a one-liner delegating to `ToolContext`:

- `ToolContext.WithWorkspace(Async)(workspace, pathHint, action)` resolves the workspace, or returns
  a rendered `ERROR` string.
- `ToolContext.WithSymbolAsync(workspace, symbolId, action)` additionally resolves the symbol id.
- `ToolContext.WithTargetAsync(workspace, pathHint, action)` hands over only the solution path and
  root and **releases the lease first** — for `build`/`run_tests`, which shell out and must be able
  to unload the workspace to release its MSBuild file locks.
- `ToolContext.RejectWrite()` is the `--read-only` gate; every mutating tool must call it first.
- `ToolBoundary.Run(Async)` catches expected exceptions and renders them; unexpected ones rethrow.

`WorkspaceRegistry` (LRU, default 4) owns the loaded `MSBuildWorkspace`s. `Resolve` hands out a
`WorkspaceLease`; an evicted or unloaded workspace is disposed only once the last lease is released,
so a call in flight never loses its solution. Resolution is deliberate:
an explicit `workspace` hint, else a path hint that lands inside exactly one root, else the single
loaded workspace, else `ERROR AmbiguousWorkspace` listing candidates — **never a guess**, because an
answer from the wrong worktree is undetectable by the agent. `LoadedWorkspace` carries `GitContext`
(branch + worktree name) and up to ten undo snapshots for `undo_last_change`.

### Errors and responses

Never throw a bare message and never return prose. Failures go through `Errors.*` →
`TerseError(Code, Message, Remedy)` → `ERROR <Code>\n<message>\nremedy: <remedy>`; add new codes to
`TerseErrorCode`. Success goes through `ResponseBuilder`: header line, then
`N unit (truncated=…, total=…)`, then one record per line, each tagged `EXACT` (Roslyn-resolved) or
`HEURISTIC` (`ConfidenceTag.Of`). **Every path in a response is workspace-relative**
(`PositionFormat.Relative`); only a file outside the workspace root is printed in full.

### Edits

All mutations funnel through `EditGate.ApplyAsync`, which diffs only the changed documents, compares
error counts before/after, **rolls back any edit that introduces a new compile error** (unless
`allowErrors: true`) in the changed projects **and every project that transitively depends on them**,
and returns the unified diff plus a changed-line count — never file contents. Every mutation and
every `dryRun` also reports `errors=N (+D) warnings=N (+D)`; a `dryRun` that *would* be rolled back
says so and names the errors, because the delta alone is not a rollback oracle (one error can
disappear as another appears). `allowErrors: true` skips the analysis entirely and is the way back to
a cheap diff-only preview. Paths are checked with `PathBoundary.Contains`, which compares whole
segments (`C:\repo` does not contain `C:\repoEvil`).

### Addressing a symbol

Symbols are addressed by Roslyn documentation-comment ids (`M:Trading.OrderService.Submit(Trading.Order)`)
via `SymbolId`, so edits are immune to line drift — **or** by name: `OrderService.Submit`,
`Fixture.Trading.OrderService.Submit`, `Submit`, and `Reconcile(Dictionary<string,int>, Order)` when a
parameter list is needed to pick an overload. `SymbolLookup` routes on `SymbolReference.IsDocumentationId`.
The name path never guesses: a qualifier must match a trailing run of the containing type's fully
qualified name (a namespace only when the symbol is itself a type), parameters are split at nesting
depth zero and compared by type name, an ambiguous name returns `AmbiguousSymbol` declaring how many
of the total it lists, and a name whose search saturates its cap is refused rather than resolved from
a truncated set. Outlines print the short form by default (`ids=full` for documentation ids) **only
where it round-trips** — `SymbolReference.RoundTrips` keeps the documentation id for constructors,
destructors, operators, indexers, explicit interface implementations, generic methods and members of
generic types, because a name cannot address those. An E2E test feeds every reference an outline
prints back into `get_symbol` and asserts none errors.

## 🚫 HARD GATE — the docs ship with the change, not after it

`README.md`, `NUGET_README.md` and `src/TerseSharp.Server/Assets/SKILL.md` are **part of the tool
surface**, not documentation about it. The README is what a user reads before installing, the NuGet
README is what nuget.org renders, and `SKILL.md` is shipped by `terse install --skill` and loaded into
an agent's context — a stale skill actively teaches the wrong call.

Before any commit that adds, removes or changes a tool, a parameter, a default or a response format,
answer all four:

1. **README** — tool table, tool count, the "what each one replaces" table, the numbers table, and the
   Status table (move the row out of 🔜 when it ships).
2. **NUGET_README** — the same, in pure Markdown; it is a separate file and diverges silently.
3. **SKILL.md** — the swap table and the working rules. A new tool that an agent is not told about
   might as well not exist; a changed response format that the skill still describes the old way is
   worse than no skill.
4. **CHANGELOG** — under `## [Unreleased]`, with the format change spelled out.

A commit that changes behaviour and leaves any of the four stale is incomplete. "I'll update the docs
after" is the same failure as "I'll add the test after": both are how a 54-tool surface drifts away
from what it claims to be. When you cannot update one of them in the same commit, say which and why in
the commit body.

## Adding or changing a tool

1. Logic in `TerseSharp.Core`, returning `Result<string>`; the `Tools` class only wires it up.
2. `[McpServerTool(Name = "snake_case_name")]` plus a `[Description]` written for an agent — say what
   it returns *and which built-in it replaces*.
3. **Every optional parameter needs a C# default** (`string? workspace = null`). Without one the MCP
   SDK marks it required and the tool fails at call time.
4. Add the tool name to the `Exercised` set in `tests/TerseSharp.E2ETests/ToolCoverageE2ETests.cs` —
   two tests fail if the advertised list and that set diverge in either direction.
5. One E2E test that asserts response **values** (never "did not throw") against
   `fixtures/FixtureSolution`; `fixtures/BrokenSolution` exists for load-failure and diagnostics
   paths. Fixtures are intentionally outside `TerseSharp.slnx`.
   `ToolRobustnessE2ETests` then covers the new tool automatically: it reads `tools/list` and calls
   every tool with garbage, empty and missing arguments, asserting a structured answer with a
   `remedy:` line and that nothing is written outside the workspace. A listing tool also belongs in
   `TokenBudgetE2ETests` — budget it against the **widest** symbol or file in the fixture, because a
   budget measured on a narrow one cannot see a regression (a format change that tripled the cost of
   `find_usages` passed a 4-usage assertion unchanged).
6. Unit tests for formatting and error paths.
7. Update `CHANGELOG.md` under `## [Unreleased]`, the tool tables in `README.md`, and `NUGET_README.md`
   (a separate pure-Markdown copy — nuget.org does not render the GitHub README's HTML).

Removing or renaming a tool, making a parameter required, or changing a response format is a
**MAJOR** version change: the tool surface is a public contract (see `RELEASING.md`). Record the
format change in `CHANGELOG.md` at the time you make it — the banner at the top of the release notes
is assembled from those entries, and a format change that is not written down is indistinguishable
from an accident.

### The rule the reviews keep enforcing

**Never answer something you cannot prove.** An empty result, a `(+0)` delta, a resolved name, an
`EXACT` tag — each is a claim. Where the claim cannot be supported, say so in the response
(`UNRESOLVED_CONTEXT`, `AmbiguousSymbol`, `SaturatedName`, `HEURISTIC`, `WARNING … would be rolled
back`) rather than returning a confident wrong answer. A false positive costs an agent more than no
answer, because it cannot detect it.

## Code style

`Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and
`AnalysisLevel=latest-recommended`, so analyzer warnings fail the build. Immutable records, `sealed`
by default, pattern matching and switch expressions over `if`/`else` ladders, explicit
`IFormatProvider` on every culture-sensitive format (`string.Create(CultureInfo.InvariantCulture, $"…")`,
never bare interpolation as a converter — `System.Globalization` is a global using), and **no
comments**: make the code say it.

## 🚫 HARD GATE — a release is not cut until the changelog links it

Every `## [x.y.z] - yyyy-mm-dd` heading in `CHANGELOG.md` has a matching link definition at the bottom
of the file, and `[Unreleased]` compares against the newest tag. Those link definitions are what make
the version headings clickable; a release whose heading exists but whose link does not is a dead
reference on nuget.org and on the GitHub release page.

When tagging `vX.Y.Z`, in the same commit as the tag's content:

1. Rename `## [Unreleased]` to `## [X.Y.Z] - <today, ISO>` and open a fresh empty `## [Unreleased]`.
2. Add the link definition for the new version at the bottom:
   `[X.Y.Z]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/vX.Y.Z`
3. Repoint the unreleased comparison at the new tag:
   `[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/vX.Y.Z...HEAD`
4. Verify: every `## [` heading except `[Unreleased]` has a `[` link definition, and every link
   definition names a tag that exists (`git tag --list`).

Do not create the GitHub release before the changelog says the version exists — the release notes are
read from it, and a tag pushed against a changelog that still says `[Unreleased]` publishes a release
describing nothing.

## Versioning

Versions come from git tags via MinVer (`v` prefix); no version is stored in any file. Releases are
cut by tagging — see `RELEASING.md`. CI must fetch full history or MinVer produces a wrong version.
