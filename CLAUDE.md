# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

TerseSharp is a Roslyn-powered MCP server shipped as the .NET global tool `terse` (package id
`TerseSharp`, assembly name `terse`). It gives a coding agent semantic navigation, editing and
refactoring of a C# solution so it never has to `Read`/`Grep`/line-`Edit` a `.cs` file.

Prime directive, stated in the README and used to settle design arguments: **save tokens, increase
speed**. A tool that does not beat the built-in it replaces does not ship, and a tool without an E2E
test is not done.

## 🚫 HARD GATE — develop TerseSharp with TerseSharp

Every read, search, edit, refactor, build and test **of this repository** goes through the installed
`terse` MCP server: `get_file_outline` / `get_symbol_source` instead of `Read`, `search_symbols` /
`find_usages` instead of `Grep`, `find_files` instead of `Glob`, `replace_symbol_body` /
`replace_symbol` / `add_member` instead of `Edit`, `read_text` / `edit_text` / `write_text` for
`.md`, `.csproj`, `.slnx` and `.json`, `build` and `run_tests` instead of shelling out to `dotnet`.

This is not style. This repo is the one place where the server is driven by the agent that also
maintains it, so **every session is the product's own usability test**. Friction you route around
with a built-in is a defect you never see: the fallback is silent, it feels faster in the moment, and
it is exactly the failure mode measured in competing servers (agents that cannot find or trust a tool
fall back to the shell and spend *more* tokens than with no MCP at all).

Dropping to a built-in or to `Bash` is allowed only when:

1. The `terse` server is not connected in this session, or errored after a real attempt on the actual
   target — a rejected glob means fix the glob, not abandon the server.
2. The task is verifying a **just-built** binary whose behaviour differs from the running server (the
   connected `terse` is whatever was installed, not `HEAD` — say which binary answered).
3. Neither the server nor any tool exposes the action: `git` plumbing, `dotnet pack`, `dotnet restore`,
   `dotnet tool install`, running the server by hand over stdio.

Say which of the three applies **at the call**, in one clause. A silent drop is the breach, and the
same drop is a candidate for the improvement backlog below.

## Commands

```bash
dotnet build TerseSharp.slnx
dotnet test  TerseSharp.slnx                      # unit + E2E

# one project / one test
dotnet test tests/TerseSharp.UnitTests/TerseSharp.UnitTests.csproj
dotnet test tests/TerseSharp.E2ETests/TerseSharp.E2ETests.csproj --filter "FullyQualifiedName~NavigationToolsE2ETests"

# required before a PR (CI runs both on ubuntu; from an agent, use cleanup verify=true fix=all)
dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info
dotnet format style     TerseSharp.slnx --verify-no-changes --severity info

# run the server by hand / package it
dotnet run --project src/TerseSharp.Server -- serve --workspace fixtures/FixtureSolution/FixtureSolution.slnx
dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg
```

**Those shell forms are for humans and CI.** From an agent they are `build`, `run_tests`,
`rerun_failed`, `list_tests`, `format verify=true`, `cleanup verify=true fix=all` and `clean` — the
`dotnet` CLI is a fallback under the gate above, not a shortcut, and `cd … && dotnet …` was the single
most common breach in this repo's own session log.

.NET 10 SDK required (`global.json` pins `10.0.300`). Central package management: add versions to
`Directory.Packages.props`, never inline in a `.csproj`.

**E2E tests need `TerseSharp.Server` built first, in the same configuration as the test binaries** —
`TerseServerFixture` launches `src/TerseSharp.Server/bin/<Configuration>/net10.0/terse.dll` as a real
child process over stdio and throws `build TerseSharp.Server first` if it is missing.

## 🚫 HARD GATE — the `terse` that answers you is the installed tool, never your working tree

The MCP server in this session is whatever `dotnet tool install`/`update` last put on PATH. It is
**not** `HEAD`, not your branch, and it does not pick up a `build` you just ran. Sessions have been
spent arguing with this:

