# TerseSharp — Requirements

**A Roslyn-backed MCP server that lets a coding agent read, navigate, edit, refactor and clean a C#
solution semantically — without `Read`, `Grep`, `Glob` or line-number `Edit` — at a fraction of the
token cost.**

| | |
|---|---|
| Artifact class | Requirements / spec (greenfield, no ticket) |
| Repo | `C:\Users\afhac\source\sharp-mcp` (empty except `.idea` — verified 2026-07-30) |
| Companion | `sharp-mcp-design.md` — architecture, diagrams, Rider-parity matrix, tool schemas, alternatives, risks, phasing |
| Status legend | ✅ shipped · 🔜 planned · ⏸ deferred · ➖ dropped · ❌ blocked |
| Provenance | Greenfield: **everything here is DERIVED or ASSUMPTION.** Sources: the live JetBrains Rider MCP tool surface (enumerated from a running session), `carquiza/RoslynMCP`, `HYMMA/csharp-lsp-mcp`, NuGet package metadata. No TerseSharp code exists yet to ground against. |

## 0. Prime directive

> **Save tokens. Increase speed.** Everything else is subordinate.

This is the tie-breaker for every design decision in this document and every one taken later. When a
choice is between *more capable* and *cheaper/faster*, cheaper and faster wins unless the capability
is the reason the tool exists. Concretely, and enforced:

| The rule | Enforced by |
|---|---|
| No tool returns more than the question needs. Defaults are the smallest useful `detail` level. | NFR-1, NFR-3 |
| Compact text, never JSON, by default. | NFR-4 |
| Never force a `Compilation` when syntax answers the question. | NFR-18 |
| Never rebuild what an index or a cache already holds. | NFR-17, NFR-22 |
| A tool that does not measurably beat the built-in it replaces **does not ship**. | NFR-15, AC-13 |
| A tool nobody calls still costs `tools/list` tokens — so the surface is profiled, not dumped. | NFR-5b, AC-16 |

### 0.1 Token saving is a measured fact, not a design intention

The claim "saves tokens" is worthless unless it is proven per tool, per release. Therefore:

| Rule | Enforced by |
|---|---|
| **Every tool publishes its saving.** For each tool that replaces a built-in, CI measures both on the same fixture task and records `built-in tokens → Terse tokens → ratio`. The table ships in the README and is regenerated each release. | NFR-1b |
| **A tool that does not beat its built-in does not ship.** Not "is comparable to" — beats. | NFR-1b, AC-1 |
| **Responses contain data, never prose.** No preamble, no "Here are the results", no restating the request, no explanation, no advice, no apology, no trailing summary. One header line, records, optional one-line trailer. | NFR-4b |
| **Nothing is returned that was not asked for.** No sibling members "for context", no surrounding lines, no full file when a range was requested, no repeated file path on every line when results are grouped by file. | NFR-3, NFR-4b |
| **Every field must earn its bytes.** A field nobody filters or reasons on is removed. Repeated values are hoisted into the group header. | NFR-4b |
| **Regressions fail the build.** Each tool's token budget is asserted in its own E2E test. A change that makes a response chattier breaks CI. | NFR-29, AC-21 |

**Naming (researched, §9):** the product is **TerseSharp** — NuGet ID `TerseSharp`, prefix
`TerseSharp.*`, CLI command `terse`, all verified free against the NuGet API on 2026-07-30. "Sharp"
signals C#/.NET; "Terse" states the prime directive. `sharp-mcp` is the working directory only — an
unrelated `sharp-mcp` project already exists on GitHub.

---

> ⚠️ **Deviation from the ticket-md 120-line body cap, stated deliberately:** this is a standalone
> requirements document, not a tracker card. The cap exists so a board stays readable; there is no
> board. The full numbered FR set stays here because the FR *is* the tool contract. Architecture,
> diagrams, parity matrix and implementation ordering are still routed out, to `sharp-mcp-design.md`.

---

## 1. Business value

An agent working a C# solution today burns tokens on the wrong shape of data. `Read` on a
2,000-line file returns ~6,000 tokens to answer "what does this method do". `Grep "OrderService"`
returns every comment, string literal and unrelated partial match, then needs 3 more reads to find
the one definition. An `Edit` needs the exact surrounding text echoed back, twice.

Roslyn already knows the answer semantically. TerseSharp exposes that knowledge as MCP tools so the
agent asks **"give me the signature list of this type"** (≈400 tokens) instead of **"give me this
file"** (≈6,000), and says **"replace the body of `M:Ns.Type.Method(System.Int32)`"** instead of
quoting 20 lines of context that go stale the moment anything above them moves.

Who benefits:

- **Agent-driven .NET development** — longer sessions before context exhaustion; less compaction.
- **Users without JetBrains Rider** — Rider MCP requires a running paid IDE with the solution open.
  TerseSharp is a headless CLI/NuGet tool with no IDE, no license, no GUI.
- **CI and headless environments** — the same navigation and cleanup tools in a container.
- **Correctness** — a rename is a Roslyn rename across the solution, not a regex sweep that also
  renames a comment and misses an interface implementation.

---

## 2. Design — what the agent sees

### 2.1 The four principles every tool obeys

| # | Principle | Consequence |
|---|---|---|
| P1 | **Semantic, never textual** | Queries take symbols, not byte patterns. `find_usages` returns 12 real call sites, not 47 string matches. |
| P2 | **Slices, never files** | No tool returns a whole file by default. Outlines, signatures, single member bodies, line-ranged spans. |
| P3 | **Stable handles** | Every symbol has an ID (`M:Ns.Type.Method(System.Int32)`) valid across edits. Pass it back; never re-search, never re-quote context. |
| P4 | **Bounded, compact responses** | Every list tool has `maxResults`, a `detail` level and a truncation marker. Default output is compact text, not JSON. |
| P5 | **Answer from an index, not from a compilation** | Name lookup, outlines and file structure are served from a persisted syntax-level index. A `Compilation` is forced only when semantics are genuinely required. This is what makes TerseSharp faster than an IDE round-trip (§5.1). |

### 2.2 Response shape

Default `format=text`, one result per line, no wrapper objects:

```
find_usages M:Trading.OrderService.Submit(Trading.Order)
14 usages in 6 files (truncated=false)

src/Trading/OrderRouter.cs:88:21    call     OrderRouter.Route
src/Trading/OrderRouter.cs:132:17   call     OrderRouter.Retry
src/Trading/RiskGate.cs:41:9        call     RiskGate.Check
tests/Trading.Tests/SubmitTests.cs:23:9   call  SubmitTests.Submits_valid_order
...
```

`get_type_outline` on a 2,000-line class — the single biggest saving over `Read`:

```
C:Trading.OrderService  class, public, sealed  src/Trading/OrderService.cs:18-2041
  F:Trading.OrderService._repo          private readonly IOrderRepository        :22
  P:Trading.OrderService.PendingCount   public int { get; }                      :29
  M:Trading.OrderService.#ctor(Trading.IOrderRepository)   public               :34-41
  M:Trading.OrderService.Submit(Trading.Order)  public Task<SubmitResult>       :58-131
  M:Trading.OrderService.Cancel(Trading.OrderId)  public Task<bool>             :133-188
  ... 34 more members
```

The agent then fetches exactly one body with `get_symbol_source M:Trading.OrderService.Submit(...)`.

### 2.3 Edits are symbol-addressed, not line-addressed

```
replace_symbol_body
  symbolId : M:Trading.OrderService.Submit(Trading.Order)
  body     : "{ ... new implementation ... }"
  dryRun   : true
→ unified diff, 22 lines. Then dryRun:false to apply.
```

No `old_string` echo. No line numbers to drift. Roslyn re-parses and rejects the edit if the result
does not compile as a member declaration.

### 2.4 Typical session, before and after

| Task | Today (built-ins) | TerseSharp | Est. saving |
|---|---|---|---|
| "What's on `OrderService`?" | `Read` file → ~6,000 tok | `get_type_outline` → ~450 tok | ~13× |
| "Who calls `Submit`?" | `Grep "Submit"` → 60 hits, 3 follow-up reads → ~4,000 tok | `find_usages` → ~200 tok | ~20× |
| "Rename `Submit`→`SubmitAsync`" | grep + 9 edits, each echoing context → ~5,000 tok, misses interface | `rename_symbol` → ~150 tok, solution-wide, correct | ~30× |
| "Fix the build" | `dotnet build` full MSBuild output → ~8,000 tok | `build` → deduped diagnostics only → ~600 tok | ~13× |

> Numbers are **ESTIMATES** at 4 chars/token from typical file sizes, not measurements. NFR-1 turns
> them into a measured, enforced budget.

---

## 3. Localization

**No user-facing strings. N/A.** TerseSharp is a machine-facing MCP server; all output is consumed by
an LLM host. Tool names, parameter names, descriptions and error text are **English-only, invariant,
and never localized** — they are part of the wire contract, and translating them would break tool
selection. All culture-sensitive formatting (line numbers, counts, timings) uses
`CultureInfo.InvariantCulture` explicitly (see NFR-11).

---

## 4. Requirements

Priority: **P0** = MVP, ship first · **P1** = full Rider parity · **P2** = beyond Rider.
Every FR names the tool it defines. Full parameter schemas: `sharp-mcp-design.md` §4.

### 4.1 Workspace & session

