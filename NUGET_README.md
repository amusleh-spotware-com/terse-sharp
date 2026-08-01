# TerseSharp

### The bridge between your coding agent and your C# codebase.

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server that lets an agent navigate, read,
edit, refactor, build and test a .NET solution **semantically** — no `Read`, no `Grep`, no
line-number `Edit`, no shelling out. **83 tools. One install. No IDE, no licence, no network.**

**Fewer tokens → lower bill. Fewer round trips → less waiting. Exact answers → fewer wrong edits.**
Your agent spends the context window **doing the work** instead of **finding the code**.

[![CI](https://img.shields.io/github/actions/workflow/status/amusleh-spotware-com/terse-sharp/ci.yml?branch=main&label=CI)](https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

---

## 💸 What it saves you

| Question | Built-in tools | TerseSharp | |
| --- | --- | --- | --- |
| What's on this 2,000-line type? | `Read` → **~6,000 tokens** | `get_type_outline` → **~450** | **13×** |
| Who calls this method? | `Grep` + follow-up reads → **~4,000** | `find_usages` → **~200** | **20×** |
| Rename it across the solution | ~5,000 tokens, **misses the interface** | `rename_symbol` → **~150**, correct | **30×** |
| Why is the build red? | **~8,000 tokens** of MSBuild spew | `build` → **~600** | **13×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

Asserted by a token-budget suite in CI on every commit, not estimated.

- 💰 **Money.** Ten type reads cost ~60,000 input tokens with `Read`; ~4,500 here — billed every
  session, on every repo, for every agent you run.
- ⏱️ **Time.** No IDE to launch, no language server handshake. The workspace loads **once**; every
  later question is answered from the same in-memory compilation, and repeat XAML / `.resx` / DI
  questions are served from a per-generation index that **reads no file at all**.
- 🎯 **Fewer wrong edits.** A grep-driven rename misses the interface and hits the comment. An edit
  that introduces a compile error is **rolled back** before the agent reports it done.

**Round trips, not just bytes.** `explore_symbol` folds signature + docs + usage counts +
implementations + XAML sites into one call; `impact_of` answers *"what breaks if I change this"* —
every referencing file and every project that recompiles — **before** the rename, not after the build
goes red.

---

## 🎨 XAML and 🧩 Razor, checked against your C#

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers what no
text tool can. **WPF · Avalonia (`.axaml`) · WinUI · MAUI**, dialect detected from the namespace.

**Does this binding actually bind?** WPF has *no* compile-time binding check — a typo fails silently to
debug output. `xaml_bindings validate=true` resolves the data context from `x:DataType` or
`d:DataContext`, maps the prefix through its `clr-namespace:`, and walks every path segment against the
real symbol:

```
BoundView.xaml:7   EXACT  TextBlock.Text  {Binding Symbol}  OK Symbol on OrderViewModel
BoundView.xaml:9   EXACT  TextBlock.Text  {Binding Symbl}   ERROR no member 'Symbl'; nearest 'Symbol'
```

**Where does this resource come from?** `xaml_resolve AccentBrush` reports every declaration of the key
with its `scope=local|theme`, instead of reading `App.xaml` and each merged dictionary in order.

**The Blazor bug nothing else catches.** An attribute matching no `[Parameter]` compiles clean and
throws `InvalidOperationException` at render. `razor_validate` reports it — with unknown components,
duplicate `@page` routes, a `@bind` with no setter and an unregistered `@inject` — at the `.razor` line,
never at the generated file under `obj/`. `razor_component` prints a component's full `[Parameter]` list,
including one from a referenced package.

**Renames carry into markup.** `rename_symbol` rewrites `Click="…"` and `{Binding …}`, but only where an
`x:Class` or `x:DataType` proves it — anything else is listed `NOT rewritten` rather than guessed — and
renaming a Blazor component renames its file plus the `.razor.cs`/`.razor.css`/`.razor.js` siblings.

---

## Install

No IDE, no licence, no Node, no Python, no language server, no API key, no network.

```
dotnet tool install -g TerseSharp
```

Register it with your agent — TerseSharp writes the config itself, you don't hand-edit JSON:

```
terse install                       # detect installed clients and register with all of them
terse install --client claude-code  # or pick one: claude-code | cursor | vscode | windsurf
terse install --skill               # also install the agent skill
terse install --guard               # also install the hook that BLOCKS Read/Grep/Edit on C#, XAML and .resx
terse doctor                        # verify SDK, MSBuild, workspace load, client registration
```

With no arguments the server walks up from the current directory, finds your `.sln` / `.slnx` /
`.slnf` / `.csproj`, and loads it. Claude Code reads `~/.claude.json`, or
`$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set — `terse install` and `terse doctor`
follow it, and `doctor` prints the config path it read.

Prefer to configure it by hand:

```json
{
  "mcpServers": {
    "terse-sharp": {
      "command": "terse",
      "args": ["serve", "--workspace", "C:/path/to/YourApp.slnx"]
    }
  }
}
```

**🔒 Make it stick.** The most expensive failure mode is an agent that has TerseSharp installed and
reaches for `Read`/`Grep` anyway — every token the server saves on a call the agent never makes is
zero. `terse install --guard` registers `terse guard` as a Claude Code `PreToolUse` hook that
**denies** the built-in and names the tool to use instead.

| | |
| --- | --- |
| **Denied** | `Read`/`Write`/`Edit`/`MultiEdit` on `.cs`, `.razor`, `.cshtml`, `.razor.css`, `.razor.js`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`, `.xaml`, `.axaml`, `.resx`, `.resw` · `Glob`/`Grep` scoped to them · a shell text read or listing on them (`grep`, `rg`, `cat`, `head`, `tail`, `sed`, `awk`, `findstr`, `type`, `find`, `fd`, `ls`, `dir`, `tree`, `wc`, `nl`, plus the PowerShell forms `Get-ChildItem`, `gci`, `Get-Content`, `gc`, `Select-String`, `sls`) · `dotnet build`, `dotnet test`, `msbuild`, `vstest`, `dotnet format`, `dotnet clean` — anywhere in a compound command. A denial names the matching tool family: `resx_*` for a resource file, `razor_*` for Razor markup, `xaml_find`/`xaml_resolve`/`xaml_styles` before `find_files` for XAML |
| **Names a tool that can do it** | `Write`/`Edit` on a `.cs` path that does not exist yet names `write_text(path, content, force=true)` — no symbol tool creates a file, and a denial with no legal move is what produces a silent fallback |
| **Says freshness is handled** | every `.cs` write denial adds that a file created or edited through `write_text` is picked up automatically, with no reload |
| **Allowed** | plain `.css`, `.js`, `.csv`, `.csx` — matching is by file **extension** plus the `.razor.css`/`.razor.js` pair, not substring · `dotnet restore`, `pack`, `publish`, `run`, `tool` — no TerseSharp tool replaces these, and a denial that names no alternative is a wall |
| **Never blocks on failure** | malformed hook input allows the call, so a guard fault cannot wedge a session |

Pair it with `--skill`: the skill teaches the swaps, the guard enforces them.

**🎮 Unity:** works on Unity game code too — Unity generates a real `.sln` with
`Assembly-CSharp.csproj`, so outlines, `find_usages`, symbol-addressed edits and compile-gated rename
across your `MonoBehaviour`s all work. Open the project in the editor once so the project files exist.
Scene graph, inspector values and play-mode state are out of scope: TerseSharp answers questions about
your **C# code**, not the editor.

**✂️ Success costs nothing.** All 30 mutating tools — `replace_symbol*`, `add_member`,
`delete_symbol`, `rename_symbol`, the refactors, `write_text`, `edit_text`, `xaml_*`, `razor_*`,
`resx_*`, `project_*`, `package_*`, `solution_*` — answer a successful edit in **one line per changed
file** (workspace-relative `path  changedLines=N`, plus the compile gate's `errors=`/`warnings=`
counters), not a diff of text the agent just wrote. `verbose=true` restores it; `dryRun=true` is never
condensed, because there the diff *is* the answer; and **every caveat prints in full regardless** — a
rollback, a new compile error, `0 files changed`, `compileGate=unavailable`, `workspace=stale`,
`UNFIXED`, `designerStale`, or the `NOT rewritten` list a XAML-aware rename leaves.

**🔔 Staying current.** A new release is announced to your agent for the cost of **one `HEAD` request
to GitHub's `releases/latest` — empty body, no token, no rate limit — at most once every 24 hours**,
on a background task that never blocks the handshake or a tool call, cached in `~/.terse/update`. When
a newer release exists the **next tool response carries one extra last line**:

```
UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp
```

It appears once per server process and never repeats. After you update, the next `terse serve`
rewrites the installed `SKILL.md` and re-applies the `terse guard` hook so both match the new binary —
only for what you installed. `TERSE_UPDATE=0` turns the check and the refresh off; `TERSE_UPDATE_URL`
points them elsewhere.

## What each tool replaces

| Instead of | Use | Why |
| --- | --- | --- |
| `Read` a `.cs` file | `get_file_outline` | types + members + line ranges, no bodies; `usings=true` adds the using directives |
| `Read` to see one method | `get_symbol_source` | that member only |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` | real references, each marked `src` or `test` |
| three calls to learn a symbol | `explore_symbol` | signature, docs, usage counts, implementations, XAML sites — one call |
| guessing a rename's blast radius | `impact_of` | every referencing file and every project that recompiles |
| grepping `Program.cs` for DI | `find_registrations` · `list_endpoints` | open generics, factory delegates and `Add*` extensions grep cannot see |
| `Edit` a `.cs` file | `replace_symbol_body` · `add_member` | addressed by symbol, immune to line drift; several declarations land as one compile-gated edit |
| creating a new `.cs` file | `write_text(path, content, force: true)` | the new type resolves on the very next call |
| `Edit` a `.xaml` file | `xaml_set_property` | addressed by element, formatting preserved |
| `Read` a `.razor` / `.cshtml` file | `razor_outline` | directives, component tree and `@code` members, each component resolved to its type |
| `Edit` a `.razor` file | `razor_set_attribute` | element-addressed, and the Razor generator re-runs so a broken edit is rolled back |
| `Read` a `.resx` file | `resx_get` | keys and values per culture; a missing translation prints `MISSING` |
| `Grep` a resource key | `resx_find` · `resx_usages` | across every family, or every C#/XAML/Razor site that names it |
| `Edit` a `.resx` file | `resx_set` · `resx_remove` · `resx_rename` | schema header, ordering, indentation, line endings and BOM preserved |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew; a clean build is one line |
| `dotnet test` | `run_tests` | a green run is one line; a failure carries its message, expected/actual and one source frame |
| `dotnet format` | `format`, `cleanup fix=all`, `cleanup verify=true` | compile-gated code fixes and a one-line verdict, never raw CLI output |
| `dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's file locks |

## The 83 tools

Every response is one record per line, with an explicit `truncated`/`total` and an `EXACT`
(Roslyn-resolved) or `HEURISTIC` (text/index) tag. Paths are workspace-relative, and truncation names
the parameter that narrows it.

- **Workspace** — `load_workspace`, `workspace_status`, `list_workspaces`, `unload_workspace`, `list_projects`
- **Navigation** — `search_symbols`, `get_symbol`, `get_file_outline`, `get_type_outline`, `get_symbol_source`, `find_usages`, `find_implementations`, `explore_symbol`, `impact_of`
- **.NET semantics grep cannot reach** — `find_registrations` (DI: open generics, factories, `Add*` extensions), `list_endpoints` (ASP.NET Core `Map*`)
- **Analyze & clean** — `analyze`, `format`, `cleanup`, `clean`, `get_diagnostics`
- **Edit** — `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`
- **Refactor** — `extract_interface`, `move_type_to_file`, `move_type_to_namespace`, `change_signature`, `undo_last_change`
- **Projects & solutions** — `solution_projects`, `solution_add_project`, `solution_remove_project`, `project_create`, `project_properties`, `project_set_property`, `project_add_reference`, `project_remove_reference`, `package_list`, `package_add`, `package_remove`
- **XAML (WPF · Avalonia · WinUI · MAUI)** — `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_resolve`, `xaml_styles`, `xaml_bindings`, `xaml_validate`, `xaml_find`, `xaml_codebehind`, `xaml_localization`, `xaml_set_property`, `xaml_add_element`, `xaml_remove_element`
- **Localization (`.resx`/`.resw`)** — `resx_files`, `resx_get`, `resx_find`, `resx_usages`, `resx_set`, `resx_remove`, `resx_rename`, `resx_validate`
- **Razor / Blazor** — `razor_outline`, `razor_component`, `razor_find`, `razor_bindings`, `razor_codebehind`, `razor_validate`, `razor_set_attribute`, `razor_add_element`, `razor_remove_element`, `razor_set_directive`
- **Files** — `read_text`, `write_text`, `edit_text`, `find_files`, `search_text`, `search_regex`
  (the search tools take `query` and anchor `^`/`$` per line; `bin`, `obj`, `.git`, `.claude`,
  `artifacts`, `TestResults`, `node_modules` and directory symlinks are skipped; `read_text` also
  reads an absolute path outside the workspace, tagged `outside-workspace`)
- **Build & test** — `build`, `run_tests`, `rerun_failed`, `list_tests`

## ⚔️ Vs the alternatives

| | **TerseSharp** | Rider MCP | `RoslynMcpServer` | `csharp-lsp-mcp` |
| --- | --- | --- | --- | --- |
| Needs a running IDE | **No** | Yes (licensed, solution open) | No | No |
| Setup | **one command** | IDE + licence | tool install | tool install + `csharp-ls` |
| C# semantics | **Roslyn, exact** | Roslyn, exact | Roslyn, exact | via `csharp-ls` |
| Can edit / refactor | **Yes** | Yes | Partial | Rename preview |
| Compile-gated edits with rollback | **Yes** | No | No | No |
| Undo the last symbol edit as a tool | **`undo_last_change`** | No | No | No |
| Response size budgeted in CI | **The savings above** | No | No | No |
| One-line success, `verbose=true` for the diff | **Yes** | No | No | No |
| Symbol addressable by short name | **Yes, round-trips** | Ids only | Ids only | Positions |
| Confidence tag on every semantic result | **`EXACT` / `HEURISTIC`** | No | No | No |
| Truncation that names the narrowing parameter | **Yes** | No | No | No |
| Type-checked XAML bindings | **Yes** | Inspections only | No | No |
| XAML resource graph (merged dictionaries, themes) | **Yes** | No | No | No |
| XAML-aware rename | **Yes** | Partial | No | No |
| Razor / Blazor component API + validation | **Yes** | Inspections only | No | No |
| Edits inside `@code` via the C# tools | **Yes** | Partial | No | No |
| `.resx` / `.resw` read, edit and translation lint | **Yes** | No | No | No |
| DI registrations & ASP.NET endpoints as tools | **Yes** | No | No | No |
| Analyzers + dead code, no licence | **Down to `info`** | Yes (licensed) | No | No |
| `build` / `run_tests` / `rerun_failed` as tools | **Yes** | Yes | No | No |
| Project / package / solution editing | **Yes** | Partial | No | No |
| Live disk sync (watcher + stamp check) | **Yes** | IDE-managed | No | No |
| Parallel worktrees / multi-repo | **First-class** | One solution per IDE | No | No |
| `--read-only` mode | **Yes** | No | No | No |
| Ships an agent skill + a `PreToolUse` guard hook | **Yes** | No | No | No |
| E2E test per advertised tool | **Required** | — | — | — |

Compared against public documentation and tool lists at time of writing; corrections welcome by PR.

## Analysis without a licence

`analyze` runs the compiler plus every analyzer your projects already reference — CA rules, StyleCop,
SonarAnalyzer, Roslynator, anything in your `PackageReference` list — down to `info` and `hidden`
severity, which a normal build hides, and reports dead code in the same list, so one call covers
everything. `cleanup` removes unused `using` directives, sorts what remains System-first and reformats
to your `.editorconfig`; `cleanup fix=style|analyzers|all` also applies the code fixes of every
analyzer the project references — the in-process equivalent of `dotnet format style` and
`dotnet format analyzers` — compile-gated, rolled back if it breaks the build, and reporting
`UNFIXED <id>` for anything no fixer covers. `format verify=true` and `cleanup verify=true` replace
`--verify-no-changes` with a one-line verdict, `path=` takes a file, a directory or a glob, and
generated code is never rewritten. `clean` replaces `dotnet clean`: it deletes `bin` **and** `obj` and
reports `projects=`, `files=` and `freedBytes=` instead of MSBuild output, releasing the workspace's
own file locks first when they block the delete. All Roslyn: no IDE, no external tool, no licence, no
network.

## Freshness — the workspace follows the disk

A loaded workspace used to be a snapshot: a file created with `write_text`, an edit from your IDE or a
`git checkout` never reached the Roslyn solution, so the next `replace_symbol` answered from stale
state **with an `EXACT` tag**. It now tracks the tree.

- **A `FileSystemWatcher` per workspace** nominates changed paths. It is a hint, not a source of
  truth: state changes only after a **content comparison**, so a dropped, duplicated or out-of-order
  OS event can delay a refresh but never corrupt one.
- **Sync is lazy** — events accumulate and are drained by the next call that needs semantics, so a
  `git checkout` storm costs one reload, not one per file. `read_text`, `write_text`, `edit_text`,
  `find_files`, `search_text` and `search_regex` answer from disk and skip it.
- **A targeted stamp check** compares one file's `(LastWriteTimeUtc, Length)` before answering about
  it, which is why correctness survives a dropped event and `--no-watch`.
- **Doubt is a rebuild** — a changed `.csproj`/`.props`/`.targets`/`.sln`/`global.json`/`.editorconfig`,
  a `.cs` added or removed under a project's directory, a watcher buffer overflow or an over-cap
  pending set reload the solution rather than guess. A call already in flight keeps answering from the
  snapshot it started with.
- **Four generation counters** — `Code`, `Project`, `Xaml`, `Resx` (plus `rz` for Razor) — so a `.cs`
  edit does not invalidate the XAML graph. They carry across a reload; compare them for inequality
  rather than ordering.
- **Repeat questions read no file at all** — `xaml_resolve`, `xaml_validate`, `xaml_styles`,
  `xaml_localization`, `xaml_find`, the `resx_*` tools, `find_registrations` and `list_endpoints`
  share one index per workspace, built once per generation and reused until that counter moves. When
  it does, only the files whose stamp changed are re-parsed — a one-file edit in a 200-file tree costs
  one parse, not 200.
- **Undo knows it was overtaken** — an external change to a file an undo snapshot covers drops that
  snapshot and every one above it, and `undo_last_change` says so instead of silently reverting
  someone else's work.

`workspace_status` reports `watch=active gen=c12/p1/x3/r0/rz2 pending=0 lastSyncMs=8 gaps=0` and the
index hit rates. `load_workspace(reload: true)` forces a reload; `--no-watch` (or `TERSE_WATCH=0`)
turns the watcher off for constrained containers, and `terse doctor` reports whether this platform
supports file watching at all.

## Safety

- **Symbol-addressed edits** — no `old_string` echo, no line numbers to drift.
- **`dryRun` on every mutation** returns the unified diff and writes nothing.
- **Compile-gated** — a C#, Razor or refactoring edit that introduces a *new* compile error is rolled
  back and the error returned. Pre-existing errors never block an edit; `allowErrors: true` opts out.
  The `.resx`, `.xaml` and `project_*`/`package_*`/`solution_*` writers are file writes: surgical and
  formatting-preserving, but outside the compile gate and outside `undo_last_change` — preview them
  with `dryRun`.
- **Short symbol references** — an outline prints `OrderService.Submit(Order)` rather than a
  200-character documentation id, and every tool that takes a `symbolId` accepts that name back. A
  member a short name cannot address unambiguously keeps its documentation id, so every reference an
  outline prints resolves. An ambiguous name lists the candidates rather than guessing.
- **Workspace containment** — paths compare by whole segment, so root `C:\repo` does not contain
  `C:\repoEvil`.
- **`--read-only`** makes every mutating tool refuse and touch nothing.

## Parallel worktrees

Run several agents at once across several git worktrees of one repo, and across unrelated repos —
one server holding many workspaces (LRU, default 4), or many processes, or both. Every answer names
its worktree and branch, and an ambiguous request returns `ERROR AmbiguousWorkspace` listing the
candidates **instead of guessing** — answering from the wrong checkout is the one failure an agent
cannot detect.

## Links

- [Source, full documentation and issues](https://github.com/amusleh-spotware-com/terse-sharp)
- [Changelog](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CONTRIBUTING.md)
- [Security policy](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/SECURITY.md)

## License

MIT Licensed. See [LICENSE](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE).

Built on [Roslyn](https://github.com/dotnet/roslyn) and the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