- "`search_text` throws on every call" — that was the 0.3.1 global tool; `main` had six passing E2E
  tests for it. No defect existed.
- "`find_usages` still prints absolute paths, F3 is open" — fixed in 0.5.0; the observation came from
  the 0.3.1 binary.
- A whole research document was written against behaviour three releases old, then corrected twice.

So: **no claim about tool behaviour is made from the connected server.** A statement of the form "tool
X does/does not Y" is proven by, in order of preference: (a) an E2E test against the freshly built
`terse.dll`, (b) a hand-run `dotnet run --project src/TerseSharp.Server -- serve …` child over stdio,
(c) current source read with `get_symbol_source`. **Say which one answered, and say the version** —
`workspace_status` and `doctor` both print it. "I called the tool and it did X" is evidence about the
*installed* version only, and it is worthless the moment you have edited that code.

The same asymmetry bites at release time: the running server holds file locks on `terse.dll`, so
`dotnet tool update` reports a success it cannot deliver until Claude Code restarts, and nuget.org's
registration endpoint lags the flat container by about a minute so the first update can no-op on a
cached index. Report that plainly instead of claiming the local install is current.

## 🚫 HARD GATE — a green build and a green suite are not a green CI

CI runs `dotnet format analyzers` **and** `dotnet format style`, both `--verify-no-changes --severity
info`, **on the ubuntu leg only** (`.github/workflows/ci.yml`). Two pushes died there — `IDE0022` on a
block-bodied one-statement test, `IDE0060` on an unused E2E parameter — while `build`, `run_tests`,
`analyze` and every other local gate were green on all three OSes. An info-severity IDE rule is
invisible to a build and fatal to that step.

Before every push, in this order, reading each result before trusting the next:

1. **`build` — and read it.** A failed build followed by a test run reports the *previous* binary's
   result; "167 passed" against a red build has been reported here more than once. Never `--no-build`
   locally — CI's Test step may use it because a Build step ran immediately before, in the same job.
   A lingering `testhost` or `terse` process holds the E2E binary and produces the same false green:
   kill it, rebuild, re-run.
2. **`run_tests` over the whole solution** — unit and E2E.
3. **`cleanup verify=true fix=all`** — the closest in-server reading of that step. It is a **superset**,
   not an equivalent: measured at `b3c381e` it named four files (`ReleaseVersion.cs`,
   `ResponseBuilderTests.cs`, `UnifiedDiffTests.cs`, `WorkspaceRegistryTests.cs`) that both CI commands
   accept, and `format verify=true` is Roslyn's whitespace formatter, which CI does not run at all. So a
   `VERIFY_FAILED` naming a file you did not touch is a prompt to look, not proof CI is red — the two
   `dotnet format … --verify-no-changes --severity info` commands are the arbiter, and running them
   scoped to the project you touched is the one legitimate `dotnet` shell-out on this path. Logged as
   `I37`.

A one-runner red is not automatically a flake, and "it passed on rerun" is not a diagnosis. Real
one-legged failures have shipped here: a macOS-only race introduced by starting the transport before
assigning the preload task, and a Windows-only `TimeoutException: Initialization timed out` when a cold
two-core runner misses the **fixed 60 s MCP handshake ceiling** that `MCP_TIMEOUT` does not raise. Name
which it is on evidence; if it is a timing budget, widen the budget in the test rather than re-running
until it passes.

## Architecture

Two projects, one rule between them: **`TerseSharp.Core` holds all logic, `TerseSharp.Server` holds
only MCP plumbing.**

The tool surface is **83 tools**. `src/TerseSharp.Core` — Roslyn services, each a static class returning `Result<string>` or a
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
3. **SKILL.md** — `src/TerseSharp.Server/Assets/SKILL.md`, an **embedded resource shipped by
   `terse install --skill` and loaded straight into an agent's context**. It must name **every** tool
   in the surface-by-job list, and its swap table, working rules and hard gate must describe the tools
   as they behave *now*. A new tool the skill does not mention might as well not exist — the agent
   will never call it. A changed response format the skill still describes the old way is **worse than
   no skill**, because the agent acts on the wrong contract. When a tool is added, renamed, removed, or
   changes a parameter, a default or its output: update the skill in the same commit, and re-read the
   whole file to check nothing else it claims has quietly become false.