| FR | P | Requirement |
|---|---|---|
| FR-1 | P0 | `load_workspace(path, [configuration], [targetFramework])` loads a `.sln`, `.slnx`, `.slnf` or `.csproj` via `MSBuildWorkspace` + `MSBuildLocator`. Returns project count, document count, load duration ms, and **every `WorkspaceFailed` diagnostic** — a partially loaded workspace is reported, never silently accepted. |
| FR-2 | P0 | The workspace is loaded **once per server process** and cached. Subsequent tool calls reuse it. Cold load of a 100-project solution completes in ≤ 180 s (NFR-2). |
| FR-3 | P0 | `workspace_status()` returns loaded path, project/document counts, whether compilations are warm, memory in MB, and staleness (documents changed on disk since load). |
| FR-4 | P0 | A `FileSystemWatcher` keeps the workspace in sync with external edits (the user's IDE, git checkout). Changed documents are re-parsed incrementally; added/removed files and `.csproj` changes trigger a scoped project reload. |
| FR-5 | P0 | `list_projects()` returns name, TFM(s), output kind, assembly name, document count, project path — one line each. |
| FR-6 | P1 | `project_dependencies([project])` returns the project reference graph, plus package references on request, and **flags cycles**. |
| FR-7 | P1 | `unload_workspace()` releases MSBuild file locks so the user can build/rebuild externally, and `reload_workspace()` re-opens it. |

### 4.2 Discovery & navigation (read)

| FR | P | Requirement |
|---|---|---|
| FR-8 | P0 | `search_symbols(query, [kinds], [projects], [accessibility], [maxResults=50])` — fuzzy/wildcard/exact name search over the whole solution's declared symbols. Returns `symbolId  kind  accessibility  file:line`. Substring, wildcard `*`, and CamelHump (`OSvc` → `OrderService`) all supported. |
| FR-9 | P0 | `get_symbol(symbolId \| file:line:col)` returns kind, full signature, accessibility, modifiers, attributes, containing type/namespace/project, declaration location, and the XML doc summary. |
| FR-10 | P0 | `get_type_outline(symbolId, [includeInherited=false], [includePrivate=true])` returns the member list with signatures and line ranges — **no bodies**. This is the primary replacement for `Read` on a type. |
| FR-11 | P0 | `get_file_outline(path)` returns every type in the file with its members, signatures, and line ranges — **no bodies**. Primary replacement for `Read` on a file. |
| FR-12 | P0 | `get_symbol_source(symbolId, [includeXmlDoc=true], [includeAttributes=true])` returns **only** that member's source text, plus its line range. |
| FR-13 | P0 | `find_usages(symbolId, [scope], [kinds], [contextLines=0], [maxResults=100])` — Roslyn `SymbolFinder.FindReferencesAsync`. Each hit is classified: `call`, `read`, `write`, `override`, `implementation`, `typeRef`, `nameof`, `xmldoc`. Grouped by file, counts in the header. |
| FR-14 | P0 | `goto_definition(file, line, col)` and `goto_definition(symbolId)` return the declaration site(s), including partial declarations, and resolve through interfaces to the concrete implementation on request. |
| FR-15 | P0 | `find_implementations(symbolId)` — interface/abstract member → all implementing members; interface/base type → all implementing/derived types. |
| FR-16 | P1 | `get_type_hierarchy(symbolId, [direction=both], [depth=3])` returns base chain and derived tree as an indented text tree. |
| FR-17 | P1 | `get_call_hierarchy(symbolId, direction=incoming\|outgoing, [depth=2], [maxNodes=100])` returns the call tree, cycles marked `↺`, depth-capped. |
| FR-18 | P1 | `find_overrides(symbolId)` and `find_overridden(symbolId)` walk the virtual/override chain in both directions. |
| FR-19 | P1 | `get_hover(file, line, col)` returns the resolved symbol, its type, and XML doc — the LSP `hover` equivalent, for when the agent has a position but no ID. |
| FR-20 | P1 | `find_symbols_by_attribute(attributeName, [kinds])` — e.g. every `[Fact]`, every `[Obsolete]`, every DI-registered service marker. |
| FR-21 | P1 | `get_namespace_tree([root], [depth])` returns the namespace → type map without touching the file system layout. |
| FR-22 | P1 | `find_dependencies(symbolId)` (what this type needs) and `find_dependents(symbolId)` (what needs it) — the blast-radius query, deduped to distinct types. |
| FR-23 | P1 | `search_text(pattern, [glob], [maxResults])` and `search_regex(...)` — the deliberate escape hatch for comments, strings, `.resx`, `.csproj`, `.json`. **Capped and scoped**; documented as "use only when the target is not a symbol". |
| FR-24 | P1 | `find_files(glob \| nameKeyword, [maxResults])` and `list_directory_tree(path, [depth], [glob])` for non-symbol file location. |
| FR-25 | P2 | `get_file_text(path, [startLine], [endLine])` — raw text, line-ranged. Exists for non-C# files and last-resort reads; **required to be the least-used read tool**, and its description says so. |
| FR-26 | P2 | `find_default_value_overrides(symbolId)` — Rider parity: locate call sites that pass a non-default value to an optional parameter. |

### 4.3 Diagnostics & quality

| FR | P | Requirement |
|---|---|---|
| FR-27 | P0 | `get_diagnostics(scope=file\|project\|solution, [target], [minSeverity=warning], [ids], [maxResults])` runs the Roslyn compilation **plus the project's configured analyzers** (`.editorconfig`, ruleset, `AnalysisLevel`) and returns deduplicated diagnostics: `id  severity  file:line:col  message`. |
| FR-28 | P0 | Diagnostics are **deduplicated across target frameworks**: a multi-targeted project reports each distinct diagnostic once with an occurrence count and a per-TFM breakdown. |
| FR-29 | P1 | `get_code_fixes(diagnosticId, file, line)` lists the Roslyn code fixes available at that location, each with a stable `fixId` and a title. |
| FR-30 | P1 | `apply_code_fix(fixId, [scope=single\|document\|project\|solution], [dryRun])` applies it, using Roslyn's **FixAll** provider for the wider scopes. |
| FR-31 | P2 | `explain_diagnostic(id)` returns the rule's title, category, default severity and help link — no web call, from analyzer metadata. |

### 4.4 Editing (semantic, minimal-token)

Every mutating tool in §4.4–§4.6 obeys **FR-59** (dryRun), **FR-60** (compile gate) and **FR-61**
(diff-only response).

| FR | P | Requirement |
|---|---|---|
| FR-32 | P0 | `replace_symbol_body(symbolId, body, [dryRun])` — replaces a method/property/accessor body. No line numbers, no context echo. |
| FR-33 | P0 | `replace_symbol(symbolId, declaration, [dryRun])` — replaces the whole member declaration including signature, attributes and XML doc. |
| FR-34 | P0 | `add_member(containingTypeId, declaration, [position=end\|after:<symbolId>\|before:<symbolId>], [dryRun])` inserts a member with correct indentation and separating trivia. |
| FR-35 | P0 | `delete_symbol(symbolId, [force=false], [dryRun])` — refuses when usages exist unless `force`, and lists them (safe delete). |
| FR-36 | P0 | `create_file(path, content \| scaffold{namespace, typeName, kind, usings}, [dryRun])` creates the file **and adds it to the owning project** when the project does not glob it implicitly. |
| FR-37 | P1 | `apply_text_edit(file, anchorSymbolId, oldText, newText, [occurrence], [dryRun])` — anchored exact-match replace for edits inside a body that are smaller than the body. Fails loudly on ambiguous matches. |
| FR-38 | P1 | `add_using(file \| project, namespace, [dryRun])` and `remove_unused_usings(scope, [dryRun])`. |
| FR-39 | P1 | `format_document(scope, [dryRun])` applies `.editorconfig` formatting via Roslyn `Formatter`. |
| FR-40 | P1 | `organize_imports(scope, [dryRun])` — sort, System-first per `.editorconfig`, remove unused. |
| FR-41 | P2 | `apply_patch(file, unifiedDiff, [dryRun])` — accepts a standard unified diff for multi-hunk edits the semantic tools cannot express. |

### 4.5 Refactoring (Roslyn, cross-file)

| FR | P | Requirement |
|---|---|---|
| FR-42 | P0 | `rename_symbol(symbolId, newName, [renameOverloads=false], [renameInComments=false], [renameInStrings=false], [dryRun])` — solution-wide `Renamer.RenameSymbolAsync`, including implementations, overrides, XML doc `cref`s, and the **file name** when the type is the file's only public type. Reports conflicts and refuses on unresolvable ones. |
| FR-43 | P1 | `extract_method(file, startLine, endLine \| startOffset, endOffset, newName, [dryRun])` — computes captured locals, ref/out-ness and return type. |
| FR-44 | P1 | `extract_interface(typeId, interfaceName, [members], [targetFile], [dryRun])` and `extract_base_class(typeId, baseName, [members], [dryRun])`. |
| FR-45 | P1 | `move_type_to_file(typeId, [targetPath], [dryRun])`, `move_type_to_namespace(typeId, namespace, [dryRun])` and `move_type_to_project(typeId, project, [dryRun])` — updating every reference and `using`. |
| FR-46 | P1 | `change_signature(methodId, parameters[{name,type,defaultValue,position}], [returnType], [dryRun])` — add/remove/reorder/retype parameters and update **every call site**, including named arguments. |
| FR-47 | P1 | `inline_method(symbolId, [dryRun])` and `inline_variable(file, line, col, [dryRun])`. |
| FR-48 | P2 | `pull_up_member(memberId, targetTypeId, [dryRun])` / `push_down_member(...)`. |
| FR-49 | P2 | `encapsulate_field(fieldId, [propertyName], [dryRun])` — field → property, all usages updated. |
| FR-50 | P2 | `introduce_parameter` / `introduce_field` / `introduce_variable` from a selected expression. |

### 4.6 Cleanup

| FR | P | Requirement |
|---|---|---|
| FR-51 | P1 | `cleanup_code(scope=file\|project\|solution, [profile=default\|full], [dryRun])` — one call runs: remove unused usings → organize imports → apply the `.editorconfig`/IDE code-style fixes → format. Returns a per-file changed-line count, not the diff, unless `dryRun`. |
| FR-52 | P1 | `find_dead_code(scope)` — unreferenced private members, unreachable code, unused private fields, unused parameters, unused locals, empty `catch` swallows. Report only; deletion goes through FR-35. |
| FR-53 | P2 | `apply_fix_all(diagnosticId, scope, [dryRun])` — bulk-apply one analyzer's fix across a project/solution via the FixAll provider. |
| FR-54 | P2 | `code_metrics(scope, [minComplexity])` — cyclomatic complexity, LOC, member count, depth of inheritance, coupling; sorted worst-first, capped. (Prior art: `RoslynMCP.AnalyzeCodeComplexity`.) |

### 4.7 Build, test, run configurations, VCS

| FR | P | Requirement |
|---|---|---|
| FR-55 | P0 | `build(scope=solution\|project, [target], [configuration])` runs the real build out-of-process and returns **deduplicated diagnostics only** — never raw MSBuild output. Includes elapsed ms and a success flag. |
| FR-56 | P1 | `run_tests([filter], [project], [maxFailures=20])` runs `dotnet test` and returns **failures only** by default: test name, message, and the assertion line of the stack trace — not the whole trace. A passing run returns one summary line. |
| FR-57 | P1 | `restore([project])` — NuGet restore, errors only. |
| FR-58 | P2 | `git_status()`, `git_diff([path], [staged])` and `list_repositories()` read-only, for change-scoping. Never commits, never pushes. |
| FR-68 | P1 | `list_run_configurations()` and `run_configuration(name, [args], [timeoutMs])` — Rider parity for `get_run_configurations` / `execute_run_configuration`. Sources: `launchSettings.json` profiles, `.sln` startup projects, and a `sharp-mcp.runconfigs.json` file. Output is captured, tail-capped and returned with the exit code. |
| FR-69 | P1 | `run_process(command, [args], [cwd], [timeoutMs], [tailLines=100])` — Rider parity for `execute_terminal_command`, but **output-capped and exit-code-aware**. Never blind-waits: the call returns when the process exits or the timeout elapses, and reports which. Disabled by `--read-only`. |
| FR-70 | P2 | `build` supports Rider's split shape too: `build_start()` returns a build id immediately and `build_state(id)` polls, for builds longer than the host's tool timeout. |

### 4.8 Safety & protocol

| FR | P | Requirement |
|---|---|---|
| FR-59 | P0 | **Every mutating tool accepts `dryRun` (default `false`) and returns a unified diff instead of applying when set.** |
| FR-60 | P0 | **Compile gate:** after any mutation, the affected documents are re-parsed and re-compiled. If the edit introduces a *new* compile error the change is **rolled back** and the error returned, unless `allowErrors=true`. Pre-existing errors never block an edit. |
| FR-61 | P0 | Mutating tools return **only the diff and a per-file changed-line count** — never the resulting file. |
| FR-62 | P0 | Every path parameter is resolved and validated to be inside the loaded workspace root; traversal outside is refused. |
| FR-63 | P1 | `undo_last_change()` reverts the most recent mutation using the retained pre-change `Solution` snapshot; the server keeps the last **10** snapshots. |
| FR-64 | P0 | Transport: **stdio by default** (`ModelContextProtocol` 2.0.0), with optional HTTP/SSE (`ModelContextProtocol.AspNetCore`) behind a flag for shared/containerized use. |
| FR-65 | P0 | Distribution: a .NET global tool (`dotnet tool install -g TerseSharp`) **and** a NuGet library, targeting **.NET 10** (SDK 10.0.301 present on this machine). |
| FR-66 | P1 | Configuration by CLI args and env vars: workspace path, log level, log file, response format default, `maxResults` default, read-only mode. **`--read-only` disables every tool in §4.4–§4.6.** |
| FR-67 | P1 | Structured logging to a file (never to stdout — stdout is the MCP transport). One line per tool call with name, duration ms, result size in bytes. |

### 4.9 Batch quality gates (Rider `lint_files` / `post_edit_quality_check` parity)

| FR | P | Requirement |
|---|---|---|
| FR-71 | P0 | `lint_files(files[], [minSeverity=warning], [timeoutMs])` — one call analyses **many** files and returns their diagnostics together. Per-file `timedOut` and a batch-level `more` flag are explicit: a file that was not analysed is **never** reported as clean. |
| FR-72 | P1 | `post_edit_quality_check(files[])` — the post-edit gate: compile errors + analyzers + formatting drift for the files just changed, in one call, returning only what regressed relative to a pre-edit snapshot. |
| FR-73 | P1 | Unlike Rider's MCP, severity is **not capped at warning**: `minSeverity=suggestion\|info` surfaces the info-level CA/IDE analyzer diagnostics that Rider's MCP silently hides and that today need a separate `dotnet format analyzers` sweep. |

### 4.10 Debugging (Rider parity — 17 tools)

Built on **netcoredbg** (Samsung, MIT, MI protocol) for live sessions and **ClrMD**
(`Microsoft.Diagnostics.Runtime`) for dumps and read-only inspection. No IDE, no proprietary
debugger. Optional module, loaded only with `--profile=full` or `--enable=debug`.

| FR | P | Requirement |
|---|---|---|
| FR-74 | P2 | `debug_start(configuration \| program, [args])` and `debug_attach(pid \| processName)` — parity with `start_debugger_session` / `attach_to_process`. Returns a session id. |
| FR-75 | P2 | `debug_set_breakpoint(file, line, [condition], [hitCount])`, `debug_remove_breakpoint(id)`, `debug_list_breakpoints()`. Breakpoint locations are **validated against the Roslyn syntax tree** before being sent — an unbindable breakpoint is rejected at call time, not silently ignored. |
| FR-76 | P2 | `debug_control(action=continue\|pause\|step_over\|step_into\|step_out\|run_to_line\|stop)` — parity with `control_session` / `run_to_line`. |
| FR-77 | P2 | `debug_status()`, `debug_get_threads()`, `debug_get_stack([threadId], [maxFrames=20])` — stacks are **capped and user-code-first** (external frames collapsed to one line), the single biggest token sink in debugger output. |
| FR-78 | P2 | `debug_get_frame_values(frameId, [depth=1], [maxItems=50])`, `debug_get_value_by_path(path)`, `debug_evaluate(expression)`, `debug_set_variable(path, value)`. Object graphs are depth- and count-capped by default. |
| FR-79 | P3 | `debug_memory_dump([pid], [path])` (creates a dump) and `dump_analyze(path)` — ClrMD: heap statistics, top types by size, thread stacks, deadlock detection, exception objects. Parity with `memory_dump`, and beyond it. |
| FR-80 | P3 | `debug_ignore_exception(type)` — parity with `ignore_exception`: suppress first-chance stops for a given exception type. |
| FR-81 | P3 | `debug_start_mixed_mode(...)` — native + managed. Parity with `start_mixed_mode_debug`; degraded to managed-only on platforms where netcoredbg cannot do it, and it **says so**. |

### 4.11 Runtime diagnostics & profiling (Rider dotTrace parity — 6 tools)

Built on `dotnet-trace` / `dotnet-counters` / `dotnet-gcdump` and **TraceEvent**
(`Microsoft.Diagnostics.Tracing.TraceEvent`) for parsing `.nettrace`. Open formats, no dotTrace
licence. Optional module (`--enable=profiling`).

| FR | P | Requirement |
|---|---|---|
| FR-82 | P3 | `trace_collect(pid \| program, [durationSec=10], [providers=cpu\|gc\|alloc\|all])` → a `.nettrace` path. |
| FR-83 | P3 | `trace_open(path)` + `trace_info()` — parity with `dotTraceOpenReport` / `dotTraceGetSnapshotInfo`. |
| FR-84 | P3 | `trace_call_tree([threadId], [minPercent=1], [maxNodes=100])` — parity with `dotTraceGetCallTree`, **percent-filtered by default** so a 200k-node tree never reaches the model. |
| FR-85 | P3 | `trace_timeline([bucketMs])` and `trace_events([filter], [maxResults])` — parity with `dotTraceGetTimeline` / `dotTraceGetTimelineEvents` / `dotTraceGetSnapshotFilters`. |
| FR-86 | P3 | `counters(pid, [counters], [durationSec])` — live CPU/GC/alloc/thread-pool counters. Beyond Rider MCP. |

### 4.12 Database — ➖ dropped

Rider's 13 database/SQL tools (`*_database_connection`, `list_database_schemas`,
`list_schema_object_kinds`, `list_schema_objects`, `get_database_object_description`,
`introspect_schema`, `execute_sql_query`, `fetch_query_result`, `cancel_sql_query`,
`preview_table_data`, `list_recent_sql_queries`) are **deliberately not implemented.** They are
DataGrip functionality that happens to be bundled in Rider — nothing to do with navigating or
editing C#, no token saving to win, and a large security surface (connection strings, credentials,
arbitrary SQL execution) for a server whose whole point is code. FR-87 → FR-90 are **withdrawn**;
the numbers are retired, not reused.

