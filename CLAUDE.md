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

`src/TerseSharp.Core` — Roslyn services, each a static class returning `Result<string>` or a
formatted string: `OutlineService`, `SourceService`, `SymbolSearch`, `ReferenceService`,
`RenameService`, `RefactorService`, `SymbolEditService`, `AnalysisService`, `DeadCodeService`,
`DiagnosticsService`, `FormatService`, `TextSearchService`, `FileService`, `XamlService`,
`ProjectFile`/`SolutionFile`.

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
`HEURISTIC` (`ConfidenceTag.Of`).

### Edits

All mutations funnel through `EditGate.ApplyAsync`, which diffs only the changed documents, compares
error counts before/after, **rolls back any edit that introduces a new compile error** (unless
`allowErrors: true`) in the changed projects **and every project that transitively depends on them**,
and returns the unified diff plus a changed-line count — never file contents.
`dryRun` returns the diff and writes nothing. Symbols are addressed by Roslyn documentation-comment
ids (`M:Trading.OrderService.Submit(Trading.Order)`) via `SymbolId`, so edits are immune to line
drift. Paths are checked with `PathBoundary.Contains`, which compares whole segments (`C:\repo` does
not contain `C:\repoEvil`).

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
6. Unit tests for formatting and error paths.
7. Update `CHANGELOG.md` under `## [Unreleased]`, the tool tables in `README.md`, and `NUGET_README.md`
   (a separate pure-Markdown copy — nuget.org does not render the GitHub README's HTML).

Removing or renaming a tool, making a parameter required, or changing a response format is a
**MAJOR** version change: the tool surface is a public contract (see `RELEASING.md`).

## Code style

`Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and
`AnalysisLevel=latest-recommended`, so analyzer warnings fail the build. Immutable records, `sealed`
by default, pattern matching and switch expressions over `if`/`else` ladders, explicit
`IFormatProvider` on every culture-sensitive format (`string.Create(CultureInfo.InvariantCulture, $"…")`,
never bare interpolation as a converter — `System.Globalization` is a global using), and **no
comments**: make the code say it.

## Versioning

Versions come from git tags via MinVer (`v` prefix); no version is stored in any file. Releases are
cut by tagging — see `RELEASING.md`. CI must fetch full history or MinVer produces a wrong version.