4. **CHANGELOG** — under `## [Unreleased]`, with the format change spelled out.

A commit that changes behaviour and leaves any of the four stale is incomplete. "I'll update the docs
after" is the same failure as "I'll add the test after": both are how an 83-tool surface drifts away
from what it claims to be. When you cannot update one of them in the same commit, say which and why in
the commit body.

## Adding or changing a tool

Before step 1, check `IMPROVEMENTS.md` and the ranking rule in the continuous-improvement gate below:
**improving an existing tool or its response format beats adding a tool**, and the new tool has to
beat the one it splits, not merely be useful.

1. Logic in `TerseSharp.Core`, returning `Result<string>`; the `Tools` class only wires it up.
2. `[McpServerTool(Name = "snake_case_name")]` plus a `[Description]` written for an agent — say what
   it returns *and which built-in it replaces*.
3. **Every optional parameter needs a C# default** (`string? workspace = null`). Without one the MCP
   SDK marks it required and the tool fails at call time.
4. Add the tool name to the `Exercised` set in `tests/TerseSharp.E2ETests/ToolCoverageE2ETests.cs` —
   two tests fail if the advertised list and that set diverge in either direction.
5. One E2E test that asserts response **values** (never "did not throw") against
   `fixtures/FixtureSolution`; `fixtures/BrokenSolution` exists for load-failure and diagnostics
   paths, `fixtures/WarningSolution` for a build that succeeds with warnings, `fixtures/RazorSolution`
   and `fixtures/GeneratorSolution` for Razor and analyzer/generator paths. Fixtures are intentionally
   outside `TerseSharp.slnx`.
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

## 🚫 HARD GATE — continuous improvement: every task ends with a tool-usage review

The prime directive is not satisfied by the tools that exist; it is satisfied by the tools that *keep
getting cheaper*. So before declaring **any** task in this repo done — feature, bug fix, docs,
release chore — review the tool calls **this task itself made** and answer all five in writing:

1. **Round trips.** Which answer cost ≥2 calls that one call could have returned? Name the sequence
   (`get_file_outline` → `get_symbol_source` → `find_usages` is a composite waiting to exist).
2. **Payload.** Which response carried tokens you never used — redundant symbol ids, absolute paths,
   echoed source, columns you did not read, a truncated list you had to re-query wider?
3. **Fallbacks.** Where did you reach for `Read`/`Grep`/`Glob`/`Edit`/`Bash`, and *which* missing,
   failing, undiscoverable or untrusted tool caused it? **Every fallback is a product defect** —
   log it as one, even when the built-in worked fine.
4. **Failures.** Which call errored, returned `ERROR` without a remedy you could act on, needed a
   retry with different arguments, or answered something it could not prove?
5. **Unanswerable.** Which question about the code did no tool answer that Roslyn *could* have —
   DI registrations, generated code, call hierarchy, XAML↔C# bridging, metadata symbols?

The review is **measured, not impressionistic**: count the calls, and state the response size you are
objecting to. "It felt verbose" is not a finding; "the id is 62% of every outline line, ~700 tokens
per 10-member outline" is.

Every finding becomes one line in `IMPROVEMENTS.md` — observed cost, the tool, the proposed change,
the expected saving. Then either fix it in the same task when it is cheap and in scope, or leave it
logged with a reason. Silently dropping it is the one outcome that is not allowed. Ranking rules:

- **Improving an existing tool or its response format beats adding a tool.** The surface already
  costs every session in tool-list tokens and in selection accuracy; a 57th tool must beat the one it
  splits, not merely be useful.
- **A saving that is not measured is not a saving.** Any accepted improvement lands with an assertion
  in `TokenBudgetE2ETests` against the *widest* fixture case, so the next format change cannot quietly
  give it back.
