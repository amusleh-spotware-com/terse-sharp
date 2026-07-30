<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>Your agent stops reading whole C# files.</b><br/>
  A Roslyn-powered MCP server that lets a coding agent navigate, edit, refactor, analyze and clean a
  .NET solution <b>semantically</b> — no <code>Read</code>, no <code>Grep</code>, no line-number
  <code>Edit</code>, no shelling out.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="MIT"/>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet&logoColor=white" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/Roslyn-5.6-512BD4.svg" alt="Roslyn"/>
  <img src="https://img.shields.io/badge/MCP-C%23_SDK_2.0-8A2BE2.svg" alt="MCP C# SDK 2.0"/>
  <img src="https://img.shields.io/badge/status-design_complete_·_not_built-orange.svg" alt="Status"/>
  <img src="https://img.shields.io/badge/PRs-welcome-brightgreen.svg" alt="PRs welcome"/>
</p>

<p align="center">
  <a href="#-why">Why</a> ·
  <a href="#-the-numbers">The numbers</a> ·
  <a href="#-install">Install</a> ·
  <a href="#-what-it-does">What it does</a> ·
  <a href="#-vs-the-alternatives">Comparison</a> ·
  <a href="#-how-it-is-fast">How it's fast</a> ·
  <a href="#-status--roadmap">Status</a>
</p>

> [!NOTE]
> **v0.1.0 — 25 tools working end to end; the speed and token numbers below are still targets.**
> What runs today: workspace loading, symbol search, outlines, usages, implementations,
> diagnostics, symbol-addressed edits, solution-wide rename, file and text tools, build and tests —
> all verified by **44 tests (19 unit + 25 E2E)**, where every E2E test drives a real server process
> over the real stdio transport against a real solution and asserts response values.
> **Not yet built:** XAML, ReSharper CLT integration, project/solution/package editing, the
> content-addressed index, debug and profiling. The tables below marked *target* are specified and
> gated in [requirements](sharp-mcp-requirements.md) but **not yet measured** — treat them as the
> contract, not as results.

---

## 🤔 Why

An agent working a C# solution spends most of its context on the wrong shape of data:

```
"What's on OrderService?"        →  Read OrderService.cs          →  ~6,000 tokens
"Who calls Submit?"              →  Grep "Submit" + 3 more Reads  →  ~4,000 tokens
"Rename Submit to SubmitAsync"   →  grep + 9 context-echoing Edits →  ~5,000 tokens, misses the interface
"Fix the build"                  →  dotnet build, full MSBuild spew → ~8,000 tokens
```

Roslyn already knows all four answers **semantically**. TerseSharp hands them over in the shape the
agent actually needs — a signature list instead of a file, 12 real call sites instead of 47 string
matches, a solution-wide rename instead of a regex sweep, deduplicated diagnostics instead of build
logs.

**Prime directive: save tokens, increase speed.** Every design decision in this project is settled by
that rule, and both halves are measured in CI.

## 📊 The numbers

Targets, each with a CI gate. A tool that misses its budget does not ship.

| Question | With built-in tools | With TerseSharp | Target |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → ~6,000 tok | `get_type_outline` → ~450 tok | **13×** |
| Who calls this method? | `Grep` + follow-ups → ~4,000 tok | `find_usages` → ~200 tok | **20×** |
| Rename across the solution | ~5,000 tok, and it misses interface implementations | `rename_symbol` → ~150 tok, correct | **30×** |
| Why is the build red? | ~8,000 tok of MSBuild output | `build` → deduped diagnostics → ~600 tok | **13×** |
| 2 failures out of 312 tests | full test output | `run_tests` → 2 failures + assertion lines | **10×** |
| What's in this 900-line XAML? | `Read` → ~7,000 tok | `xaml_outline` → ~400 tok | **17×** |

**Speed**, p95, on a warm 100-project / ~1 M LOC solution:

| Tool | Budget | vs Rider MCP |
|---|---|---|
| `search_symbols` | ≤ 50 ms | ≤ **25 %** |
| `get_file_outline` | ≤ 30 ms | ≤ **25 %** |
| `get_type_outline` | ≤ 50 ms | ≤ **25 %** |
| `goto_definition` | ≤ 150 ms | ≤ 50 % |
| `find_usages` (100 hits) | ≤ 1.5 s | ≤ 50 % |
| Second cold start (persisted index) | ≤ 5 s | Rider's caches aren't reusable headlessly |
| Second **worktree** of the same commit | ≤ 10 s, ≥ 95 % shards reused | Rider re-indexes per solution |

A comparative harness runs the same query set against Rider MCP and TerseSharp on the same solution
and publishes p50/p95 per tool with every release. Missing the ratio on any tool is a **release
blocker**, not a footnote.

## 🚀 Install

One command. No IDE, no licence, no Node, no Python, no language server.

```bash
# from source, today:
git clone https://github.com/amusleh-spotware-com/terse-sharp && cd terse-sharp
dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg
dotnet tool install -g TerseSharp --add-source artifacts/nupkg

# once published to nuget.org:
dotnet tool install -g TerseSharp
```

### The 25 tools shipping today

| Group | Tools |
|---|---|
| Workspace | `load_workspace` · `list_workspaces` · `unload_workspace` · `list_projects` |
| Navigation | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` |
| Diagnostics | `get_diagnostics` |
| Edit | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| Files | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| Build | `build` · `run_tests` |

Register it with your agent — TerseSharp writes the config itself, you don't hand-edit JSON:

```bash
terse install                      # detects installed clients and registers with all of them
terse install --client claude-code # or pick one: claude-code | vscode | cursor | visualstudio
terse doctor                       # verifies SDK, MSBuild, workspace, index, registration
```

Then just work. With no arguments the server walks up from the current directory, finds your
`.sln` / `.slnx` / `.slnf` / `.csproj`, and loads it.

## 🧰 What it does

| | |
|---|---|
| 🔎 **Navigate by symbol, not by text** | Symbol search with CamelHump, real find-usages classified as call / read / write / override / implementation, go-to-definition, type & call hierarchies, implementations, overrides, dependents. Every result carries a **stable symbol ID** you pass straight back — no re-searching, no stale line numbers. |
| 📄 **Outlines instead of files** | `get_type_outline`, `get_file_outline`, `get_symbol_source` — member signatures and line ranges, then exactly the one body you asked for. This is where most of the token saving lives. |
| ✏️ **Edit by symbol** | `replace_symbol_body(symbolId, …)` — no `old_string` echo, no line numbers to drift. Every mutation is `dryRun`-able, returns **only a diff**, and is **rolled back if it introduces a compile error**. |
| 🔧 **Real refactorings** | Solution-wide rename (including interfaces, overrides, XML-doc `cref`s and the file name), change signature with every call site, extract method / interface / base class, move type to file / namespace / project, inline, encapsulate, safe delete. |
| 🩺 **Full ReSharper analysis, headless** | Two engines: Roslyn analyzers for the hot loop, plus the **free ReSharper Command Line Tools** for the complete ~2,500-inspection set — dead code, unused `using`s, redundancies, naming, nullability, `PossibleMultipleEnumeration`, and the rest — at every severity down to `HINT`, honouring **all** `.DotSettings` layers. |
| 🧹 **Cleanup & format** | ReSharper Code Cleanup profiles (built-in and your solution's custom ones) plus the Roslyn fast path. Idempotent, diff-only, rolled back on regression. |
| 🎨 **XAML as a first-class surface** | Outline, semantic find by element type / binding path / resource key, **binding validation against the resolved DataContext type**, `x:Name` and resource-key renames across code-behind and merged dictionaries, extract style / resource / UserControl, format and cleanup. WPF · Avalonia · WinUI · MAUI. |
| 📦 **Projects, solutions & packages** | Structural `.sln` / `.csproj` editing that preserves formatting, CPM-aware package add / remove / update, project references with cycle checks, MSBuild property provenance ("where did this value come from?"). |
| 🧪 **Tests that don't flood your context** | Discovery without running, **failures only** with the assertion frame (a green run is one line), rerun-failed, and which tests cover a given symbol. |
| 🐞 **Debug & profile** | Breakpoints, stepping, stacks, frame values and evaluation via **netcoredbg**; dumps and heap analysis via **ClrMD**; CPU/alloc traces via `dotnet-trace` with percent-filtered call trees. No proprietary tooling. |
| 🌳 **Built for parallel worktrees** | Run several agents at once across several git worktrees of one repo, and across unrelated repos — one server holding many workspaces, or many processes, or both. Index shards are **content-addressed**, so opening a second worktree of the same commit reuses **≥ 95 %** of the index and is ready in seconds. Every answer names its worktree and branch; an ambiguous request **errors instead of guessing**. |
| 🛡️ **Crash-only and corruption-proof** | Atomic writes, immutable checksummed shards, lock-free reads, write locks with a heartbeat that reclaim themselves when an owner dies. A `SIGKILL` at any instant leaves valid state — there is no recovery pass to get wrong. Proven by an 8-process × 3-worktree × 30-minute soak and 500 kill cycles. |
| 🏷️ **Tells you what it's guessing** | Every record is tagged `EXACT` (resolved through the Roslyn semantic model) or `HEURISTIC` (index, text, or an unresolvable DataContext). One character, and your agent stops treating an inference as a fact. |
| 🚫 **No fallbacks** | Non-C# file reads, structural outlines for JSON/XML/YAML/Markdown, an indexed text search with no process spawn, git status/diff, build, restore, process execution. A CI test asserts **every** built-in an agent might reach for has a TerseSharp counterpart. |

## ⚔️ Vs the alternatives

| | TerseSharp | Rider MCP | `RoslynMcpServer` / `RoslynMCP` | `csharp-lsp-mcp` | `graphify` |
|---|---|---|---|---|---|
| Needs a running IDE | **No** | Yes (Rider, licensed, solution open) | No | No | No |
| C# semantics | **Roslyn, exact** | Roslyn, exact | Roslyn, exact | via `csharp-ls` | tree-sitter, approximate |
| Can edit / refactor | **Yes** | Yes | Partial | Rename preview | No — read-only graph |
| Token budget enforced in CI | **Yes** | No | No | No | Output budgets, not gated |
| Speed target vs Rider MCP | **≤ 50 % / ≤ 25 %** | baseline | — | — | — |
| Persisted index across restarts | **Yes**, content-addressed | IDE-local | No | No | Yes, `graph.json` |
| Parallel worktrees / multi-repo | **First-class**, shared shards | One solution per IDE | No | No | Multi-context registry |
| Full ReSharper inspection set | **Yes** (CLT) | Capped at WARNING | No | No | No |
| XAML semantics | **20+ dedicated tools** | Not on the MCP surface | No | 7 tools | No |
| Project / solution / package editing | **Yes** | No | Partial | No | No |
| Setup | one command, one artifact | IDE + licence | tool install | tool install + `csharp-ls` | pip/uv + extras + API keys |
| E2E test per advertised tool | **Required** | — | — | — | — |

**What we took from [graphify](https://github.com/safishamsi/graphify)** — the best generic prior art
here: its per-file SHA-256 cache (generalised into content-addressed shards shared across worktrees),
its LRU multi-context server, its confidence-tagged edges (→ `EXACT`/`HEURISTIC`), bisecting an
oversized response instead of failing, ignore-file merge semantics, and one-command install **and**
uninstall.

**And what we took as a warning list.** graphify is powerful but painful to set up, and each of its
sharp edges is now an explicit anti-requirement: the package is `graphifyy` but the command is
`graphify`; MCP support is an *optional extra*; `pip`/`pipx` leaves it off `PATH`; a feature
disappears on Python 3.13+; parts of indexing need API keys; PowerShell needs different syntax; and
the index is committed to git, which then needs a merge driver to survive parallel commits.
TerseSharp: **one artifact, one name, one command, zero keys, zero network, and the index never
touches your repo.**

## ⚡ How it's fast

Rider MCP's latency floor is structural: `agent → MCP plugin (JVM) → RD protocol → ReSharper backend`
on a process that is also driving a GUI and a continuous inspection daemon. TerseSharp is
`agent → one process → Roslyn`.

On top of that:

- **Index-first, compile-later** — name search, outlines and file structure are served from a
  syntax-level declaration index. No `Compilation` is created at all for the four hottest tools.
- **Persisted index cache**, keyed by a content checksum, so the *second* cold start is ~5 s instead
  of minutes — agents restart constantly, and an IDE's caches can't help them.
- **Minimum compilation set** — semantic queries compile the owning project plus its dependents,
  never the solution.
- **Incremental re-parse** — one changed document re-parses one document.
- **Result cache** keyed by solution version — a repeated query answers in ~5 ms.
- **Parallel fan-out** across all cores, streaming into a bounded channel.
- **Slow engines never block** — the ReSharper pass runs in the background and every response header
  states which engines answered and whether anything was cached or stale.

## 📐 Design principles

1. **Semantic, never textual.** Queries take symbols, not byte patterns.
2. **Slices, never files.** No tool returns a whole file by default.
3. **Stable handles.** `M:Trading.OrderService.Submit(Trading.Order)` survives every edit.
4. **Bounded, compact responses.** Text, not JSON. `maxResults` everywhere. Explicit truncation.
5. **Data, never prose.** No preamble, no restatement, no advice, no closing summary.
6. **Concise never means incomplete.** Truncation is always declared; a missing result is a defect.

## 📋 Status & roadmap

| Phase | Scope | State |
|---|---|---|
| P1 | Workspace load, symbol search, outlines, compact formatter | ✅ v0.1.0 |
| P2 | Semantic navigation (usages, implementations), multi-workspace LRU, worktree awareness, `EXACT`/`HEURISTIC` | ✅ v0.1.0 · ⏸ content-addressed index, file watcher |
| P3 | Symbol-addressed edits, dryRun, compile gate, rollback | ✅ v0.1.0 · ⏸ undo |
| P4 | Solution-wide rename, diagnostics | ✅ v0.1.0 · ⏸ extract/move/change-signature, code fixes |
| P5 | ReSharper CLT integration, `analyze` / `format` / `cleanup` | 🔜 |
| P6 | Build + tests ✅ · projects & packages, full no-fallback file set | 🔜 partial |
| P7 | XAML | 🔜 |
| P8 | `terse install` / `uninstall` / `doctor` ✅ · agent skill, debug & profiling | 🔜 partial |

**Every phase exits with an E2E test per tool** — driven through a real MCP client, over the real
transport, against a real workspace, asserting values. A tool without one is not done.

Full specification: **[requirements](sharp-mcp-requirements.md)** — 192 numbered functional
requirements, 39 non-functional, 37 acceptance criteria, 45 test cases · **[design](sharp-mcp-design.md)** —
Rider parity matrix, architecture, performance and concurrency design, alternatives considered, risks.

## 🙅 What it deliberately doesn't do

- **Database / SQL tools** — that's DataGrip functionality bundled into Rider. No C# relevance, no
  token saving, and it would put credential storage and arbitrary SQL execution in a code server.
- **Unity / Unreal editor tools** — they read a live editor's state. A headless process cannot, and
  six broken tools are worse than none.
- **Commit / push** — git access is read-only. Your agent already has git.
- **VB.NET / F# language tools** — C# first; they load without breaking navigation and language
  tools refuse them with a clear message rather than guessing.

## 🤝 Contributing

The spec is complete and the phases are independently buildable — P1 is a good first slice. Two rules
that aren't negotiable: **a tool without an E2E test isn't done**, and **a tool that doesn't beat the
built-in it replaces doesn't ship**.

## 📄 License

MIT.