### 4.13 IDE-session tools — equivalents and honest gaps

| FR | P | Requirement |
|---|---|---|
| FR-91 | P1 | `list_recent_files()` / `list_active_files()` — TerseSharp's equivalent of Rider's `get_all_open_file_paths`: the files this session has touched, plus git-modified files. There is no editor, so "open" is redefined as "in play", and the description says so. |
| FR-92 | P2 | `reformat_file(path)` and `reorganize_namespaces(scope)` — direct parity with the same-named Rider tools (already covered functionally by FR-39/FR-40; exposed under familiar names as aliases). |
| FR-93 | P2 | `analyze_calls(symbolId, direction)` and `get_class_hierarchy(symbolId)` — Rider-named aliases of FR-17 / FR-16, so an agent trained on Rider's surface finds them. |
| FR-94 | P1 | `open_file_in_editor` has **no equivalent** and is not implemented — there is no editor. The tool is absent rather than a no-op stub, so the model never wastes a call on it. |
| FR-95 | P1 | `execute_tool` / `skill_search` are **host concerns, not server concerns** — the MCP host already owns tool dispatch and skill lookup. Deliberately not implemented; recorded in the parity matrix as ➖ with this reason. |

### 4.14 Complete coverage — the agent must never fall back to a built-in

**Requirement: every tool call a coding agent needs while working a .NET repo is served by Terse.**
Falling back to `Read`/`Grep`/`Glob`/`Edit`/`Bash` is the failure this product exists to prevent —
those tools are both slower (no index, no semantics, a process spawn each) and far more expensive in
tokens. Coverage is therefore specified as a **mapping with no holes** and audited by AC-17.

| Built-in the agent would otherwise reach for | Terse tool | FR |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline` / `get_type_outline` / `get_symbol_source` | FR-10–12 |
| `Read` a non-code file (`.json`, `.xml`, `.yml`, `.md`, `.resx`, `.csproj`, `.editorconfig`) | `read_text(path, [startLine], [endLine], [maxBytes])` | FR-96 |
| `Read` to understand a config file's shape | `outline_file(path)` — structural outline per format | FR-97 |
| `Grep` / `rg` | `search_text` / `search_regex` (indexed, §4.2) | FR-23, FR-98 |
| `Glob` / `find` / `ls` | `find_files` / `list_directory_tree` | FR-24 |
| `Edit` a `.cs` file | `replace_symbol_body` / `replace_symbol` / `apply_text_edit` | FR-32, 33, 37 |
| `Edit` a non-code file | `edit_text(path, oldText, newText, [occurrence])` / `apply_patch` | FR-99, FR-41 |
| `Write` a new file | `create_file` (C#) / `write_text` (anything) | FR-36, FR-99 |
| File moves, renames, deletes | `move_path` / `delete_path` — Roslyn-aware for `.cs` (updates the project, warns on referenced types) | FR-100 |
| `Bash` for `dotnet build` / `test` / `restore` / `format` | `build`, `run_tests`, `restore`, `cleanup_code` | FR-55–57, FR-51 |
| `Bash` for `dotnet add package` / `sln add` | §4.15 project & solution tools | FR-102–108 |
| `Bash` for `git status` / `diff` | `git_status` / `git_diff` | FR-58 |
| `Bash` for anything else | `run_process` — last resort, and its description says so | FR-69 |

| FR | P | Requirement |
|---|---|---|
| FR-96 | P0 | `read_text(path, [startLine], [endLine], [maxBytes=64KB])` — any file, line-ranged, byte-capped, with a truncation marker. Binary files are refused with their size and type rather than dumped. |
| FR-97 | P1 | `outline_file(path)` — **structural outline for non-C# formats**, so the agent sizes a file before reading it: JSON/YAML → key tree with value kinds and line numbers · XML/`.csproj`/`.resx` → element tree with attributes · Markdown → heading tree · `.editorconfig`/`.ini` → section+key list · `.sln`/`.slnx` → project list. Depth- and count-capped. |
| FR-98 | P0 | `search_text` / `search_regex` are served from a **persisted trigram index** over the whole repo, not a process spawn: no `rg` startup cost, results pre-grouped by file with counts, `contextLines=0` by default, and `.gitignore` + `bin`/`obj` honoured. p95 ≤ **80 ms** on a 1 M LOC repo (NFR-16). |
| FR-99 | P0 | `write_text(path, content, [dryRun])` and `edit_text(path, oldText, newText, [occurrence], [dryRun])` for non-C# files — atomic write, unified-diff response, ambiguous-match refusal. `.cs` files are routed to the semantic tools and `edit_text` refuses them unless `force=true`. |
| FR-100 | P1 | `move_path(from, to, [dryRun])` and `delete_path(path, [dryRun])` — for a `.cs` file this updates the owning project, and refuses when the file declares a type with live usages unless `force=true`. |
| FR-101 | P0 | **No-hole audit.** The mapping table above is a shipped test fixture: a CI test enumerates the host's built-in tool set and asserts every row has a live Terse tool. A new agent capability with no counterpart is a **build failure**, not a backlog item. |

### 4.15 Projects, solutions and packages — first-class, not shelled out

Editing `.csproj`/`.sln` through `Bash` + `Edit` costs a file read, a diff echo, and a fresh
`dotnet` process per call, and silently corrupts XML formatting. These are structured edits and Terse
does them structurally.

| FR | P | Requirement |
|---|---|---|
| FR-102 | P0 | `solution_list()` / `solution_add_project(path)` / `solution_remove_project(path)` / `solution_folders()` — `.sln`, `.slnx` and `.slnf` supported. Round-trips the file **preserving formatting and GUIDs**; never regenerates it. |
| FR-103 | P0 | `project_get_properties(project, [names])` and `project_set_property(project, name, value, [condition], [dryRun])` — MSBuild-evaluated read, targeted XML write. Returns the **evaluated** value and the file+line it came from (including inheritance from `Directory.Build.props`). |
| FR-104 | P0 | `project_add_reference(project, target)` / `project_remove_reference(...)` — project references, with a **cycle check before writing**. |
| FR-105 | P0 | `package_list([project], [outdated], [vulnerable], [transitive])`, `package_add(project, id, [version], [dryRun])`, `package_remove(project, id, [dryRun])`, `package_update(project, id, version, [dryRun])`. **Central Package Management aware**: with `ManagePackageVersionsCentrally`, versions are written to `Directory.Packages.props` and the `PackageReference` stays version-less. |
| FR-106 | P1 | `project_create(template, path, [tfm], [addToSolution])` and `project_files(project)` — the latter lists compiled items and, critically, **which are globbed vs explicitly included**, so the agent knows whether creating a file needs a project edit at all. |
| FR-107 | P1 | `project_targets(project)` / `project_tfms(project)` / `project_set_tfms(project, tfms, [dryRun])`. |
| FR-108 | P1 | `msbuild_evaluate(project, expression \| property)` — explain where a property's final value came from, across the whole import chain. The `binlog_explain_property` capability, without needing a binlog. |
| FR-109 | P2 | `editorconfig_get(path, [rule])` / `editorconfig_set(path, rule, severity, [scope], [dryRun])` — read/write analyzer severities structurally. |
| FR-110 | P1 | Every write in §4.15 obeys FR-59/60/61 (dryRun, validation gate, diff-only), and **re-evaluates the project after the edit** — a `.csproj` change that breaks evaluation is rolled back. |

### 4.16 Tests — discovery, execution and mapping

| FR | P | Requirement |
|---|---|---|
| FR-111 | P0 | `test_list([project], [filter])` — discovery **without running**, from the Roslyn model (`[Fact]`, `[Test]`, `[TestMethod]`, `[Theory]`) plus VSTest discovery. Returns `fqn  project  file:line`, capped. |
| FR-112 | P0 | `run_tests([filter], [project], [maxFailures=20], [includePassed=false])` — **failures only** by default: name, message, and the **assertion frame of the stack trace, not the whole trace**. A green run is one summary line: `312 passed, 0 failed, 41.2 s`. |
| FR-113 | P1 | `test_rerun_failed()` — reruns exactly the previous run's failures. The single most common agent loop, and today it costs a full re-enumeration. |
| FR-114 | P1 | `test_for_symbol(symbolId)` and `symbol_for_test(testFqn)` — which tests cover this member (via the call graph), and what a test exercises. Drives "add a regression test where the others live". |
| FR-115 | P2 | `test_coverage([filter])` — line/branch coverage per file via Coverlet, returned as **uncovered ranges only**, not a full report. |
| FR-116 | P1 | Test runs stream progress (NFR-25) and **never blind-wait**: the call returns on process exit or timeout and says which, with the partial results captured so far. |

### 4.17 Install and setup — one command, zero configuration

| FR | P | Requirement |
|---|---|---|
| FR-117 | P0 | **Single-command install.** Shipped as a .NET tool with `<PackageType>McpServer</PackageType>` **and** `DotnetTool`, so both work: `dotnet tool install -g Terse` and, on .NET 10+, direct execution via `dnx Terse` with no prior install. |
| FR-118 | P0 | **One-command registration with the agent.** `terse install [--client claude-code\|vscode\|cursor\|visualstudio]` writes the MCP server entry into that client's config itself — the user does not hand-edit JSON. `terse install` with no argument detects installed clients and reports what it registered. |
| FR-119 | P0 | **Zero required configuration.** With no arguments, the server walks up from the working directory to find a `.sln`/`.slnx`/`.slnf`, falling back to a single `.csproj`, and loads it. Multiple candidates → it exposes `load_workspace` and says which it found rather than guessing. |
| FR-120 | P0 | **No prerequisites beyond the .NET SDK.** No IDE, no licence, no Node, no Python, no separate language server, no manual `PATH` surgery. Startup verifies the SDK and MSBuild resolution and, on failure, prints the one command that fixes it. |
| FR-121 | P1 | **Ships an agent skill.** A `SKILL.md` is included in the package and installed by `terse install --skill`, teaching the agent which Terse tool replaces which built-in (the §4.14 table), so coverage is used in practice and not just available. |
| FR-122 | P1 | `terse doctor` — verifies SDK, MSBuild, workspace load, index cache, client registration and tool count in one command, and prints a fix line per failure. First thing to run when something is wrong. |
| FR-123 | P2 | Optional container image and a `--http` mode (FR-64) for shared/CI use, registered the same way. |

### 4.18 XAML — first-class, semantic, and joined to the C# model

XAML is where agents waste the most tokens in a .NET desktop repo: a 900-line `.xaml` read whole to
find one binding, and edits made by regex that silently break a `StaticResource`. Terse treats XAML
as a **typed tree joined to the Roslyn model** — element types, attached properties, resource keys,
`x:Name`s and binding paths all resolve to real C# symbols. Supported dialects: **WPF, Avalonia,
WinUI/UWP, MAUI**; the dialect is detected per project from its SDK and namespaces, and the tools say
which one they used.

**Discovery and reading**

| FR | P | Requirement |
|---|---|---|
| FR-124 | P0 | `xaml_outline(path, [depth=3], [includeAttributes=false])` — the element tree with line ranges, `x:Name`s, `x:Key`s and element types only. The replacement for reading a `.xaml` file: a 900-line view outlines in ≤ **400 tokens**. |
| FR-125 | P0 | `xaml_element(path, xamlPath \| line)` — one element with its full attribute set, resolved types, and line range. `xamlPath` is a stable address (`Window/Grid[1]/Button#SaveButton`). |
| FR-126 | P0 | `xaml_find(query)` — semantic search across all XAML: by **element type** (`Button`, including subclasses), **attached property** (`Grid.Row`), **binding path**, **resource key**, **style/template**, **converter**, **event handler**, or **`x:Name`**. Returns `file:line  element  match`, capped. This is what replaces `Grep` over `.xaml`. |
| FR-127 | P0 | `xaml_names(path \| project)` — every `x:Name`, its element type, and **every code-behind/ViewModel reference to it** (resolved through the generated `InitializeComponent` field via Roslyn). |
| FR-128 | P0 | `xaml_resources([scope], [kind])` — every `x:Key` resource: key, type, defining file:line, dictionary, and merge order. `xaml_resource_usages(key)` returns every `StaticResource`/`DynamicResource`/`ThemeResource` reference, **across merged dictionaries and theme variants**. |
| FR-129 | P0 | `xaml_bindings(path \| project, [maxResults])` — every binding with its path, mode, converter, `RelativeSource`/`ElementName`/`Source`, and the **resolved DataContext type**. |
| FR-130 | P1 | `xaml_styles([targetType])` and `xaml_templates([targetType])` — style/template inventory with `BasedOn` chains, and their usage sites. |
| FR-131 | P1 | `xaml_codebehind(path)` / `xaml_view_for(viewModelId)` — the joins: `.xaml` ↔ `.xaml.cs` ↔ ViewModel type, both directions. |