- **Fixing a fallback outranks a new capability.** An agent that falls back to `Grep` spends more
  tokens than one with no MCP at all — that is the measured failure mode of competing servers, and it
  is the only failure that scales with every session.
- A shipped improvement carries the docs gate with it: README, NUGET_README, `SKILL.md`, CHANGELOG.
  A tool the skill does not teach saves nobody anything.

An empty review is legitimate **only** when it names what was checked and why each of the five came
back clean. Banned rationalizations: "the task worked, so the tools are fine" · "that fallback was
just this once" · "the agent should have known which tool to call" (interface design beats
instructions — if the agent guessed wrong, the schema or the description is the defect) · "too small
to log" · "I'll note it next time".

## 🚫 HARD GATE — the file system is async, everywhere

Every file-system and process call on the request path is **asynchronous**. Before writing
`File.ReadAllText`, `File.WriteAllText`, `File.ReadLines`, `File.ReadAllBytes`, `new StreamReader(...)
.ReadToEnd()`, `XDocument.Load(path)` or a `FileStream` without `useAsync`, stop: the async overload
exists, use it, and `await … .ConfigureAwait(false)`. `AtomicWrite` exposes only `TextAsync`; there is
no synchronous writer. A `FileStream` opened for scanning takes
`FileOptions.Asynchronous | FileOptions.SequentialScan`.

The same rule holds for anything else with an async API: `Process` draining, Roslyn's
`GetTextAsync`/`GetSemanticModelAsync`/`GetSyntaxRootAsync`, `SemaphoreSlim.WaitAsync`. Sync-over-async
(`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) is banned outright — it deadlocks under a
synchronization context and burns a thread-pool thread per call, and this server serves one stdio
client that must answer the MCP handshake inside 60 s.

**Two exceptions, both narrow, both stated at the call:** a one-shot bootstrap read outside the
request path (`LoadedWorkspace.DetectLineEnding` reads the solution file once, lazily, to learn the
repo's dominant line ending), and a cheap metadata probe with no async overload (`File.Exists`,
`Directory.CreateDirectory`, `FileInfo.Length` from an enumeration, `AtomicWrite`'s three-byte
byte-order-mark sniff). Anything that reads or writes *content* on the request path is async.

Converting a leaf to async ripples up the call chain: propagate it, do not stop the ripple with a
blocking call.

## 🚫 HARD GATE — the allocation-free path is the only path, unless none exists

**The default is zero allocation. An allocation is a last resort that must be justified at the call.**

STOP before every `new`, every `ToArray`/`ToList`/`ToString`/`Substring`/`Split`/`Join`/`Concat`, every
LINQ chain and every string interpolation, and answer:

> **"Is there an allocation-free way to get this exact result?"**

If yes, **you must take it** — span, `stackalloc`, an existing buffer, a struct, an in-place scan, a
`SearchValues`, a pre-sized collection, an enumerator instead of a materialized sequence. "The
allocating version is shorter" and "it's only one small object" are not reasons; on a per-file,
per-element, per-symbol or per-line path they are the whole cost.

If genuinely no allocation-free solution exists — the value must outlive the frame, be stored, cross an
`await`, or leave the method as a `string` — then allocate **once**, at the outermost boundary, and say
in one clause why it was unavoidable. What is never acceptable is allocating without having looked.

**Allocation-free first, in this order:** slice a span → reuse a caller's buffer → `stackalloc` a
bounded buffer → pool/reuse an instance → pre-size the one collection you must build → allocate.

**STOP before every `Split`, `Substring`, `Trim`, `IndexOf`, `StartsWith`, `EndsWith`, `Replace`,
`Join`, `Concat`, `+` and interpolation you are about to write, and answer one question:**

> **"Does this produce a value that leaves the method, or am I just looking at the text?"**

If you are *looking* — comparing, scanning, slicing, splitting to inspect a part — the operation is
**mandatory `ReadOnlySpan<char>`**. `AsSpan()` first, then slice. A `string` is allocated **only** for a
value that leaves the method: a response line, a dictionary key, a record field, a returned name.
There is no third case and no "it's only a small string" exemption.

This server's work is string work: it walks trees, splits paths, matches names and renders responses,
and it does it once per file, per element, per symbol, per line. An allocation on one of those paths
is an allocation multiplied by the size of the user's solution.

Before writing any of these, stop and use the span form:

| Never | Use |
|---|---|
| `text.Split(…)` to look at parts | `text.AsSpan().Split(…)` / `EnumerateLines()` / manual `IndexOf` walk |
| `string.Join(sep, parts.Select(…))` | write into a `Span<char>` or a pooled `StringBuilder` |
| `text.Substring(a, b)` / `text[a..b]` to inspect | `text.AsSpan(a, b)` — slice, do not copy |
| `left + "=" + right` in a loop | `string.Create` with the total length, or a reused builder |
| `.ToLowerInvariant()` to compare | `Equals(other, StringComparison.OrdinalIgnoreCase)` |
| `text.Contains(other)` with no comparison | `Contains(other, StringComparison.Ordinal)` — vectorized, and says what it means |
| `Path.GetFileName(string)` | `Path.GetFileName(ReadOnlySpan<char>)` |
| `new FileInfo(path).Length` per file | the `FileInfo` the directory enumeration already produced |

**A `string` is only allocated for a value that leaves the method** — a response line, a dictionary key,
a record field. Everything on the way there is a `ReadOnlySpan<char>`.

**Signatures carry spans too.** A helper that only inspects its argument takes `ReadOnlySpan<char>`, not
`string` — `IsCSharp`, `IsGenerated`, `IsMarkdown`, `Same`, `Simple` all do. A helper that returns a
*slice of its own argument* returns `ReadOnlySpan<char>` and lets the caller decide whether to
materialize it. The caller pays for the `new string(...)` at the one place the value is stored.

**Three hard limits, because the compiler enforces them and "always" would not build:**
1. **You cannot return a span over a `stackalloc` buffer** (CS8352) — the buffer dies with the frame.
   A method that builds new characters returns a `string`; only a method that *slices an input* returns
   a span.
2. **A `ref struct` cannot be a field, a generic argument, a record member, or a collection element.**
   Anything stored in `XamlFileRecord`, a `Dictionary`, a `List` or a response is a `string`.
3. **A span cannot cross an `await`.** In an `async` method, slice before the first await or work on a
   `string`/`ReadOnlyMemory<char>`.

So the rule is *"span in, span through, string out at the boundary"* — not "span everywhere".

`stackalloc` is the default buffer for a bounded, small result (a normalized path, a collapsed
signature, a formatted counter). Guard it: `length <= Max ? stackalloc char[Max] : new char[length]`,
never `stackalloc` on an unbounded input. `SearchValues<char>` for a set membership test in a loop,
`MemoryExtensions.IndexOf` over the whole text rather than `Contains` per line, and
`Regex.EnumerateMatches(span)` rather than `Regex.Matches`.

Judgement, not ritual: a one-shot call on the startup path, or a place where the span version is
genuinely less clear for no measured gain, stays simple — **and says so at the call**. What is banned
is the per-file, per-element or per-line allocation nobody measured.

## 🚫 HARD GATE — success costs nothing: minimum response, `verbose=true` restores it

**Every tool answers a success with the absolute minimum, and puts everything else behind
`verbose=true`.** This is not a per-tool judgement call and not a nicety — it is the prime directive
applied to the most common outcome there is. An agent that already knows the edit landed pays for the
diff it will never read, on every call, in every session.

Before shipping or changing **any** tool, answer one question:

> **"In the success case, what would the agent actually act on?"**

Whatever is not in that answer does not go in the response. It goes behind `verbose=true`.

**The contract, on every tool that mutates or verifies:**

1. **Success is one line, or one line per changed file** — never a diff, never file contents, never a
   per-item table. `build`, `run_tests`, `rerun_failed`, `format`, `cleanup` and `clean` are the
   reference shape; every edit, refactor, project/package/solution, `.resx`, XAML and Razor writer
   answers the same way: `<tool> applied  <path>  changedLines=N  errors=0 warnings=0`.
2. **`verbose=true` restores the full report, byte for byte** — the diff, the per-file detail, the
   counters. Every tool with a short form must accept it, and it must be a real C# default
   (`bool verbose = false`), or the MCP SDK marks it required.
3. **`dryRun=true` is never condensed.** There the diff *is* the answer — the whole reason the call
   was made.
4. **The short form is only ever emitted for a result that has nothing else to say.** Any failure, a
   rollback, a timeout, a zero-result run, a locked file, a `NOT rewritten` list, a stale-workspace
   note, **a "0 files changed" that means nothing landed** — all keep the full response. Condensing a
   result that carried a caveat is the same defect as a confident wrong answer, because the agent
   cannot see what was dropped.
   **A warning is not a caveat — it is a payload, and it is opt-in.** A successful build answers in
   one line however many warnings it produced, and a failed one lists **error-severity diagnostics
   only**; the rest is a single `warnings=N hidden` count. A count is what proves `verbose=true` has
   something to show, so it is never dropped — but the lines themselves are returned only when the
   agent asks. The tools whose payload *is* the diagnostics — `analyze`, `get_diagnostics`,
   `xaml_validate`, `razor_validate`, `resx_validate` — are exempt under rule 5: there the warnings
   are the answer, not ceremony around it. `build` carries one carve-out the test tools do **not**:
   a failed build with no error-severity line lists what it does have, because answering a failure
   with nothing is the confident wrong answer this whole section exists to prevent. On `run_tests`,
   `rerun_failed` and `list_tests` the same case falls through to the bounded output tail instead —
   two different rules, so they get two differently-named helpers, never one shared `Shown`.
5. **A read tool is not exempt, but its answer is not a "success report".** `get_file_outline`,
   `find_usages`, `search_symbols` and friends exist to return that payload — it is the answer, not
   ceremony, and it is not condensed away. What *is* banned there is the ceremony around it: echoed
   arguments, absolute paths, redundant ids, columns nobody reads, a header restating the question.

A new mutating tool that returns a diff on success, or that has no `verbose` parameter, is
**incomplete** — the same as one with no E2E test. Assert the short form's size in
`TokenBudgetE2ETests` against the widest fixture case, so the next format change cannot quietly give
it back.

## Code style

`Directory.Build.props` sets `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and
`AnalysisLevel=latest-recommended`, so analyzer warnings fail the build. Immutable records, `sealed`
by default, pattern matching and switch expressions over `if`/`else` ladders, explicit
`IFormatProvider` on every culture-sensitive format (`string.Create(CultureInfo.InvariantCulture, $"…")`,
never bare interpolation as a converter — `System.Globalization` is a global using), and **no
comments**: make the code say it.

Write the newest form the compiler accepts, and modernize the lines you touch: collection expressions
(`[]`, `[.. spread]`) over `new List<T>()` / `Array.Empty` / `Enumerable.Empty`, primary constructors,
the `field` keyword instead of a hand-written backing field, target-typed `new`, `is null` / `is not
null` over `== null`, raw string literals for embedded JSON and XML, file-scoped namespaces,
`required` / `init` over settable properties, `CancelAsync()` over `Cancel()`. The ubuntu format gate
runs at `--severity info`, so `IDE0022` (expression body), `IDE0060` (unused parameter) and their
siblings are **CI-breaking** here, not suggestions — and the build will not tell you, because
`.editorconfig` carries them at `suggestion` and `TreatWarningsAsErrors` escalates warnings, not
suggestions.

## 🚫 HARD GATE — a release is not cut until the review is closed and the changelog links it

**No tag, no `dotnet pack`, no `dotnet nuget push`, no GitHub release, while a code review is open.**
Before any of those four, both must hold:

1. **The code-review agent has finished** — not "was spawned", not "is probably fine": its report
   exists and has been read. A review still running is a blocker; wait for it.
2. **Every CRITICAL and WARNING it found is fixed**, the gates and the affected suites were re-run
   green *after* those fixes, and the fix round was itself re-reviewed. A finding may only stay open if
   it is written down — in the report and in the release notes — as a deliberate, justified decision;
   "I disagree" is not a justification, and neither is "it is a NIT to me".

Cutting a release with an unread or unaddressed review is the one failure this project cannot walk
back: the package is public the moment it is pushed, `nuget delete` only unlists it, and a shipped
wrong answer costs every agent that installs it. If the release is urgent, the correct move is to
narrow the change set, not to skip the review.

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

## 🚫 HARD GATE — a rule with no census gate is a suggestion

Every "every X does Y" rule in this file is enforced by a test that **discovers all X from the running
server** (`tools/list`) or from source, and fails on any non-conforming instance.
`ToolCoverageE2ETests` is the model: it asserts the advertised list and the enrolled set match **in
both directions**, so neither a new tool nor a deleted one can slip past it.

A gate that checks only what somebody remembered to enrol is forbidden. It silently exempts everything
added later, which is exactly how the tool count, the NUGET_README and `SKILL.md` each went stale while
every local gate stayed green — a docs hard gate written in prose is obeyed until the session that is
in a hurry. Where an exception is genuinely needed, put it in a checked-in exclusion set with a written
reason per entry, and treat that set as a **ratchet: it may only shrink**.

Add a rule ⇒ add its census gate in the same change. **Current census gates — this list is exhaustive,
and being absent from it is the point:**

| Rule | Gate | Discovers its subject from |
|---|---|---|
| every tool has an E2E test | `ToolCoverageE2ETests` | `tools/list`, both directions |
| every tool is named in `SKILL.md`, `README.md`, `NUGET_README.md` | `DocsCoverageE2ETests` | `tools/list` |
| every tool answers garbage, empty and missing arguments with a `remedy:` | `ToolRobustnessE2ETests` | `tools/list`, minus the `ProcessSpawning` / `WorkspaceMutating` / `Destructive` arrays — an exclusion set that carries no written reason per entry and no ratchet, so it does not yet meet the rule above it |
| **every mutating tool takes `verbose`** | **none — 7 hand-written spot checks** | — |
| **every listing tool has a token budget** | **none — `TokenBudgetE2ETests` is 20 per-tool `[Fact]`s, and its `EveryReadToolStaysWithinTheGlobalCap` names four tools by hand** | — |
| **no build/test tool returns a warning unless `verbose=true`** (rule 4 above) | **none — 8 hand-written `DotnetRunnerTests` render cases plus `BuildWarningsE2ETests`; nothing discovers the family from `tools/list`** | — |

The last three rows are the ones to close first: they are stated in this file as if enforced, and they
are not. Closing the third needs a warning-emitting **test** project in `fixtures/WarningSolution` —
today it has none, so `run_tests` and `list_tests` are covered only at the render-function level.

**Never delete, skip, `[Fact(Skip=…)]` or weaken a test — or its assertions — to make a suite go
green.** A red test is resolved by fixing the code, by fixing an expectation that was itself wrong, or
by making the test deterministic. A genuinely obsolete test is *replaced* in the same change by one
that covers the same behaviour at least as strongly.

## Traps that cost time — session-hardened

Each burned real tokens in a past session in this repo. They are the fast path, not style.

- **The compile gate rolls back a callee-after-caller edit.** 35 `CompileRegression` rejections were a
  `replace_symbol` / `add_member` whose new body called a helper that did not exist yet (`FileGlob`,
  `Separated`, `MinSharedPrefix`). Add the callee **first**, bottom-up — a rejected edit costs the call
  *and* the whole declaration you sent.
- **Never hand-write a documentation id.** 15 `SymbolNotFound`s were ids typed from memory, missing a
  parameter list or a `~ReturnType` suffix. Copy it from `get_file_outline ids=full` /
  `search_symbols`, or use the short name form (`OrderService.Submit`) — that is what it exists for.
- **`replace_symbol` takes exactly one member.** Two members answers `the declaration is not exactly
  one member`; call it once per member.
- **On a markdown file, address the section — do not recall the text.** 102 `edit_text`
  `InvalidArgument`s were `oldText matched 0 times` (text remembered from an earlier read, or a file
  that moved under you) or `matched 48 times` (anchor too short). `read_text headings=true` gives the
  map; `section="## Commands"` replaces a whole section with no `oldText` at all.
- **Bulk-editing C# with `python` or `sed` through `Bash` is the recurring breach.** It happened three
  times in one release and was self-logged each time. A repetitive change across N members is N
  `replace_symbol_body` calls, or one `write_text force=true` from a *fresh* read — both go through
  `EditGate`; the shell rewrite does not, and it is the precise fallback this repo exists to remove.
- **More than one workspace is usually loadable here, so pass `workspace:` on the first call.**
  `.claude/worktrees/agent-*` holds whole copies of this tree, and a task that loads
  `fixtures/FixtureSolution` alongside the solution makes every un-hinted call ambiguous.
  `AmbiguousWorkspace` was the second most frequent error code in the session logs; the resolver was
  taught to rank hints in **I5/I13**, so a worktree name resolves today — but naming it up front still
  beats one error plus one retry.
- **This tree is shared with other sessions and with agent worktrees.** `git add -A` swept an untracked
  working note into a release commit, and one commit did not contain the edit claimed for it because a
  parallel session's work landed in between. Stage by path, then `git show --stat HEAD` before saying
  what shipped.
- **A test the fixture cannot fail is not coverage.** Dialect detection matched strings that occur in
  no real markup, so every file reported `dialect=wpf` and no test could fail — there was no non-WPF
  fixture. Overload selection was untested because the fixture had no overloads. A `find_usages` format
  change tripled a 46-usage response and passed the 4-usage budget assertion unchanged. Put the case in
  the fixture, observe the test fail, then make it pass.
- **A reviewer's snapshot goes stale mid-review.** Fixes applied while a review runs produce findings
  against code that no longer exists — and one such round still caught a real regression introduced *by*
  the fix round. Re-verify each finding against the current tree; never dismiss a whole report because
  part of it is stale.
- **Changing a guard means changing the tests that assert the old answer.** A push failed on all three
  runners because a test still asserted `dotnet build` was *allowed* against a guard just taught to deny
  it, while E2E was 330/330 green locally on the stale expectation.

## Definition of done

- [ ] `build` clean — **read before** any test result; `run_tests` green over the whole solution.
- [ ] `analyze` down to `info` on every touched file → `format` / `cleanup` → re-`analyze`;
      `get_diagnostics` for the solution-wide sweep.
- [ ] `cleanup verify=true fix=all` **and** `format verify=true` — the ubuntu-only CI step.
- [ ] New behaviour has an E2E test asserting **values** against `fixtures/FixtureSolution`, observed
      failing first; a new tool is in `ToolCoverageE2ETests.Exercised`; a listing tool has a
      `TokenBudgetE2ETests` assertion against the **widest** fixture case.
- [ ] Docs gate, same commit: `README.md`, `NUGET_README.md`,
      `src/TerseSharp.Server/Assets/SKILL.md`, `CHANGELOG.md`.
- [ ] Tool-usage review written — all five questions, measured; findings in `IMPROVEMENTS.md`.
- [ ] `code-review-gate` run; every CRITICAL and WARNING fixed, or left open in writing with a reason.
- [ ] `git status --porcelain` shows nothing this task did not produce; commit by path, never `-A`.

## Versioning

Versions come from git tags via MinVer (`v` prefix); no version is stored in any file. Releases are
cut by tagging — see `RELEASING.md`. CI must fetch full history or MinVer produces a wrong version.