**Validation — the token win that regex can never give**

| FR | P | Requirement |
|---|---|---|
| FR-132 | P0 | `xaml_validate(path \| project)` — well-formedness, **type resolution** (element and attached-property types exist in the referenced assemblies, via the Roslyn compilation), property existence, `x:Key` duplicates, unresolved `StaticResource` keys, `BasedOn` cycles, markup-extension syntax, and `x:Name` collisions. Returns `file:line:col  code  message`, the same shape as `get_diagnostics`. |
| FR-133 | P0 | `xaml_find_binding_errors(path \| project)` — **binding paths checked against the resolved DataContext type through Roslyn**: missing property, wrong case, non-notifying property (no `INotifyPropertyChanged`), missing converter, `ElementName` that does not exist, command property that is not `ICommand`. These fail silently at runtime today; here they are compile-time-shaped output. |
| FR-134 | P1 | `xaml_find_unused()` — resources, styles, templates, converters, and `x:Name`s that nothing references, across XAML **and** C#. Report only; deletion goes through FR-137. |
| FR-135 | P1 | `xaml_find_hardcoded_strings(path \| project)` — user-visible literals not bound to a resource, with the property they sit on. Feeds localization work directly. |

**Editing, refactoring and cleaning**

Every tool below obeys FR-59/60/61 (dryRun, validation gate, diff-only) and, additionally,
**re-validates the XAML after the edit** (FR-132) — a change that breaks resolution is rolled back.

| FR | P | Requirement |
|---|---|---|
| FR-136 | P0 | `xaml_set_attribute` / `xaml_remove_attribute` / `xaml_replace_element` / `xaml_insert_element(parentPath, position, markup)` — structural edits addressed by `xamlPath`, not line number. Formatting of untouched siblings is byte-preserved. |
| FR-137 | P0 | `xaml_delete_element(xamlPath, [force])` — refuses when the element is `x:Name`d and referenced, or is a keyed resource with usages, and lists them. Safe-delete for XAML. |
| FR-138 | P0 | `xaml_rename_name(oldName, newName)` — renames an `x:Name` **and every code-behind reference, `ElementName` binding, `Storyboard.TargetName`, and trigger reference**. Solution-wide, conflict-checked. |
| FR-139 | P0 | `xaml_rename_resource_key(oldKey, newKey)` — renames an `x:Key` and every `StaticResource`/`DynamicResource`/`ThemeResource` reference across all dictionaries and themes. |
| FR-140 | P1 | `xaml_extract_style(xamlPath, key, [targetDictionary])` — lifts inline attributes into a keyed `Style` and replaces them with a `StaticResource` reference. |
| FR-141 | P1 | `xaml_extract_resource(xamlPath \| attribute, key, [targetDictionary])` — lifts a literal (brush, thickness, string) into a resource. |
| FR-142 | P1 | `xaml_extract_control(xamlPath, controlName, [targetProject])` — extracts a subtree into a new `UserControl` with its `.xaml` + `.xaml.cs`, rewrites the original to reference it, and **registers both files with the project**. |
| FR-143 | P1 | `xaml_move_resource(key, targetDictionary)` — moves a resource between dictionaries, fixing merge order and every reference. |
| FR-144 | P1 | `xaml_format(path \| scope, [profile])` — canonical formatting: indentation, attribute-per-line thresholds, **attribute ordering** (`x:Name`/`x:Key` first, layout, appearance, bindings, events), self-closing tags, and namespace-prefix alignment. Deterministic and idempotent. |
| FR-145 | P1 | `xaml_cleanup(scope)` — one call: remove unused `xmlns` prefixes, collapse redundant attributes matching the applied style, remove empty elements, sort resource dictionaries, then `xaml_format`. |
| FR-146 | P2 | `xaml_organize_namespaces(scope)` — normalize `xmlns` prefixes to the project's convention across all files. |
| FR-147 | P2 | `xaml_localize(path, keys[])` — replace hardcoded strings with `.resx` lookups, **creating the `.resx` entries**, in one call. Pairs with FR-135. |

**Coverage rule**

| FR | P | Requirement |
|---|---|---|
| FR-148 | P0 | No agent task on a `.xaml` file may require `Read`/`Grep`/`Edit` (FR-101 audit includes XAML): reading → FR-124/125, searching → FR-126, editing → FR-136, cleaning → FR-144/145. `read_text` on `.xaml` is permitted but its description points at `xaml_outline` first. |
| FR-149 | P1 | XAML tools are **auto-enabled**: present when the workspace contains at least one `.xaml`, absent otherwise, so non-UI solutions pay no `tools/list` tokens for them. |
| FR-150b | P1 | XAML analysis and formatting participate in §4.19: ReSharper's XAML inspections and its XAML formatter are exposed through the same `analyze` / `format` / `cleanup` tools, not a separate surface. |
| FR-150 | P1 | `.axaml` (Avalonia) and `.paml` are treated as XAML. Dialect-specific constructs (Avalonia selectors, `CompiledBinding`, WinUI `x:Bind`) are parsed and validated per dialect; an unsupported construct is reported as UNKNOWN, never silently accepted. |

### 4.19 Analysis and formatting — full ReSharper parity

**Requirement: everything ReSharper can find, Terse finds; everything ReSharper can clean, Terse
cleans — and it comes back in the same compact shape as every other Terse result.**

Roslyn analyzers alone cannot deliver this: ReSharper ships ~2,500 inspections, many with no Roslyn
equivalent (`UnusedMember.Global`, `ConvertToPrimaryConstructor`, `PossibleMultipleEnumeration`,
`ReturnTypeCanBeEnumerable.Local`, redundancy/naming/localization/annotation families), plus its own
formatter and cleanup profiles. So Terse runs **two engines**:

| Engine | What it is | Cost | Role |
|---|---|---|---|
| **Roslyn** | The compilation + the project's configured analyzers (`.editorconfig`, ruleset, `AnalysisLevel`) | ms | The hot loop — always on |
| **ReSharper** | JetBrains **ReSharper Command Line Tools** (`jb inspectcode`, `jb cleanupcode`) — **free, cross-platform, no IDE and no licence** | seconds to minutes | Full inspection + formatting parity |

> **DECISION:** integrate the real ReSharper CLT rather than re-implement 2,500 inspections. It is
> the only way "all current ReSharper features" is a true statement rather than an aspiration. The
> cost is latency, and §4.19 is built around hiding it (FR-156, FR-157).

**Analysis**

| FR | P | Requirement |
|---|---|---|
| FR-151 | P0 | `analyze(scope=file\|project\|solution, [target], [engine=auto\|roslyn\|resharper\|both], [minSeverity=suggestion], [categories], [ids], [maxResults])` returns diagnostics from both engines in **one deduplicated list**, in the same shape as `get_diagnostics`: `id  severity  category  file:line:col  message`. The response header names which engines contributed and whether any result came from cache. |
| FR-152 | P0 | **ReSharper's severity model is preserved**: `ERROR`, `WARNING`, `SUGGESTION`, `HINT`. `minSeverity` reaches all the way down to `hint` — the info/suggestion tier that Rider's own MCP silently drops (FR-73) and that today needs a separate `dotnet format analyzers` sweep. |
| FR-153 | P0 | **ReSharper inspection IDs and categories are preserved verbatim** (`UnusedMember.Global`, `RedundantUsingDirective`, `Redundancies in Code`, `Potential Code Quality Issues`, `Language Usage Opportunities`, …) so the agent can filter, suppress and reason about them exactly as a ReSharper user would. |
| FR-154 | P0 | **Severity configuration is honoured in full**: `.editorconfig`, solution `*.DotSettings`, project `*.DotSettings`, and the **"This computer" layer** (`GlobalSettingsStorage.DotSettings`) — the layer Rider's MCP ignores. `analyze` reports which settings layers it applied. |
| FR-155 | P0 | Explicitly covered inspection families, each with a named E2E test: **dead / unused code** (unused private, internal and public members, unused parameters, unused locals, unreachable code, unused type parameters, never-assigned fields) · **unused / redundant `using` directives** · redundant qualifiers, casts, `this.`, parentheses, `else`, string interpolation, `ToString()` · naming-convention violations · possible `null` dereference and nullability-annotation issues · `PossibleMultipleEnumeration` · disposable-not-disposed · async/await misuse · culture-sensitive formatting without an `IFormatProvider` · localization inspections · XAML inspections (FR-150b). |
| FR-156 | P0 | `analyze_rules([category], [severityOnly])` — enumerate **every available inspection**: id, category, default severity, **currently effective severity**, and which settings layer set it. This is how an agent discovers what the analyzer can even find, instead of guessing rule names. |
| FR-157 | P1 | `suppress(id, scope=line\|member\|file\|project\|solution, [target], [justification], [dryRun])` — writes the correct suppression form for the engine: ReSharper `// ReSharper disable` comment or `.DotSettings` entry, Roslyn `#pragma` / `[SuppressMessage]` / `.editorconfig`. Justification is required for file scope and above. |

**Making a slow engine feel fast** (prime directive, §0)

| FR | P | Requirement |
|---|---|---|
| FR-158 | P0 | `engine=auto` (the default) returns **Roslyn results immediately** plus **ReSharper results from cache when fresh**, and states in the header when the ReSharper pass is stale or still running. The agent is never blocked behind a multi-minute solution inspection to get an answer it could have had in 40 ms. |
| FR-159 | P0 | **Persistent ReSharper cache** — `jb inspectcode` runs with a stable `--caches-home` under Terse's cache dir, scoped with `--project=` to the projects that actually changed, and with `--no-build` when the build outputs are current. Results are cached per file content-hash. |
| FR-160 | P0 | **Background re-inspection.** After an edit, the affected projects are re-inspected on a background worker; the next `analyze` call finds fresh results. Progress is reported (NFR-25); nothing blind-waits, and a crashed `jb` process is detected and reported, never waited on. |
| FR-161 | P1 | First use **self-installs** the CLT (`dotnet tool install -g JetBrains.ReSharper.GlobalTools`) after telling the user, honouring FR-117's one-command promise. If installation is refused or fails, `analyze` degrades to `engine=roslyn` and **says so in every response header** — never silently returns a thinner result set. |

**Formatting and cleanup**

| FR | P | Requirement |
|---|---|---|
| FR-162 | P0 | `format(scope, [engine=auto], [dryRun])` — ReSharper's formatter via `jb cleanupcode` for full fidelity, Roslyn's `Formatter` as the fast path for a single file. Both honour `.editorconfig` **and** `*.DotSettings` formatting settings. Idempotent: running twice produces no second diff. |
| FR-163 | P0 | `cleanup(scope, [profile], [dryRun])` — full **ReSharper Code Cleanup**, including the built-in profiles (`Built-in: Full Cleanup`, `Built-in: Reformat Code`) **and custom profiles defined in the solution's `.DotSettings`**. `cleanup_profiles()` lists what is available. This supersedes FR-51, which becomes the Roslyn-only fast path. |
| FR-164 | P0 | The cleanup set explicitly includes: **remove unused `using`s**, sort/organize `using`s, **remove dead code and redundant members**, remove redundant qualifiers/casts/parens, apply modern-language rewrites (`var`, pattern matching, primary constructors, collection expressions), fix naming, add/remove braces, arrange trailing commas, arrange `this.` qualification, reformat, and XAML cleanup. Each is individually selectable. |
| FR-165 | P0 | Cleanup and format responses are **diff-only with a per-file changed-line count** (FR-61), obey `dryRun`, re-validate after the change, and roll back on a new compile error (FR-60). A cleanup that would touch files outside `scope` is refused. |
| FR-166 | P1 | `analyze` and `cleanup` are wired into the quality gates: `post_edit_quality_check` (FR-72) runs both engines over just-changed files and reports only what regressed. |

**Honest gaps**

| FR | P | Requirement |
|---|---|---|
| FR-167 | P0 | ReSharper **context actions and on-the-fly intentions** are not exposed by the command-line tools and therefore have **no full equivalent**. Terse covers the overlapping subset through Roslyn code fixes and refactorings (FR-29, FR-30, §4.5) and documents the gap in `analyze_rules` output rather than implying parity it does not have. |
| FR-168 | P1 | Where both engines report the same defect, the ReSharper id is reported with the Roslyn id as an alias, once — never twice (FR-151 dedup). Where they disagree on severity, the **effective configured** severity wins and the response says which layer set it. |

### 4.20 Multi-workspace, parallel use, and resilience

**Requirement: the same machine runs TerseSharp against several git worktrees of one .NET repo, and
against several unrelated repos, at the same time — from several agents — without corruption,
without lock fights, and without re-doing work that a sibling already did.**

This supersedes the earlier "one workspace per process" position (Q4), which does not survive the
worktree use case.

**Serving many workspaces**

| FR | P | Requirement |
|---|---|---|
| FR-169 | P0 | **One server, many workspaces.** `load_workspace` may be called repeatedly; loaded workspaces are held in an **LRU keyed by canonical solution path**, capped by `--max-workspaces` (default **4**, `0` = unbounded). Eviction unloads cleanly and releases every handle. Borrowed from graphify's `--max-contexts` (default 8) multi-graph server. |
| FR-170 | P0 | **Every tool accepts an optional `workspace`.** Resolution order: (1) explicit `workspace`, (2) the workspace whose root contains the `path`/`symbolId` argument, (3) the single loaded workspace. Two loaded workspaces and no way to disambiguate → `AMBIGUOUS_WORKSPACE`, listing them. **Never a silent guess** — picking the wrong worktree is the worst possible failure here. |
| FR-171 | P0 | `list_workspaces()` — path, branch, worktree name, project/document count, index freshness, memory, last used. `unload_workspace(path)` and `unload_all()`. |
| FR-172 | P0 | **Multi-process is equally supported.** N independent `terse` processes (one per agent, per client, per worktree) are a first-class deployment, not an accident. Everything shared between them — cache dirs, lock files, index shards — is safe for concurrent access by construction (FR-176). |
| FR-173 | P1 | **Worktree awareness.** `list_workspaces` and every workspace header report the **git worktree name and branch**, so an agent operating three worktrees of one repo can never confuse them. Two worktrees of the same repo are distinct workspaces with distinct caches. |

**Sharing work between worktrees — the payoff**

| FR | P | Requirement |
|---|---|---|
| FR-174 | P0 | **Content-addressed index shards.** The declaration/trigram index is stored per-file keyed by **SHA-256 of file content**, not by path. Two worktrees of the same repo differ in a handful of files; the other ~99 % of shards are **reused verbatim**, so the second worktree indexes in seconds. Directly generalises graphify's per-file SHA-256 semantic cache and its portable relative-path manifest. |
| FR-175 | P1 | Shard reuse is measured and reported: `workspace_status` states how many shards were reused vs built, so the saving is visible rather than assumed. Target: **≥ 95 % reuse** when opening a second worktree of the same commit. |

**Concurrency safety**

| FR | P | Requirement |
|---|---|---|
| FR-176 | P0 | **Every shared-file write is atomic**: write to a temp file in the same directory, `fsync`, then atomic rename. A process killed mid-write can never leave a partially written shard, manifest or lock. Readers never see a torn file. |
| FR-177 | P0 | **Cross-process locks are advisory, scoped and self-healing**: one lock per workspace for *writes only*, holding the owning PID and a heartbeat timestamp. A lock whose owner is dead or whose heartbeat is stale is **reclaimed automatically** with a logged warning. No lock is ever held across a tool call boundary. |
| FR-178 | P0 | **Readers never block.** Read tools operate on immutable `Solution` snapshots and content-addressed shards, so any number of processes read the same cache concurrently while one writes. |
| FR-179 | P0 | **Per-workspace isolation of external tools.** ReSharper CLT gets a **`--caches-home` per workspace** (JetBrains caches are not concurrent-safe); MSBuild runs with **node reuse disabled** so worktrees never share a build node; each workspace has its own `obj`/`bin` by construction. Concurrent NuGet restore is serialized per global-packages folder with the lock of FR-177. |
| FR-180 | P1 | **Bounded global resource use.** Total worker parallelism, compilation LRU size and memory ceiling are **process-wide, not per-workspace**, and configurable (`--max-workers`, `--memory-limit`). Four workspaces do not mean four times the RAM budget. Under memory pressure the least-recently-used workspace is evicted **before** the process is at risk. |

**Resilience**

| FR | P | Requirement |
|---|---|---|
| FR-181 | P0 | **A corrupt or unreadable cache is never fatal.** Every shard and manifest carries a format version and a checksum; a mismatch discards **that shard only** and rebuilds it, with one log line. Cache corruption degrades speed, never correctness. |
| FR-182 | P0 | **Crash-only design.** No shutdown step is required for correctness: killing the process at any moment leaves the on-disk state valid, because of FR-176 and FR-181. Startup performs no repair pass beyond dropping stale locks. |
| FR-183 | P0 | **Oversized responses auto-bisect instead of failing.** A response that would exceed the client's limit is split and returned with a cursor, never dropped and never truncated silently — the failure mode graphify hit and had to patch (context-length exceeded now retries with bisected chunks). |
| FR-184 | P0 | **A failing external process never hangs a call.** `jb inspectcode`, `dotnet build`, `dotnet test`, `netcoredbg` are supervised: liveness is polled, death is detected within **1 s** and reported with its exit code and log tail. **No blind waits, ever.** Timeouts are sized to the measured work, not to a ceiling. |
| FR-185 | P0 | **One bad workspace cannot take down the others.** Workspace load failure, MSBuild explosion, analyzer crash or watcher failure is contained: that workspace is marked failed with its reason, every other workspace keeps serving. |
| FR-186 | P1 | **Watchdog + self-report.** `terse doctor` and `workspace_status` surface: stale locks reclaimed, shards rebuilt after corruption, evictions, external-process failures, and watcher gaps. Silent degradation is the thing this requirement exists to prevent. |
| FR-187 | P1 | **Ignore rules.** `.gitignore` is honoured, plus a `.terseignore` whose patterns merge and **win on conflict** (graphify's `.graphifyignore` semantics). `bin`, `obj`, `node_modules`, `.git` excluded by default; `--no-gitignore` includes generated sources. |
| FR-188 | P1 | **The cache is never committed.** Cache and index live outside the repo (per-user cache dir keyed by canonical workspace path); nothing TerseSharp writes lands in `git status`. Explicitly *not* graphify's "commit `graphify-out/`" model — a machine-local index has no business in version control, and it removes the merge-conflict problem entirely rather than solving it with a merge driver. |

**Trust labelling**

| FR | P | Requirement |
|---|---|---|
| FR-189 | P0 | **Every result is tagged `EXACT` or `HEURISTIC`.** `EXACT` = resolved through the Roslyn semantic model (symbol references, overrides, resolved bindings). `HEURISTIC` = index/text/structural inference that could not be semantically confirmed (unresolved DataContext, dynamic dispatch, reflection, `nameof` in a string, text search hits). Adapted from graphify's `EXTRACTED` / `INFERRED` edge confidence. One character per record; the agent stops treating a guess as a fact. |
| FR-190 | P1 | A tool that can only answer heuristically **says so in its header** and names why (`DataContext unresolved`, `index-only, compilation not loaded`). Confidence is never implied by silence. |

**Anti-requirements — the setup failures observed in graphify, which TerseSharp must not repeat**

| FR | P | Requirement |
|---|---|---|
| FR-191 | P0 | **One artifact, no optional feature packages.** Every capability ships in the single `TerseSharp` package. No `[mcp]`-style extras where the MCP server itself is an optional dependency group; no capability silently missing because an extra was not installed. The only external tool fetched on demand is the ReSharper CLT (FR-161), and its absence is reported in every response header rather than degrading silently. |
| FR-192 | P0 | **One name, one command, everywhere.** Package `TerseSharp`, command `terse`, identical invocation on PowerShell, cmd, bash and zsh. No package-name/command-name mismatch (graphify's `graphifyy`-vs-`graphify` trap), no shell-specific syntax (`/graphify .` vs `graphify .`), no `PATH` step — the .NET global tool and `dnx` both handle it, and `terse doctor` verifies it. |
| FR-193 | P0 | **Zero API keys, zero network, zero telemetry.** All analysis is local and deterministic. No LLM backend, no cloud call, no key to configure, therefore no partially-indexed workspace caused by a missing key. `--offline` is the only mode there is. |
| FR-194 | P1 | **No runtime-version cliffs.** A single self-contained .NET artifact; no capability disabled by the host runtime's version (graphify loses Leiden clustering on Python 3.13+). |
| FR-195 | P1 | `terse uninstall [--client …]` removes the registration from every client in one command, and `terse cache clear [--workspace …]` purges caches. Setup that cannot be cleanly undone is setup people fear. |

---

## 5. Non-functional requirements

### 5.1 Speed — TerseSharp must be **faster than Rider MCP**, and prove it

Rider MCP's latency floor is structural: every call crosses the MCP process → the IDE's protocol
(RD) → the frontend/backend split → ReSharper's caches, on a JVM+.NET process that is also servicing
a GUI, indexing, and inspections. TerseSharp has none of that: one process, one workspace, no UI
thread, no protocol hop. That advantage is the product, so it is specified as a **measured, enforced
budget** — not a claim.

| NFR | P | Requirement |
|---|---|---|
| NFR-14 | P0 | **Comparative benchmark is a shipped artifact.** A harness runs the same N=12 query set (symbol search, outline, find-usages, hierarchy, diagnostics, rename-preview) against TerseSharp and against Rider MCP on the *same* solution, and reports p50/p95 per tool. It runs in CI on a fixed fixture and on demand against a large real solution. |
| NFR-15 | P0 | **Target: TerseSharp p95 ≤ 50 % of Rider MCP p95** on every comparable read tool, and **≤ 25 %** on the index-served tools (`search_symbols`, `get_type_outline`, `get_file_outline`, `find_files`). Falling above 50 % on any tool is a release blocker, not a nice-to-have. |
| NFR-16 | P0 | **Absolute warm budgets** (p95, 100-project / ~1 M LOC solution, index warm): `search_symbols` ≤ **50 ms** · `get_file_outline` ≤ **30 ms** · `get_type_outline` ≤ **50 ms** · `get_symbol` / `get_symbol_source` ≤ **80 ms** · `goto_definition` ≤ **150 ms** · `find_usages` (100 hits) ≤ **1.5 s** · `get_diagnostics` (one file) ≤ **400 ms** · `rename_symbol` preview ≤ **3 s** · `xaml_outline` ≤ **30 ms** · `xaml_find` ≤ **80 ms** · `xaml_validate` (one file) ≤ **300 ms**. |
| NFR-17 | P0 | **Persistent on-disk index.** A syntax-level symbol/declaration/trigram index is built once and stored under a cache dir keyed by solution path + a content checksum. A **second cold start reuses it** and is ≤ **5 s** to first query on a 100-project solution, versus ≤ 180 s for a first-ever load (NFR-2). Rider's caches are per-IDE-install and not reusable by an agent; ours are the point. |
| NFR-18 | P0 | **Index-first, compile-later.** `search_symbols`, `get_file_outline`, `get_type_outline`, `find_files`, `search_text` and `get_namespace_tree` are served **without forcing a `Compilation`**, from syntax + the index. Semantic tools (`find_usages`, `goto_definition`, hierarchy, diagnostics, all refactorings) force compilations for the **minimum project set** — the symbol's project plus its dependents, never the whole solution. |
| NFR-19 | P0 | **Incremental, never rebuild-the-world.** A changed document re-parses that document only (Roslyn's incremental `SyntaxTree` reuse) and invalidates exactly the dependent compilations. Edit-then-query round-trip on a 1 M LOC solution ≤ **500 ms** p95. |
| NFR-20 | P1 | **Warm on start.** With `--preload`, the workspace loads and the P0 indices build on a background thread while `tools/list` is already answering. First tool call never waits for more than the data it actually needs. |
| NFR-21 | P1 | **Parallel by default.** Project loading, index building and multi-project queries use all cores (`Parallel.ForEachAsync`, bounded by `Environment.ProcessorCount`). `find_usages` fans out across projects concurrently and streams results into a bounded channel. |
| NFR-22 | P1 | **Cache the answers, not just the trees.** Per-tool result caching keyed by (tool, args, solution version). An unchanged solution answers a repeated query from cache in ≤ **5 ms**. Cache is invalidated by document version, not by timer. |
| NFR-23 | P1 | **Allocation-conscious hot paths.** Response building uses pooled `StringBuilder`/`ArrayPool`, `Span`-based formatting and no intermediate LINQ materialization in the per-result loop. No `Compilation` is pinned by a strong root beyond its LRU. |
| NFR-24 | P1 | **Latency is reported.** Every response carries an `elapsedMs` trailer (≤ 1 line). Slow calls are logged with a breakdown (load / index / semantic / format) so regressions are attributable, not guessed. |
| NFR-25 | P2 | **Streaming for long operations.** Tools that can exceed 5 s (`find_usages` on a hot symbol, `cleanup_code` on a solution, `build`) report progress via MCP progress notifications rather than going silent. |

> ⚠️ **Where Rider wins and we accept it:** Rider's ReSharper engine has years of tuning on some
> inspections, and its caches survive across solution *branches*. TerseSharp does not attempt to beat
> ReSharper on inspection breadth (FR-27 uses Roslyn analyzers). The speed claim is scoped to
> **navigation, structure, search, and edit round-trip** — the operations an agent actually spends
> its turns on.

### 5.2 General

| NFR | P | Requirement |
|---|---|---|
| NFR-1 | P0 | **Token budget, measured and enforced.** Default response for `get_type_outline` on a 40-member type ≤ **800 tokens**; `find_usages` with 20 hits ≤ **500 tokens**; `get_symbol` ≤ **150 tokens**; `build` on a solution with 10 distinct diagnostics ≤ **700 tokens**. A benchmark harness asserts these in CI (AC-1). |
| NFR-2 | P0 | Cold `load_workspace` ≤ **180 s** for a 100-project solution; ≤ **20 s** for a 10-project solution. Warm tool calls (post-load, compilation cached) ≤ **2 s** at p95; `search_symbols` ≤ **500 ms**. |
| NFR-3 | P0 | Every list-returning tool has `maxResults` with a documented default (50 or 100) and a **hard server cap**, and sets `truncated=true` with the total count when it clips. No tool can return an unbounded response. |
| NFR-1b | P0 | **Published, per-tool token savings.** CI runs each tool and the built-in it replaces against the same fixture question and records `built-in → Terse → ratio`. Target **≥ 5×** on read/navigation tools and **≥ 3×** on edit tools. A tool below **2×** is a defect: either it is redesigned or it is cut. The table is regenerated per release and published. |
| NFR-4 | P0 | Default `format=text` (compact, one record per line). `format=json` is opt-in. **JSON is never the default** — it costs 2–3× the tokens for the same information. |
| NFR-4b | P0 | **Zero verbosity, enforced by test.** Responses carry data only: no preamble, no restatement of the request, no explanation, no advice, no closing summary, no decorative separators, no empty fields, no field repeated per-row that belongs in the group header. Every response is greppable, one record per line. A CI check fails any response whose non-data bytes exceed **5 %** of its length. |
| NFR-4c | P0 | **Accuracy is a precondition of concision, not a trade against it.** A shorter answer that omits a real result is a defect, not an optimization: truncation is always explicit (`truncated=true, total=N`), and `maxResults` never silently drops. Concise means *nothing superfluous*, never *nothing missing*. |
| NFR-5 | P0 | Tool **descriptions are part of the product.** Each description states when to use the tool *and which built-in it replaces* (e.g. "use instead of Read on a .cs file"), in ≤ 3 lines. |
| NFR-5b | P0 | **Profiles keep the surface affordable.** Full Rider parity is ~95 tools, which would cost more context in `tools/list` than it saves. Three profiles: `core` (P0, ~25 tools, ≤ **2,000 tokens**) · `default` (P0+P1, ~55 tools, ≤ **4,500 tokens**) · `full` (everything, ≤ **8,000 tokens**). Optional modules (`debug`, `profiling`) are **off unless enabled**; XAML tools load automatically only when the workspace contains `.xaml`. `default` is the default. |
| NFR-6 | P1 | Memory ≤ **4 GB** for a 100-project solution with compilations warm. Compilations are cached weakly and recomputed on demand rather than pinned. |
| NFR-7 | P0 | **Concurrency:** the workspace is single-writer. Mutating tools serialize on a workspace lock; read tools run concurrently against an immutable `Solution` snapshot. |
| NFR-8 | P0 | **Failure modes are explicit.** Workspace-not-loaded, symbol-not-found, ambiguous-symbol, edit-conflict, project-load-failure each return a distinct error code and a one-line remedy. Never an empty success. |
| NFR-9 | P1 | Source generators are honoured — generated documents are visible to navigation but **read-only**; a mutation targeting one is refused with the generator's name. |
| NFR-10 | P1 | Multi-targeted projects: navigation resolves against a selected TFM (`targetFramework` param, first TFM by default) and says which one it used. |
| NFR-11 | P0 | Code standards: explicit `IFormatProvider` on every culture-sensitive format/parse (`CultureInfo.InvariantCulture` throughout — all output is machine-readable); immutable `record` results; `readonly record struct` ids (`SymbolId`, `ProjectId`, `DocumentPath`) never raw `string`/`Guid`; `switch` expressions over `if`/`else`; everything disposable disposed. |
| NFR-12 | P1 | Cross-platform: Windows, Linux, macOS. No IDE, no GUI, no JetBrains dependency, no license. |
| NFR-13 | P2 | Startup ≤ **1 s** to first `tools/list` response; the workspace loads lazily on first use or eagerly with `--preload`. |
| NFR-26 | P0 | **Every MCP tool has an E2E test. No exceptions, no "trivial" exemption.** A tool is not done until a test drives it *through a real MCP client over the real transport against a real workspace* and asserts the **values** in its response — not that it returned something. See §7.4. |
| NFR-27 | P0 | **CI gate on coverage-by-tool:** a test enumerates `tools/list` and fails the build if any advertised tool has no E2E test registered against its name. Adding a tool without a test is a **build failure**, not a review comment. |
| NFR-28 | P0 | Every tool additionally has **unit tests** (logic, formatting, truncation, error paths) and, where it touches the workspace, **integration tests**. Three tiers per tool: unit + integration + E2E. A tier that genuinely cannot apply is declared in the test file with the reason. |
| NFR-29 | P1 | E2E tests assert the **token budget** of each response alongside its content (NFR-1), so a regression that makes a tool chattier fails a test rather than quietly costing users money. |
| NFR-31 | P0 | **Parallel scaling.** 4 workspaces loaded in one process, or 4 processes each with one workspace, stay within the single-process memory ceiling ±25 % and within 1.5× the single-workspace p95 latency. Parallelism is bounded process-wide (FR-180), not multiplied per workspace. |
| NFR-32 | P0 | **Second-worktree cost.** Opening a second git worktree of the same commit reuses **≥ 95 %** of index shards and reaches first-query-ready in **≤ 10 s** on a 100-project solution — versus ≤ 180 s for a first-ever index (NFR-2). |
| NFR-33 | P0 | **Concurrency soak.** 8 processes × 3 worktrees × 30 minutes of mixed read/write traffic against a shared cache produces **zero** corrupt shards, zero deadlocks, zero lost updates, and no unreclaimed locks. Run nightly, not per-PR. |
| NFR-34 | P0 | **Kill soak.** Processes killed with `SIGKILL` at random points during index writes, edits and builds leave on-disk state valid every time; the next start needs no repair beyond dropping stale locks (FR-182). 500 kill cycles, zero corruption. |
| NFR-35 | P1 | **Isolation of failure.** With one deliberately broken workspace loaded (unloadable projects, corrupted cache, killed analyzer), the other workspaces' p95 latency degrades by **≤ 10 %** and none of their calls fail. |
| NFR-30 | P1 | Install and setup are E2E tested from a clean container: `dotnet tool install -g Terse` → `terse install --client claude-code` → an MCP client connects and calls a tool. Covers FR-117–FR-122. |

---

## 6. Parity coverage and non-goals

**Requirement: every tool in the current Rider MCP surface (~90 tools, enumerated 2026-07-30) has a
TerseSharp equivalent, or an explicit written verdict.** The row-by-row matrix is
`sharp-mcp-design.md` §2 and AC-11 enforces it. Summary:

| Rider MCP area | Tools | Verdict |
|---|---|---|
| Search / navigation / symbols | 12 | ✅ equivalent, superset (§4.2) |
| Read / files / directories | 5 | ✅ equivalent, plus outline tools Rider has no answer to |
| Edit / refactor / cleanup | 13 | ✅ equivalent, superset (§4.4–§4.6) |
| Problems / lint / quality | 4 | ✅ equivalent, **superset** — info-severity included (FR-73) |
| Build / run / test / projects | 6 | ✅ equivalent (§4.7) |
| VCS | 2 | ✅ equivalent, read-only (FR-58) |
| Debugger (incl. `xdebug_*`) | 17 | 🔜 equivalent via netcoredbg + ClrMD (§4.10), optional module |
| dotTrace profiling | 6 | 🔜 equivalent via dotnet-trace + TraceEvent (§4.11), optional module |
| Database / SQL | 13 | ➖ **dropped on request** — DataGrip functionality, not C# work (§4.12) |
| IDE session (`open_file_in_editor`, `get_all_open_file_paths`) | 2 | ➖ / ✅ redefined — see FR-91, FR-94 |
| Host dispatch (`execute_tool`, `skill_search`) | 2 | ➖ host concern — FR-95 |
| Game engine (`search_assets`, `get_asset_properties`, `search_tags`, `spawn_actor`, `viewport_camera`, `take_screenshot`) | 6 | ➖ **genuinely not reproducible** |

**The one honest gap.** The six game-engine tools require a **running Unity or Unreal editor** with
JetBrains' plugin attached — they read live editor state (scene graph, asset database, viewport
camera, editor screenshot). A headless process cannot produce that, and pretending otherwise would
ship six broken tools. If Unity support is ever wanted it must be a **separate Unity-side plugin**
that TerseSharp talks to, not a Roslyn feature. ⏸ recorded, not planned.

Genuine non-goals (deliberately out of scope, no Rider counterpart lost):

| ➖ Dropped | Why |
|---|---|
| Database / SQL tools (13 Rider tools) | Dropped on the user's explicit instruction. DataGrip functionality bundled in Rider; no C# relevance, no token saving, and it would add credential handling and arbitrary SQL execution to a code server. |
| VB.NET / F# language tools | C# only in v1. The workspace loads them so navigation does not break; language tools reject them with a clear error rather than guessing. |
| Writing to git (commit / push / branch) | Read-only VCS only (FR-58). Irreversible, outward-facing, and the host already has git. |
| A GUI, an editor, or an LSP server | TerseSharp speaks MCP. If an LSP surface is ever wanted it wraps the same core, it does not replace it. |

---

## 7. QA / acceptance criteria

### 7.1 Acceptance criteria (Given / When / Then)

| AC | Covers | Criterion |
|---|---|---|
| AC-1 | NFR-1 | **Given** a fixture solution with a 40-member, 2,000-line type, **when** the benchmark harness calls `get_type_outline`, **then** the response is ≤ 800 tokens and contains every member's signature and line range. The same harness asserts the `find_usages`, `get_symbol` and `build` budgets. CI fails on regression. |
| AC-2 | FR-42 | **Given** a method implementing an interface, overridden in two derived types, referenced in XML doc `cref` and in a test, **when** `rename_symbol` runs, **then** all 5+ sites and the interface declaration are updated, the solution still compiles, and a `Grep` for the old name returns zero hits in code. |
| AC-3 | FR-59, FR-60 | **Given** any mutating tool with `dryRun=true`, **then** no file on disk changes and a unified diff is returned. **Given** an edit that introduces a new compile error with `allowErrors=false`, **then** the change is rolled back, the file is byte-identical to before, and the error is returned. |
| AC-4 | FR-1, NFR-8 | **Given** a solution containing one unloadable project, **when** `load_workspace` runs, **then** it succeeds, reports the loaded count, and lists the failed project with its `WorkspaceFailed` reason — it neither throws nor silently reports zero documents. |
| AC-5 | FR-4 | **Given** a loaded workspace, **when** a `.cs` file is changed outside the server, **then** the next `get_type_outline` on it reflects the change without a manual reload. |
| AC-6 | FR-13 | **Given** a symbol whose name also appears in comments, strings and an unrelated type, **when** `find_usages` runs, **then** only real semantic references are returned, each classified, and the count matches Rider's find-usages count on the same fixture. |
| AC-7 | FR-27, FR-28 | **Given** a multi-targeted project (`net10.0;netstandard2.0`) emitting the same warning per TFM, **when** `get_diagnostics` runs, **then** the diagnostic appears **once** with `×2` and a per-TFM breakdown. |
| AC-8 | NFR-7 | **Given** two concurrent mutating calls, **then** they serialize and both results are consistent; **given** 10 concurrent read calls during a mutation, **then** none observe a half-applied solution. |
| AC-9 | FR-62, FR-66 | **Given** a path outside the workspace root, **then** every tool refuses it. **Given** `--read-only`, **then** every §4.4–§4.6 tool is absent from `tools/list`. |
| AC-10 | NFR-5 | **Given** the full tool surface, **when** `tools/list` is called, **then** the serialized schema is ≤ 6,000 tokens; `--profile=core` returns only the P0 tools. |
| AC-11 | Parity | **Given** the Rider-parity matrix in `sharp-mcp-design.md` §2, **then** every row marked *in scope* has a shipped tool, and every row marked *dropped* appears in §6 above with a reason. |
| AC-12 | FR-65 | **Given** a clean machine with .NET 10, **when** `dotnet tool install -g TerseSharp` then `terse --workspace <sln>` runs, **then** an MCP host connects over stdio and `tools/list` succeeds — no IDE installed. |
| AC-13 | NFR-14, NFR-15 | **Given** the same solution open in Rider and loaded in TerseSharp, **when** the comparative harness runs the 12-query set, **then** TerseSharp p95 is ≤ 50 % of Rider MCP p95 on every comparable read tool and ≤ 25 % on the four index-served tools. The report is published with the release. |
| AC-14 | NFR-16, NFR-17 | **Given** a warm 100-project solution, **then** every absolute budget in NFR-16 holds at p95 over 100 runs. **Given** the server is restarted, **then** the persisted index is reused and the first query answers within 5 s — proven by deleting the cache dir and observing the difference. |
| AC-15 | NFR-18, NFR-19 | **Given** `get_file_outline` on a cold solution, **then** no `Compilation` is created (asserted by instrumentation, not by timing). **Given** one document edited, **then** only that document re-parses and only dependent projects re-compile. |
| AC-16 | NFR-5b | **Given** `--profile=core`, `default` and `full`, **then** `tools/list` serializes within 2,000 / 4,500 / 8,000 tokens respectively, the `debug`/`profiling` modules are absent unless explicitly enabled, and the XAML tools are absent when the workspace has no `.xaml` file. |
| AC-17 | FR-96–101 | **Given** a scripted realistic task (find a bug, read config, edit a `.cs` and a `.json`, add a package, build, test, commit-ready) run **with every host built-in disabled**, **then** it completes using Terse tools only. Any point where the agent would have needed `Read`/`Grep`/`Glob`/`Edit`/`Bash` is a failed assertion naming the missing tool. |
| AC-18 | FR-117–122 | **Given** a clean container with only the .NET 10 SDK, **when** `dotnet tool install -g Terse && terse install --client claude-code` runs, **then** the client config contains the server, `terse doctor` reports all green, and a tool call succeeds against an auto-discovered solution — **zero manual JSON editing, zero extra prerequisites**. |
| AC-19 | FR-102–110 | **Given** a solution using Central Package Management, **when** `package_add` runs, **then** the version lands in `Directory.Packages.props`, the `PackageReference` is version-less, the file's existing formatting is byte-preserved outside the change, and the project still evaluates. **Given** an edit that breaks evaluation, **then** it is rolled back. |
| AC-20 | FR-111–116 | **Given** a suite with 312 tests and 2 failures, **then** `run_tests` returns ≤ 700 tokens containing both failures with their assertion frames and no passing-test output; **then** `test_rerun_failed` runs exactly those 2. |
| AC-21 | NFR-26, NFR-27 | **Given** the shipped `tools/list`, **then** every advertised tool name maps to at least one E2E test that invoked it over the real transport and asserted response values. **Given** a new tool added without such a test, **then** CI fails. |
| AC-22 | FR-124–126 | **Given** a 900-line `.xaml`, **when** `xaml_outline` runs, **then** the response is ≤ 400 tokens and names every `x:Name`, `x:Key` and element type with its line range. **Given** `xaml_find(elementType=Button)`, **then** subclasses of `Button` are included and comments/strings containing "Button" are not. |
| AC-23 | FR-133 | **Given** a binding to `{Binding CustomerNmae}` on a view whose DataContext is `CustomerViewModel`, **when** `xaml_find_binding_errors` runs, **then** it reports the file, line, the unresolved path, and the nearest matching property `CustomerName`. **Given** a valid binding, **then** nothing is reported. |
| AC-24 | FR-138, FR-139 | **Given** an `x:Name` referenced from code-behind, an `ElementName` binding and a `Storyboard.TargetName`, **when** `xaml_rename_name` runs, **then** all four sites change, the project compiles, and the XAML re-validates. Same for `xaml_rename_resource_key` across two merged dictionaries and a theme variant. |
| AC-25 | FR-136, FR-144 | **Given** a structural XAML edit, **then** untouched siblings are byte-identical and the file re-validates; a change that breaks type resolution is rolled back. **Given** `xaml_format` run twice, **then** the second run produces no diff (idempotent). |
| AC-27 | FR-151–155 | **Given** a fixture file seeded with one instance of each family in FR-155 — an unused private method, an unused public member, an unused `using`, a redundant cast, a naming violation, a `PossibleMultipleEnumeration`, an undisposed `IDisposable`, a culture-sensitive `ToString()` — **when** `analyze(engine=both, minSeverity=hint)` runs, **then** **every one** is reported with its ReSharper id, category and severity, and nothing is reported twice. |
| AC-28 | FR-154, FR-156 | **Given** an inspection elevated to `ERROR` in the solution `.DotSettings` and another elevated in the "This computer" layer, **then** `analyze` reports both at `ERROR` and `analyze_rules` names the layer that set each. This is the case Rider's own MCP gets wrong. |
| AC-29 | FR-158, FR-161 | **Given** a cold cache, **when** `analyze(engine=auto)` runs, **then** Roslyn results return within the NFR-16 budget and the header states the ReSharper pass is running. **Given** the CLT is not installed and installation is declined, **then** results still return, and **every** response header says `engine=roslyn (resharper unavailable)`. |
| AC-30 | FR-162–165 | **Given** a file with unused `using`s, dead code, redundant casts and bad formatting, **when** `cleanup(profile="Built-in: Full Cleanup")` runs, **then** all four classes are fixed, the project compiles, `analyze` reports zero of those ids, and a second `cleanup` produces an empty diff. |
| AC-31 | FR-169–171, NFR-31 | **Given** three git worktrees of one repo and one unrelated repo loaded into a single server, **when** a tool is called with a `path` inside worktree B, **then** it is served from worktree B's workspace; **when** called with an ambiguous `symbolId` and no `workspace`, **then** it returns `AMBIGUOUS_WORKSPACE` listing all four — **never** a silent pick. |
| AC-32 | FR-174, NFR-32 | **Given** worktree A fully indexed, **when** worktree B of the same commit is loaded, **then** ≥ 95 % of shards are reused, `workspace_status` reports the reuse count, and first query is ready within 10 s. |
| AC-33 | FR-176–179, NFR-33 | **Given** the 8-process × 3-worktree soak, **then** zero corrupt shards, zero deadlocks, zero stale locks left behind, and every ReSharper run used its own `--caches-home`. |
| AC-34 | FR-181, FR-182, NFR-34 | **Given** a shard truncated on disk, **then** that shard alone is rebuilt and the call succeeds. **Given** 500 `SIGKILL` cycles at random points, **then** every restart finds valid state and needs no repair pass. |
| AC-35 | FR-185, NFR-35 | **Given** one workspace whose projects fail to load and whose analyzer process is killed mid-run, **then** it is marked failed with its reason and the other three workspaces keep serving within 10 % of their normal p95. |
| AC-36 | FR-189, FR-190 | **Given** a `find_usages` result mixing semantic references with text-index hits, **then** each record is tagged `EXACT` or `HEURISTIC`; **given** XAML bindings on a view whose DataContext cannot be resolved, **then** the header says so and no binding is reported as an error. |
| AC-37 | FR-191–195 | **Given** a clean machine, **then** one install command yields the **complete** tool surface with no optional extras, no API key, no `PATH` step, and identical invocation on PowerShell and bash; **then** `terse uninstall` removes every client registration and `terse cache clear` leaves no state behind. |
| AC-26 | FR-148, FR-149 | **Given** the no-fallback task (AC-17) extended with XAML work, **then** it completes with no `Read`/`Grep`/`Edit` on a `.xaml`. **Given** a workspace with no `.xaml`, **then** the XAML tools are absent from `tools/list`. |

### 7.2 Headline test cases

| TC | Pri | Case | Expected ✅ |
|---|---|---|---|
| TC-01 | P1 | Load the fixture solution (5 projects, one multi-targeted, one with a source generator, one unloadable) | Loads, reports 4/5 projects, names the failure, generated documents visible |
| TC-02 | P1 | `search_symbols("OSvc")` CamelHump | `OrderService` in the first 3 results |
| TC-03 | P1 | `get_file_outline` on a 3-type, 1,500-line file | All 3 types, all members, correct line ranges, no bodies, ≤ 800 tokens |
| TC-04 | P1 | `get_symbol_source` on a partial-class member | Returns that member only, from the correct partial file |
| TC-05 | P1 | `find_usages` on an interface method | Implementations + call sites through the interface, each classified |
| TC-06 | P1 | `replace_symbol_body` on a method whose line numbers moved since the last read | Succeeds — proves symbol addressing beats line addressing |
| TC-07 | P1 | `rename_symbol` producing a conflict (name already taken in a derived type) | Refused, conflict reported, nothing changed |
| TC-08 | P1 | `change_signature` adding a parameter with a default | Every call site compiles; named-argument call sites correct |
| TC-09 | P1 | `cleanup_code` on a project with unused usings and style violations | Compiles, `dotnet format --verify-no-changes` clean afterwards |
| TC-10 | P2 | `build` on a solution with 3 errors across 2 projects | 3 diagnostics, no MSBuild spew, ≤ 700 tokens |
| TC-11 | P2 | `run_tests` with 2 failures out of 300 | 2 failures with assertion lines only; no passing-test output |
| TC-12 | P2 | `delete_symbol` on a still-referenced private method | Refused, usages listed |
| TC-13 | P2 | Kill the host mid-mutation | No partial write on disk; workspace consistent on restart |
| TC-14 | P2 | Legacy non-SDK `.csproj` in the solution (`dotnet/roslyn#82931`) | Either loads, or fails with an actionable message naming the project — never a raw `RemoteInvocationException` |
| TC-15 | P3 | 200-project solution | Loads within NFR-2, memory within NFR-6 |
| TC-16 | P1 | Same query set against Rider MCP and TerseSharp, 100 runs each | TerseSharp p95 ≤ 50 % of Rider's on every comparable tool; report emitted |
| TC-17 | P1 | Restart the server, repeat TC-03 | Answer within 5 s of process start; index read from cache, not rebuilt |
| TC-18 | P1 | `get_file_outline` with a compilation-creation counter attached | Counter stays at 0 |
| TC-19 | P2 | Edit one file in a 1 M LOC solution, immediately `find_usages` on a symbol in it | ≤ 500 ms p95; result reflects the edit |
| TC-20 | P2 | Repeat an identical query with no intervening change | ≤ 5 ms, served from the result cache |
| TC-21 | P2 | `debug_start` → breakpoint → `debug_get_stack` on the fixture console app | Stops at the line; stack ≤ 20 frames, user code first, external frames collapsed |
| TC-22 | P3 | `trace_collect` 10 s on the fixture app, then `trace_call_tree` | ≤ 100 nodes, ≥ 1 % filter applied, hot method identifiable |
| TC-24 | P1 | `lint_files` over 40 files where 2 time out | 38 reported, 2 flagged `timedOut` — **never** reported as clean |
| TC-25 | P1 | `xaml_outline` on a 900-line WPF window | ≤ 400 tokens, every `x:Name`/`x:Key`, correct line ranges |
| TC-26 | P1 | `xaml_find_binding_errors` on a view with 1 typo'd path, 1 missing converter, 1 non-`ICommand` command | All 3 found, nearest-match suggested; 20 valid bindings not reported |
| TC-27 | P1 | `xaml_rename_resource_key` for a brush used in 2 merged dictionaries + a dark theme | All references updated; app builds; XAML re-validates |
| TC-28 | P1 | `xaml_extract_control` on a 40-line subtree | New `.xaml` + `.xaml.cs` created, added to the project, original references it, both compile |
| TC-29 | P1 | `xaml_format` run twice on a messy file | Second run yields an empty diff (idempotent) |
| TC-30 | P2 | Same suite against an **Avalonia** `.axaml` and a **WinUI** `x:Bind` view | Dialect detected and reported; unsupported constructs flagged UNKNOWN, never silently accepted |
| TC-31 | P2 | `xaml_find_unused` on a dictionary with 3 dead brushes and 1 used only from C# | Reports the 3, does **not** report the C#-referenced one |
| TC-32 | P1 | The FR-155 seeded fixture through `analyze(engine=both)` | Every seeded defect found, ReSharper ids preserved, no duplicates |
| TC-33 | P1 | `analyze(engine=auto)` on a cold cache | Roslyn results ≤ 400 ms; header declares the ReSharper pass pending; a later call returns the full set |
| TC-34 | P1 | `jb inspectcode` killed mid-run | Detected as a dead process within 1 s, reported, `analyze` degrades — **never** waits out the timeout |
| TC-35 | P1 | `cleanup` with a custom `.DotSettings` profile | Custom profile listed by `cleanup_profiles()` and applied; diff-only response; idempotent |
| TC-36 | P2 | `suppress("UnusedMember.Global", scope=member)` | Correct ReSharper comment form inserted; `analyze` no longer reports it; no other suppression touched |
| TC-37 | P1 | 3 worktrees of one repo + 1 unrelated repo in one server; call with a path in worktree B | Served from B; branch and worktree name in the header |
| TC-38 | P1 | Same, ambiguous `symbolId`, no `workspace` argument | `AMBIGUOUS_WORKSPACE` listing all four — never a silent pick |
| TC-39 | P1 | Load worktree B after A is indexed | ≥ 95 % shard reuse reported; ready ≤ 10 s |
| TC-40 | P1 | 8 processes × 3 worktrees, 30 min mixed read/write soak | Zero corrupt shards, zero deadlocks, zero stale locks, per-workspace ReSharper caches |
| TC-41 | P1 | `SIGKILL` mid index-write, 500 cycles | Valid state every restart; no repair pass; no partial shard |
| TC-42 | P1 | Truncate a shard on disk, then query it | That shard rebuilt, one log line, call succeeds |
| TC-43 | P2 | Kill `jb inspectcode` mid-run while 3 workspaces are busy | Death detected ≤ 1 s, reported with exit code + log tail; other workspaces unaffected |
| TC-44 | P2 | Load a workspace with unloadable projects alongside 3 healthy ones | Marked failed with reason; healthy p95 degrades ≤ 10 % |
| TC-45 | P2 | Concurrent `package_add` in two worktrees sharing the NuGet global-packages folder | Serialized by the workspace lock; both succeed; no corrupt restore |
| TC-46 | P2 | `terse uninstall` then `terse cache clear` | No client registration remains; no cache directory remains; repo `git status` clean throughout |

### 7.3 The E2E mandate — every tool, verified working end to end

**Rule: no tool ships without an E2E test that proves it works through the real protocol.** Not a
unit test of its handler. Not "the integration test covers it". An E2E test that starts the server
as a **separate process**, connects a **real MCP client** over the **real transport**, calls the
tool by its advertised name with its advertised schema, and asserts the **values** in the response.

| Rule | Detail |
|---|---|
| **Per tool, not per feature** | Terse advertises ~95 tools across the `full` profile; the E2E suite has ≥ 95 named tests. `tools/list` is the checklist and CI compares against it (NFR-27, AC-21). |
| **Real transport** | stdio by default, plus a `--http` pass for the transport-sensitive subset. A tool that works in-process but fails over stdio (serialization, ordering, large payloads) is a real and common defect. |
| **Real workspace** | The committed fixture solution — multi-TFM, source generator, unloadable project, CPM, a console app for debugging, a test project with deliberate failures. Not mocks. |
| **Assert values, not survival** | `Assert.Equal(14, usages.Count)` and the exact expected lines — never "did not throw", never "response is not empty". |
| **Both directions** | Every mutating tool is tested for the success path **and** the refusal path: dryRun leaves disk untouched, compile gate rolls back, ambiguous match refuses, read-only refuses, out-of-workspace refuses. |
| **Budget asserted** | Response token count asserted against NFR-1 in the same test (NFR-29). |
| **Round-trip where state exists** | Edit → re-read → assert the change is visible; restart the server → assert the persisted index still answers; write a `.csproj` → re-evaluate. |
| **Degradation is declared, not silent** | A tool whose backend is absent (netcoredbg, a `.nettrace` recorder, Rider for the comparative run) gets a **skip-clean** test that reports *which tier was degraded and why* — never a silent pass. |

### 7.4 Test tiers

- **Unit** — one Roslyn fixture workspace built in-memory (`AdhocWorkspace`) per tool: symbol resolution, outline shaping, diff generation, response formatting, `maxResults`/truncation, every error code in §4.3 of the design. Covers FR-8→FR-26, FR-32→FR-41, NFR-3, NFR-4, NFR-8.
- **Integration** — the on-disk fixture solution (§7.2 TC-01) loaded through `MSBuildWorkspace`: every refactoring FR, the compile gate, dryRun/rollback, the file watcher, multi-TFM dedup. Covers FR-1→FR-7, FR-27→FR-31, FR-42→FR-58.
- **E2E** — **one named test per advertised tool** (§7.3), plus scenario tests: `tools/list` per profile, the 12-call navigate→edit→build→test session, the no-fallback task (AC-17), `--read-only`, the clean-container install (AC-18, NFR-30). Covers every FR, and is the tier that decides whether a tool is done.
- **Benchmark** — two harnesses, both CI-gated: a **token-budget** harness asserting NFR-1/NFR-5b, and a **latency** harness (BenchmarkDotNet + a soak runner) asserting NFR-16/NFR-19/NFR-22. Covers AC-1, AC-14, AC-15, AC-16.
- **Comparative** — the Rider-vs-Terse harness (NFR-14, AC-13, TC-16). Runs on demand, not in CI: it needs a licensed Rider with the solution open. **Degraded tier** — CI asserts only the absolute budgets (NFR-16); the comparative ratio is verified per release on a developer machine, and the report is attached to the release.
- **Module tests** — debug (§4.10) against a fixture console app with netcoredbg; profiling (§4.11) against a recorded `.nettrace`. Each module's suite is skippable-clean when its prerequisite is absent, and says which tier was skipped.
- **Manual only** — TC-15 (200-project solution) and TC-14 (legacy non-SDK project), both requiring machine-specific solutions. Reason: no such fixture can be committed.

---

## 8. Open questions

| # | Question | Default if unanswered |
|---|---|---|
| Q1 | Tool-name prefix (`sharp_find_usages`) to avoid collisions with other MCP servers in the same host? | **No prefix.** Names chosen to be distinct (`find_usages`, not `search`). Revisit if a collision is observed. |
| Q2 | `MSBuildWorkspace` (needs the SDK, slow, accurate) vs. a hand-rolled `.csproj` reader (fast, fragile)? | **MSBuildWorkspace** — correctness first; NFR-2 budgets the cost. Alternatives table in `sharp-mcp-design.md` §5. |
| Q3 | Should `build`/`run_tests` live here at all, given the host has Bash? | **Yes** — the token saving is in the *output filtering* (FR-55, FR-56), which Bash cannot do. |
| Q4 | ~~Single process per solution, or one server multiplexing several workspaces?~~ | **RESOLVED 2026-07-30 — both, from v1.** The earlier "one workspace per process, multi-workspace is v2" answer does not survive the parallel-worktree requirement. §4.20 now specifies an LRU multi-workspace server **and** safe concurrent multi-process operation; memory is bounded process-wide (FR-180) rather than per workspace. Superseded answer kept so it is not re-proposed. |
| Q5 | Ship the P2/P3 tools at all, or keep the surface small for NFR-5b? | Ship behind `--profile=full` and per-module `--enable=` flags; `core` and `default` stay lean. |
| Q6 | Debugger backend: **netcoredbg** (MIT, MI protocol, cross-platform, less complete) vs **vsdbg** (complete, but its licence forbids use outside Microsoft tooling)? | **netcoredbg.** vsdbg's licence makes it unshippable here. Accept reduced mixed-mode support (FR-81). |
| Q7 | Is a persisted index (NFR-17) safe against a stale cache after a git branch switch? | Key the cache on a **content checksum** of all `.cs` + project files, not on mtime; a mismatch rebuilds only the changed projects. Verify in TC-17. |
| Q8 | ~~Do the database tools belong in a *C# code* server at all?~~ | **RESOLVED 2026-07-30 — no.** Dropped on the user's instruction; §4.12 records the reason. Kept here so it is not re-proposed. |
| Q9 | Do the debug and profiling modules survive the same test as the database module did? | They stay for now — both are C#/.NET runtime work and Rider parity was explicitly requested — but they are P2/P3 and the first candidates if scope has to shrink. Ask before building P6. |
| Q10 | `search_text` from a self-built trigram index (FR-98) vs shelling out to `ripgrep` | **Own index.** Shelling out costs a process spawn per call and forfeits the no-fallback goal; the index is already being built for `search_symbols`. Cost: index build time and memory, budgeted in NFR-2/NFR-6. |

---

## 9. Naming — research and decision

Checked against the **NuGet search API** (`azuresearch-usnc.nuget.org/query?q=packageid:<id>`,
prerelease included) on **2026-07-30**, for both the exact root ID and the ID prefix. Web search was
not treated as evidence of availability — only the API was.

### 9.1 Why not the obvious names

| Candidate | Verdict |
|---|---|
| `sharp-mcp` (current folder) | ➖ **A `sharp-mcp` MCP server already exists** — a 23-tool Roslyn + NuGet-reflection C# server. NuGet root `SharpMcp` is free, but the public name collides where it matters: discovery. |
| `RoslynMcp`, `Roslyn.Mcp`, `roslyn-mcp` | ➖ **Crowded, five ways**: `carquiza/RoslynMCP`, `JoshuaRamirez/RoslynMcpServer` (already owns the `RoslynMcp.Server` global tool ID and ships 41 tools), `sailro/RoslynMcpExtension`, `darylmcd/roslyn-mcp`, `YaroslavHorokhov/RoslynMcp`. Unusable. |
| `Sextant` | ➖ Active ReactiveUI navigation library, v4.0.30. |
| `Crux` | ➖ Root free, but `Crux.Core` / `Crux.WebApi` squat the prefix (dormant since 2015). |
| `Scalpel` | ➖ Root free, but `Scalpel.Fody` exists and "scalpel" reads as *cutting code out*. |
| `Trim` | ➖ Collides head-on with .NET **trimming**. Actively confusing. |
| `Sift`, `Chisel`, `Lucid`, `Concise`, `Sharpen`, `Laconic`, `Lithe`, `Deft`, `Brisk`, `Nimble` | ➖ All taken as exact NuGet root IDs. |
| `Loupe`, `Prism`, `Quill`, `Sleek`, `Compass`, `Nutshell`, `Marrow`, `Distil`, `Hone` | ➖ Root technically free but the prefix is occupied (`Loupe.Agent.*`, `Prism.*`, `Hone.Basic`, `Marrow.XPlat.*`, `DistIL.*`, `Nutshell.Bus`, …). |

### 9.2 Clean on both root and prefix

`Terse` · `Whittle` · `Lodestar` · `Brevity` · `Adroit` · `Swift`¹ · `Cue`²

¹ Free on NuGet but unusable — Apple's language. ² Free on NuGet but collides with **CUE**, the
configuration language.

### 9.3 Constraint added by the user: the name must signal .NET / C#

A bare value-prop name (`Terse`, `Whittle`, `Lodestar`) fails this — nothing in it says the tool is
for C#. Re-checked against the NuGet API on 2026-07-30, `Sharp`/`.NET`/`csharp`-carrying candidates:

| Candidate | Root | Prefix | Verdict |
|---|---|---|---|
| **`TerseSharp`** | free | free | ✅ **both constraints met** — "Terse" = the prime directive, "Sharp" = C# |
| `SharpTerse` | free | free | ✅ clean, but worse rhythm and reads as a typo of the above |
| `SharpLens`, `SharpScope`, `SharpSight` | free | free | ✅ clean; signal C# + inspection, but say nothing about token saving |
| `RoslynSharp`, `NetTerse`, `TerseNet` | free | free | ⚠️ clean but awkward; `Roslyn*` also re-enters the crowded space of §9.1 |
| `CSharpMcp` / `CsharpMcp` | **taken** | — | ➖ unavailable |
| `DotnetMcp` / `DotNetMcp` | free | **occupied** | ➖ prefix already in use |
| `SharpMcp` | free | free | ➖ NuGet-free but an existing `sharp-mcp` GitHub project owns the name in search |
| `SharpNav`, `SharpKit`, `SharpCode`, `Sharpen` | taken | — | ➖ unavailable |

### 9.4 DECISION — **TerseSharp**

| Criterion | Why TerseSharp wins |
|---|---|
| Signals .NET / C# | "Sharp" is the ecosystem's own suffix — `SharpZipLib`, `RestSharp`, `MCPSharp`. Nobody mistakes it for a Python tool. |
| States the prime directive | "Terse" is the product: concise, accurate, no verbosity (§0, NFR-1). |
| Verified clean | NuGet root `TerseSharp` = 0 hits, `TerseSharp.*` prefix = 0 hits (API-checked, not web-searched). |
| Types well | CLI command stays **`terse`** — `terse install`, `terse doctor`, `terse --workspace App.sln`. |
| Clean namespace family | `TerseSharp` (tool), `TerseSharp.Core`, `TerseSharp.Tools.Xaml`, `TerseSharp.Tools.Debug`. |

Repo `terse-sharp`; NuGet title *"TerseSharp — token-efficient Roslyn MCP server for C# and .NET"*;
tags `mcp roslyn csharp dotnet code-navigation refactoring xaml resharper ai-agent`. Runners-up if a
plainer name is preferred: **SharpLens** or **SharpScope** (both verified clean).

> Renaming is cheap **now** and expensive after the first NuGet publish (IDs are permanent). Decide
> before P1 ships. The folder `sharp-mcp` is a working directory, not the product name.

---

*Local working note — do NOT commit/push. Companion: `sharp-mcp-design.md`.*
