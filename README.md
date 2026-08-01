<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>Your agent stops reading whole C# files.</b><br/>
  A Roslyn-powered <a href="https://modelcontextprotocol.io">MCP</a> server that lets a coding agent
  navigate, read, edit and refactor a .NET solution <b>semantically</b> —
  no <code>Read</code>, no <code>Grep</code>, no line-number <code>Edit</code>, no shelling out.
</p>

<p align="center">
  <a href="#-install">
    <img src="https://img.shields.io/badge/⚡_Install_in_30s-dotnet_tool_install-512BD4?style=for-the-badge&labelColor=141414" alt="Install" height="42"/>
  </a>
  &nbsp;
  <a href="#-make-your-agent-actually-use-it">
    <img src="https://img.shields.io/badge/🔒_Force_your_agent-to_use_it-8A2BE2?style=for-the-badge&labelColor=141414" alt="Force your agent to use it" height="42"/>
  </a>
</p>

<p align="center">
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/release.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/release.yml/badge.svg" alt="Release"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/v/TerseSharp.svg?logo=nuget&label=NuGet" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/dt/TerseSharp.svg?logo=nuget&label=downloads" alt="Downloads"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="MIT"/></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet&logoColor=white" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/Roslyn-semantic-512BD4.svg" alt="Roslyn"/>
  <img src="https://img.shields.io/badge/XAML-WPF_·_Avalonia_·_WinUI_·_MAUI-0078D4.svg" alt="XAML"/>
  <img src="https://img.shields.io/badge/tools-82-26C281.svg" alt="82 tools"/>
  <a href="CONTRIBUTING.md"><img src="https://img.shields.io/badge/PRs-welcome-brightgreen.svg" alt="PRs welcome"/></a>
</p>

<p align="center">
  <a href="#-why">Why</a> ·
  <a href="#-install">Install</a> ·
  <a href="#-make-your-agent-actually-use-it">Enforce it</a> ·
  <a href="#-the-tools">Tools</a> ·
  <a href="#-xaml-that-knows-about-your-c">XAML</a> ·
  <a href="#-vs-the-alternatives">Comparison</a> ·
  <a href="#-status">Status</a> ·
  <a href="RELEASING.md">Releasing</a>
</p>

---

## 🤔 Why

An agent working a C# solution spends most of its context on the wrong shape of data. Roslyn already
knows every answer **semantically** — TerseSharp hands it over in the shape the agent needs.

| Question | With built-in tools | With TerseSharp | |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → **~6,000 tok** | `get_type_outline` → **~450 tok** | **13×** |
| Who calls this method? | `Grep` + follow-ups → **~4,000 tok** | `find_usages` → **~200 tok** | **20×** |
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| 2 failures out of 312 tests | full test output | 2 failures + assertion lines | **10×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

<sub>Asserted by the token-budget suite in CI, not estimated.</sub>

**Prime directive: save tokens, increase speed.** Every design decision is settled by that rule — a
tool that does not beat the built-in it replaces does not ship.

---

## ✨ What you get

| | |
|---|---|
| 🧠 **Semantic, never textual** | Real references, not string matches. Every record tagged `EXACT` (Roslyn-resolved) or `HEURISTIC` (text/index) so you always know what you are trusting. |
| ✂️ **Slices, never files** | No tool returns a whole file by default. Outlines give types, members, signatures and line ranges — never bodies. |
| 🔗 **Addressable by name** | An outline prints `OrderService.Submit(Order)`; feed it straight back to any tool. Ambiguous? It lists the candidates instead of guessing. |
| 🛡️ **Compile-gated edits** | An edit that introduces a new compile error is rolled back. Every mutation reports `errors=N (+D) warnings=N (+D)` — no separate `analyze` needed. |
| 🔄 **Always fresh** | A `FileSystemWatcher` plus a content comparison keeps the workspace level with the disk, so a file you just created or an edit from your IDE is already in the answer. |
| 🎨 **XAML that knows your C#** | WPF · Avalonia · WinUI · MAUI. Type-checked bindings, a workspace-wide resource graph, and renames that carry into the markup. |
| 🧩 **Razor and Blazor, resolved** | Components, parameters, bindings and routes read through the Razor source generator — plus the unknown-parameter bug that compiles clean and throws at render. |
| 🔍 **Analysis without a licence** | Compiler + every analyzer your projects already reference + dead code, down to `info` severity. No IDE, no ReSharper, no network. |
| 🧪 **Tests an agent can act on** | Counters, then each failure's message, expected/actual and **one** source frame — capped so a red suite cannot flood the context. |
| 🌲 **Parallel worktrees** | Many workspaces at once. An ambiguous request names the candidates rather than answering from the wrong checkout. |
| 🚫 **Never guesses** | `UNRESOLVED_CONTEXT`, `AmbiguousSymbol`, `SaturatedName`, `HEURISTIC` — where it cannot prove an answer, it says so. A false positive costs an agent more than no answer. |

---

## 🚀 Install

One command. No IDE, no licence, no Node, no Python, no language server, no API key, no network.

```bash
dotnet tool install -g TerseSharp
```

Register it with your agent — TerseSharp writes the config itself, you don't hand-edit JSON:

```bash
terse install                       # detects installed clients and registers with all of them
terse install --client claude-code  # or pick one: claude-code | cursor | vscode | windsurf
terse install --skill               # also install the agent skill (teaches the tool-for-built-in swaps)
terse install --guard               # also install the hook that BLOCKS Read/Grep/Edit on C# and XAML
terse doctor                        # verify SDK, MSBuild, workspace load, client registration
```

Then just work. With no arguments the server walks up from the current directory, finds your
`.sln` / `.slnx` / `.slnf` / `.csproj`, and loads it.

<details>
<summary><b>Build from source</b></summary>

```bash
git clone https://github.com/amusleh-spotware-com/terse-sharp && cd terse-sharp
dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg
dotnet tool install -g TerseSharp --add-source artifacts/nupkg --prerelease
```
</details>

<details>
<summary><b>Manual MCP config</b> (if you'd rather not run <code>terse install</code>)</summary>

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
</details>

Claude Code reads `~/.claude.json`, or `$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set —
`terse install` and `terse doctor` follow it, `--skill` lands in `$CLAUDE_CONFIG_DIR/skills` (else
`~/.claude/skills`), and `doctor` prints the config path it read.

<details>
<summary><b>🎮 Unity projects</b></summary>

Unity generates a real `.sln` with `Assembly-CSharp.csproj` and friends, so TerseSharp works on Unity
game code exactly as it does on any other solution — outlines, `find_usages`, symbol-addressed edits,
compile-gated rename across your `MonoBehaviour`s, `analyze` with whatever analyzers your project
references.

```bash
cd /path/to/UnityProject      # the folder holding the generated .sln
terse install
```

Two things to know:

- **Open the project in the Unity editor once first**, or run *Assets → Open C# Project*, so the
  `.sln` and `.csproj` files exist. TerseSharp reads them; it does not generate them.
- **Editor state is out of scope.** TerseSharp is a headless Roslyn server — it will not read your
  scene graph, inspector values, `ScriptableObject` assets or play-mode state, and it does not drive
  the editor. It answers questions about your **C# code**. For scene and asset work, use a
  Unity-specific MCP alongside it.

Regenerate the project files after adding assemblies or packages. A `.csproj` change is picked up by
the watcher on the next semantic call; `load_workspace(reload: true)` forces it.

</details>

---

## 🔒 Make your agent actually use it

> [!IMPORTANT]
> The most expensive failure mode is not a slow tool — it is an agent that has TerseSharp installed
> and reaches for `Read`, `Grep` and line-`Edit` anyway, out of habit. **Every token the server saves
> on a call the agent never makes is zero.**

Three levels, weakest to strongest. Use all three.

### 1️⃣ Ship the skill

```bash
terse install --skill
```

Costs nothing until it is needed, then teaches the whole swap table and the working rules.

### 2️⃣ Put a hard gate in your agent's instructions

Paste this at the top of `CLAUDE.md`, `AGENTS.md` or `.cursorrules`. Phrase it as a **rule with the
loopholes named** — a soft preference loses to habit every time.

```markdown
## 🚫 HARD GATE — C#/.NET goes through terse-sharp, built-ins LAST

Before EVERY `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call, answer:
**"Is the target a `.cs`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`, `.xaml` or `.axaml` file?"**
If yes → you are FORBIDDEN from the built-in. No "just this once", no "Grep is faster".

| Never | Always |
|---|---|
| `Read` a `.cs` / `.xaml`      | `get_file_outline` · `get_symbol_source` · `xaml_outline` |
| `Grep` a type or member       | `search_symbols` · `find_usages` · `find_implementations` |
| `Glob` / `ls`                 | `find_files` |
| `Edit` a `.cs`                | `replace_symbol_body` · `replace_symbol` · `add_member` · `rename_symbol` |
| create a new `.cs`            | `write_text(path, content, force: true)` — then `add_member` |
| `Edit` a `.xaml`              | `xaml_set_property` |
| `Bash: dotnet build` / `test` | `build` · `run_tests` |

**CLI text tools are built-ins too.** `grep`, `rg`, `find`, `cat`, `head`, `sed`, `awk`, `ls` do not
escape this gate because they run in a shell.

**An `ERROR` is not permission to switch toolchains.** Read the `remedy:` line and fix the call.
`AmbiguousSymbol`, `UNRESOLVED_CONTEXT` and `HEURISTIC` mean *narrow the question*, not *use Grep*.

When you do drop to a built-in, say why in the same message — a silent drop is the breach.
```

### 3️⃣ Enforce it in the harness — `terse install --guard`

Instructions can be read and then ignored; a hook cannot. This is the only level that survives a long
session, because it does not depend on the model remembering.

```bash
terse install --guard        # writes the PreToolUse hook into Claude Code's settings.json
```

That registers `terse guard` as a `PreToolUse` hook. Claude Code hands it every `Read`, `Write`,
`Edit`, `MultiEdit`, `Grep`, `Glob` and `Bash` call before it runs, and the guard **denies** the ones
that target .NET source, naming the tool to use instead:

```
$ echo '{"tool_name":"Read","tool_input":{"file_path":"src/App/OrderService.cs"}}' | terse guard
{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny",
 "permissionDecisionReason":"TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
  Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text."}}
```

What it covers, and what it deliberately does not:

| | |
|---|---|
| **Denies** | `Read`/`Write`/`Edit`/`MultiEdit`/`NotebookEdit` on `.cs`, `.razor`, `.cshtml`, `.razor.css`, `.razor.js`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`/`.slnf`, `.xaml`, `.axaml`, `.paml`, `.resx`, `.resw` · `Glob` for those · `Grep` scoped to them by `glob`, `path` or `type` · a shell text read or listing (`grep`, `rg`, `cat`, `head`, `tail`, `sed`, `awk`, `findstr`, `type`, `find`, `fd`, `ls`, `dir`, `tree`, `wc`, `nl`, plus the PowerShell forms `Get-ChildItem`, `gci`, `Get-Content`, `gc`, `Select-String`, `sls`) **naming a .NET file**, anywhere in a compound command. A denial names the matching tool family: `resx_*` for a resource file, `razor_*` for Razor markup, and for a XAML glob or shell walk it names `xaml_find`, `xaml_resolve` and `xaml_styles` **before** `find_files`, because globbing XAML is nearly always a search for a key, a name or a style |
| **Names a tool that can actually do it** | `Write`/`Edit` on a `.cs` path that **does not exist yet** names `write_text(path, content, force=true)`, because no symbol tool creates a file. Pointing a stuck agent at `replace_symbol_body` for a file that is not there is the dead end that produced a silent `edit_text force=true` fallback in 0.8.0. A relative path the hook process cannot resolve is offered creation only as the "if it does not exist yet" case, so the remedy never recommends overwriting a file that does exist |
| **Says freshness is handled** | every `.cs` **write** denial adds: a file you create or edit through `write_text` is picked up automatically — no reload, no re-`Read` to check |
| **Allows** | everything else, including plain `.css`, `.js`, `.csv` and `.csx` — matching is by **file extension** plus the `.razor.css`/`.razor.js` pair, not substring, so an ordinary stylesheet stays editable |
| **Denies** | `dotnet build`, `dotnet test`, `dotnet msbuild`, `dotnet vstest`, bare `msbuild` — anywhere in a compound command — because `build`, `run_tests`, `rerun_failed` and `list_tests` replace them |
| **Denies** | `dotnet format`, `dotnet clean` — because `format`, `cleanup fix=…`, `cleanup verify=true` and `clean` replace them, compile-gated and without the raw CLI output |
| **Allows** | `dotnet restore`, `pack`, `publish`, `run`, `tool update` — **no TerseSharp tool replaces these**, and a denial that names no alternative is just a wall |
| **Allows** | `git add OrderService.cs` — the path is mentioned, but the command is not a text read; and `ls src/App`, `find . -name "*.md"` — a listing that names no .NET file |
| **Never blocks on failure** | malformed or unexpected hook input allows the call, so a guard fault cannot wedge a session |

Re-running `install --guard` replaces only TerseSharp's own hook and leaves any other hooks in the
same matcher untouched. Remove it by deleting the `terse guard` entry from `settings.json`.

> [!TIP]
> `terse install --skill --guard` in one go: the skill teaches the swaps, the guard enforces them.

---

## 🧰 The tools

83 tools. Every response is one record per line, with an explicit `truncated`/`total` and an
`EXACT` / `HEURISTIC` tag. Paths are workspace-relative. Truncation names the parameter that narrows it.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **.NET semantics grep cannot reach** | `find_registrations` · `list_endpoints` |
| **Analyze & clean** | `analyze` · `format` · `cleanup` · `clean` · `get_diagnostics` |
| **Edit** | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Localization (`.resx`/`.resw`)** | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Files** | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Build & test** | `build` · `run_tests` · `rerun_failed` · `list_tests` |

### 🔄 Freshness — the workspace follows the disk

A loaded workspace used to be a snapshot. A file you created with `write_text`, an edit from your IDE,
a `git checkout` — none of it reached the Roslyn solution, so the next `replace_symbol` answered from
stale state **with an `EXACT` tag**. That is the response contract's worst failure: a confident wrong
answer the agent cannot detect.

It now tracks the tree:

- **A `FileSystemWatcher` per workspace** nominates changed paths. It is only a hint — state changes
  after a **content comparison**, so a dropped, duplicated or out-of-order OS event can delay a
  refresh but never corrupt one, and your own writes are naturally no-ops.
- **Sync is lazy.** Events accumulate and are drained by the next call that needs semantics. A
  `git checkout` storm costs one reload, not one per file. `read_text`, `write_text`, `edit_text`,
  `find_files`, `search_text` and `search_regex` answer from disk and skip the sync entirely.
- **A targeted check catches what the watcher drops.** Before answering about a specific file, its
  `(LastWriteTimeUtc, Length)` is compared against the last known stamp — one `FileInfo` call on
  exactly the file whose answer would otherwise be wrong. This is why `--no-watch` is still correct.
- **Doubt is a rebuild.** A changed `.csproj`/`.props`/`.targets`/`.sln`/`global.json`/`.editorconfig`,
  a `.cs` added or removed **under a project's directory**, a watcher buffer overflow or an over-cap
  pending set all reload the solution rather than guess. (A `.cs` that appears outside every project
  directory belongs to no project, so it is ignored rather than paid for.) Callers already holding a
  lease keep answering from the snapshot they started with — that answer is correct for its request,
  not stale.
- **Four generation counters, not one** — `Code`, `Project`, `Xaml`, `Resx`. A `.cs` edit must not
  invalidate the XAML graph, and a `.resx` edit must not invalidate anything Roslyn holds. A reload
  bumps `Code` and `Project` only — it rebuilds the Roslyn solution, and it tells you nothing about
  markup or resources — so a `.csproj` save does not throw away a XAML cache. They carry across a
  reload rather than restarting at zero. **Compare them for inequality, never for ordering:** they
  start again when a workspace is unloaded and loaded afresh, so "changed since I last looked" is the
  only question they answer.
- **Undo knows it was overtaken.** An external change to a file an undo snapshot covers drops that
  snapshot and every one above it, and `undo_last_change` *says so* rather than silently reverting
  someone else's work: `nothing to undo - 2 snapshot(s) were dropped after an external change to
  src/Foo.cs`.

`workspace_status` reports it in one line:

```
watch=active gen=c12/p1/x3/r0 pending=0 lastSyncMs=8 gaps=0
index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-) documents=9/128 parses=9
```

The second line is the **workspace index**. `xaml_resolve`, `xaml_validate`, `xaml_styles`,
`xaml_localization`, `xaml_find`, the `resx_*` tools, `find_registrations` and `list_endpoints` used
to walk and re-parse the whole tree on every call — thirteen sites, and `xaml_localization` did it
twice in one call. They now share one index per workspace, built once per generation and reused until
the counter above it moves, so **ask the same question again freely: the second call reads no file at
all.** When a generation does move, only the files whose `(LastWriteTimeUtc, Length)` changed are
re-parsed — a one-file edit in a 200-file tree costs one parse, not 200. When the watcher is off or
degraded the index verifies by stamp sweep before answering, so `--no-watch` is still correct. Parsed
documents live behind a bounded LRU (`documents=9/128`) because an `XDocument` costs 5-10× its file;
the per-file records most of those tools answer from are always kept, so only `xaml_find` and the XAML
sweep inside `find_usages` — which need every parsed document, not a record — re-parse beyond the cache
on a solution with more than 128 XAML files. No tool's response format changed, and
nothing about the index is printed on a response — the counters live here so proving the hit rate
costs one status call rather than tokens on every answer.

`load_workspace(reload: true)` forces a reload. `--no-watch` (or `TERSE_WATCH=0`) turns the watcher
off for constrained containers where inotify limits would make it unreliable; `terse doctor` reports
whether this platform supports file watching at all.

<details>
<summary><b>What each one replaces</b></summary>

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline` | types + members + line ranges, no bodies |
| `Read` to see one method | `get_symbol_source` | that member only |
| quoting a 200-character symbol id | `OrderService.Submit(Order)` | every reference an outline prints resolves back |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` | real references, each marked `src` or `test` |
| `Edit` a `.cs` file | `replace_symbol_body` | addressed by symbol, immune to line drift |
| creating a new `.cs` file | `write_text(path, content, force: true)` | no symbol tool creates a file; the new type resolves on the very next call |
| `Edit` a `.xaml` file | `xaml_set_property` | addressed by element, formatting preserved |
| `Read` a `.resx` file | `resx_get` | keys and values per culture; a missing translation prints `MISSING` |
| `Grep` a resource key | `resx_find` · `resx_usages` | across every family, or every C#/XAML/Razor site that names it |
| `Edit` a `.resx` file | `resx_set` · `resx_remove` · `resx_rename` | schema header, ordering, indentation, line endings and BOM preserved |
| `Read` a `.razor` / `.cshtml` file | `razor_outline` | directives, component tree and `@code` members, each component resolved to its type |
| "how do I use this component" | `razor_component` | every `[Parameter]`, which are `[EditorRequired]`, from source **or** a referenced package |
| `Edit` a `.razor` file | `razor_set_attribute` · `razor_add_element` · `razor_set_directive` | element-addressed, and the Razor generator re-runs so a broken edit is rolled back |
| eyeballing `<Card Foo="1" />` | `razor_validate` | an unknown parameter compiles clean and throws at render — nothing else catches it |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `Bash: dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` | `run_tests` | counters plus each failure's message, expected/actual and one source frame |
| `Bash: dotnet format` | `format` · `cleanup fix=all` · `cleanup verify=true` | compile-gated code fixes and a one-line verdict, never raw CLI output |
| `Bash: dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's own file locks |

</details>

### Analysis without a licence

`analyze` runs the **compiler plus every analyzer your projects already reference** — CA rules,
StyleCop, SonarAnalyzer, Roslynator, whatever is in your `PackageReference` list — down to `info` and
`hidden` severity, which a normal build hides. It reports **dead code** in the same list, so one call
covers everything. `cleanup` removes unused `using` directives, sorts what remains System-first and
reformats to your `.editorconfig`; `cleanup fix=style|analyzers|all` goes further and **applies the code fixes** of every analyzer the project references - the in-process equivalent of `dotnet format style` and `dotnet format analyzers`, compile-gated and rolled back if it breaks the build, with an `UNFIXED <id>` line for anything no fixer covers. `format verify=true` and `cleanup verify=true` replace `--verify-no-changes`: one verdict line instead of a diff. `path=` takes a file, a directory or a glob, and generated code (`obj/`, `*.g.cs`, `*.Designer.cs`) is never rewritten. All Roslyn: **no IDE, no external tool, no licence, no network.**

`clean` replaces `dotnet clean`: it deletes the `bin` and `obj` directories of the workspace or of one project and reports `projects=`, `files=` and `freedBytes=` instead of MSBuild output. Unlike `dotnet clean` it also removes `obj`, and when the loaded workspace's own MSBuild file locks block the delete it unloads, retries and reloads - the same recovery `build` uses. It only ever deletes a directory literally named `bin` or `obj` inside the workspace root, and `dryRun=true` lists what would go without touching anything. It is **not** covered by `undo_last_change`.

### Tests an agent can act on

```
2 failures (truncated=false, total=2)

passed=3 failed=2 skipped=1 total=6 durationMs=229 exitCode=1 elapsedMs=9533

FAIL Fixture.Trading.Tests.DeliberateOutcomesTests.FailsAssertion (2 ms)
  Assert.Equal() Failure: Values differ
  Expected: 4
  Actual:   5
  at tests/Fixture.Trading.Tests/DeliberateOutcomesTests.cs:26
```

Failures are capped at 20 and each message at 12 lines, so a red suite cannot flood the context. A
filter that matches nothing says so instead of looking green, and a run that produced no results at
all never reports `0 failures`.

---

## 🎨 XAML that knows about your C#

TerseSharp holds the XAML tree **and** the Roslyn compilation in one process — so it answers the two
questions no text tool can. **WPF · Avalonia (`.axaml`) · WinUI · MAUI**, dialect detected from the
markup namespace.

### Does this binding actually bind?

WPF has **no** compile-time binding check at all — a typo fails silently to debug output.
`xaml_bindings validate=true` resolves the data context from `x:DataType` or `d:DataContext`, maps the
XAML prefix through its `clr-namespace:`, and walks every path segment against the real symbol:

```
src/Views/BoundView.xaml:7   EXACT  TextBlock.Text  {Binding Symbol}           OK Symbol on Trading.OrderViewModel
src/Views/BoundView.xaml:9   EXACT  TextBlock.Text  {Binding Symbl}            ERROR no member 'Symbl'; nearest 'Symbol'
src/Views/BoundView.xaml:10  EXACT  TextBlock.Text  {Binding Selected.Symbol}  OK Selected.Symbol on Trading.OrderViewModel
```

With no data context in scope the record says `UNRESOLVED_CONTEXT` and stays `HEURISTIC`. It never
reports an error it cannot prove — a false "your binding is broken" costs more than no answer.

### Where does this resource come from?

Resolving one `{StaticResource AccentBrush}` by hand means reading `App.xaml`, then every
`MergedDictionaries` entry **in order**, then the theme dictionaries. One call instead:

```
xaml_resolve AccentBrush
2 declarations (truncated=false, total=2)

scanned=7 files
src/Views/OrderView.xaml:5    HEURISTIC  SolidColorBrush  scope=local
src/Views/Themes/Dark.xaml:4  HEURISTIC  SolidColorBrush  scope=theme
```

The same index backs `xaml_validate`: a key is reported unresolved only when it is declared in **no**
XAML file under the workspace root, so the check does not fire on every real application.

### A rename that does not silently break the UI

Renaming a code-behind handler used to leave `Click="OnSubmit"` pointing at nothing, and renaming a
bound property used to leave `{Binding Symbol}` bound to nothing — **neither is a compile error in
WPF**, so the compile gate certified a broken UI as clean. Both now travel with `rename_symbol`, and
both appear in `find_usages` so the blast radius is visible first. The rewrite happens only where an
`x:Class` or `x:DataType` **proves** the reference; anything else is listed `NOT rewritten` rather
than rewritten on a guess.

---

## 🌍 Localization without reading a single `.resx`

A 500-key `.resx` costs ~12 000 tokens to read, and a neutral + `fr` + `de` family ~36 000 — so the two
questions that matter are the two an agent cannot afford to ask. `resx_validate` answers them in one call:

```
RESX001  src/App/Strings.de.resx  Order_Submit  MISSING      no de value; neutral="Submit order"
RESX002  src/App/Strings.de.resx  Order_Count   PLACEHOLDER  neutral has {0},{1}, this culture has {0} - {1} is never filled in
RESX002  src/App/Strings.fr.resx  Order_Total   PLACEHOLDER  this culture has {2} which the neutral value does not - string.Format throws FormatException
RESX004  src/App/Strings.resx     Order_Submit  DUPLICATE    declared at lines 40, 88 - GenerateResource fails on a duplicate name
```

A placeholder mismatch is a runtime `FormatException` in one locale only — the bug class no compiler
catches. Edits are surgical: `resx_set`, `resx_remove` and `resx_rename` rewrite only the `<data>` element
they address, so the schema header, the ordering, the indentation, the line endings and the byte order mark
survive, and a rename that would leave any file malformed writes nothing at all. Typed and binary entries
are listed and passed through untouched, never rewritten. **WinForms designer resources are recognised and
excluded from the translation lint** rather than flooding it with false positives, and `.resw` families are
served by the same tools.

---

## ⚔️ Vs the alternatives

| | TerseSharp | Rider MCP | `RoslynMcpServer` | `csharp-lsp-mcp` |
|---|---|---|---|---|
| Needs a running IDE | **No** | Yes (licensed, solution open) | No | No |
| C# semantics | **Roslyn, exact** | Roslyn, exact | Roslyn, exact | via `csharp-ls` |
| Can edit / refactor | **Yes** | Yes | Partial | Rename preview |
| Compile-gated edits with rollback | **Yes** | No | No | No |
| Type-checked XAML bindings | **Yes** | Inspections only | No | No |
| XAML-aware rename | **Yes** | Partial | No | No |
| Parallel worktrees / multi-repo | **First-class** | One solution per IDE | No | No |
| Confidence tag on every result | **Yes** | No | No | No |
| E2E test per advertised tool | **Required** | — | — | — |
| Setup | one command | IDE + licence | tool install | tool install + `csharp-ls` |

---

## ⚡ How it's fast

Rider MCP's floor is structural: `agent → MCP plugin (JVM) → RD protocol → ReSharper backend`, on a
process also driving a GUI and a continuous inspection daemon. TerseSharp is
`agent → one process → Roslyn`. On top of that: the workspace is loaded once and reused, semantic
queries compile the owning project plus its dependents rather than the solution, and responses are
built as compact text rather than JSON.

## 📐 Design principles

1. **Semantic, never textual.** Queries take symbols, not byte patterns.
2. **Slices, never files.** No tool returns a whole file by default.
3. **Stable handles.** `M:Trading.OrderService.Submit(Trading.Order)` survives every edit, and
   `OrderService.Submit` resolves to it when unambiguous — an ambiguous name lists the candidates
   instead of guessing.
4. **Bounded, compact responses.** Text, not JSON. Truncation is explicit, and names the parameter
   that narrows it.
5. **Data, never prose.** No preamble, no explanation, no closing summary.
6. **Concise never means incomplete.** Truncation is always declared.
7. **Never answer what you cannot prove.** An empty result, a `(+0)` delta and an `EXACT` tag are all
   claims. Where the claim cannot be supported, the response says so.

---

## 🧩 Razor and Blazor, resolved through the compiler

The Razor compiler is a **Roslyn source generator**, so the loaded workspace already knows what every
`<Card />` in your markup is. TerseSharp reads that — and reports it at the `.razor` line, never at
the generated file under `obj/`.

### The bug nothing else catches

An attribute that matches no `[Parameter]` compiles **clean** and throws
`InvalidOperationException` the first time the component renders. `razor_validate` is the only static
answer:

```
razor_validate solution
6 findings (truncated=false, total=6) - narrow with rules=

RZR002  src/App/Components/Home.razor:6   Card.Bogus     UNKNOWN_PARAMETER  Card has no [Parameter] with that name - InvalidOperationException at render
RZR001  src/App/Components/Home.razor:8   MudButton      UNKNOWN_COMPONENT  resolves to no component - it renders as a plain HTML tag
RZR006  src/App/Components/Legacy.razor:1 /order/{Id:int} DUPLICATE_ROUTE   also declared by Components/Detail.razor - AmbiguousMatchException on navigation
```

`RZR003` (missing `[EditorRequired]`), `RZR004` (a `@bind` with no setter), `RZR005` (a route
parameter with no property), `RZR007` (a mistyped `@ref`), `RZR009` (an `@inject` nothing registers)
and `RZR010` (markup that will not parse) complete the set.

### Edits the generator has to accept

`razor_set_attribute`, `razor_add_element`, `razor_remove_element` and `razor_set_directive` address
an element by the path `razor_outline` prints, keep the file's formatting, re-parse the result, then
**re-run the Razor generator** and compare the error count. An edit that breaks the build is rolled
back with the error at its `.razor` line — the same contract C# edits already have, measured at
~170 ms per regeneration.

### One call to learn a component

```
razor_component Badge
4 parameters (truncated=false, total=4)
Fixture.Blazor.Components.Badge  EXACT  src/App/Components/Badge.razor  base=ComponentBase

Kind          string                         [Parameter, EditorRequired]
Count         int                            [Parameter]
ChildContent  RenderFragment                 [Parameter]
OnDismiss     EventCallback<MouseEventArgs>  [Parameter]
routes=-   captureUnmatched=False
```

It answers for a component from a **referenced package** too, where there is no `.razor` to read at
all.

### The C# tools reach into `@code`

`replace_symbol_body`, `replace_symbol`, `delete_symbol` and `add_member` recognise a member declared
inside a `.razor` and edit the Razor source through the generator's mapping, so the code half of a
component needs no separate tool. `rename_symbol` on a component renames the **file** — a Blazor
class name comes from its file name — along with its `.razor.cs`, `.razor.css` and `.razor.js`
siblings and every `<Card …>` in markup.

## 📋 Status

| Area | State |
|---|---|
| Workspace loading, multi-workspace, worktree awareness | ✅ |
| Symbol search, outlines, usages, implementations | ✅ |
| Symbol-addressed edits, dryRun, compile gate, rollback, diagnostic deltas | ✅ |
| Solution-wide rename, diagnostics | ✅ |
| Build, tests, non-C# file and text tools | ✅ |
| `terse install` / `uninstall` / `doctor` / `--skill` | ✅ |
| Extract interface, move type, change signature, undo | ✅ |
| Project, solution and package editing, full `.slnx` support | ✅ |
| `analyze` / `format` / `cleanup`, Roslyn-only | ✅ |
| `cleanup fix=style\|analyzers\|all` code fixes, `verify` mode, glob scope, `clean` | ✅ |
| XAML outline, names, resources, bindings, validation, search | ✅ |
| XAML resource graph, typed binding validation, dialect fixtures | ✅ |
| `xaml_codebehind`, `xaml_set_property`, XAML-aware `rename_symbol` and `find_usages` | ✅ |
| Short symbol references, name resolution, truncation steering | ✅ |
| Token budget harness | ✅ |
| `explore_symbol`, `impact_of`, `find_registrations`, `list_endpoints` | ✅ |
| XAML element insert/remove, dead-resource detection, `terse install --guard` | ✅ |
| `xaml_styles` (implicit + keyed + `BasedOn` chain), `xaml_localization` (`x:Uid`→resx) | ✅ |
| `.resx`/`.resw` listing, cross-culture read, search, key usages, surgical edit, rename, `RESX001`–`RESX009` lint | ✅ |
| Razor/Blazor outline, component API, bindings, validation, element and directive edits | ✅ |
| Razor-aware `find_usages`, `search_symbols`, `list_endpoints`, diagnostics mapped back to the `.razor` line | ✅ |
| `@code` members edited through `replace_symbol_body` / `add_member`, component rename incl. its files | ✅ |
| File watcher, lazy external-change sync, targeted stamp check | ✅ |
| Per-kind generation counters (Code / Project / Xaml / Resx), carried across a reload | ✅ |
| Undo provenance: a snapshot overtaken by an external change is dropped and reported | ✅ |
| `load_workspace(reload)`, `--no-watch` / `TERSE_WATCH=0`, `doctor` watcher line | ✅ |
| Per-workspace XAML / resx / DI index, memoized per generation, incremental, bounded | ✅ |
| Shared warm workspace daemon across processes | 🔜 |
| Content-addressed (hashed) index, cross-session persistence, trigram search | 🔜 |

Changes are recorded in [CHANGELOG.md](CHANGELOG.md). Versioning and the release pipeline are
described in [RELEASING.md](RELEASING.md).

## 🙅 What it deliberately doesn't do

- **Database / SQL tools** — no C# relevance, no token saving, and it would put credential storage and
  arbitrary SQL in a code server.
- **Debugging and profiling** — a debugger needs a live session and a profiler needs a trace host;
  both are separate products, and `dotnet-trace` and your IDE already do them well.
- **Live XAML visual-tree inspection** — needs a running app. Avalonia's DevTools MCP does it properly.
- **Unity / Unreal *editor* tools** — scene graph, inspector, play-mode state. Those read a live
  editor, which a headless process cannot, and six broken tools are worse than none. The **C# code**
  in a Unity project is fully supported — see the Unity note under [Install](#-install).
- **Translation formats other than `.resx`/`.resw`** — no `.xlf`, no `.restext`, no CSV round trip and no
  machine translation. The resx tools address keys and values; moving strings between systems is a
  localization pipeline's job.
- **Razor formatting** — the Razor formatter lives in the (unpublished) Razor tooling stack, not in
  Roslyn. `format` and `cleanup` stay C#-only; `razor_*` edits preserve the formatting already there.
- **Commit / push** — git access is read-only. Your agent already has git.
- **Arbitrary shell execution** — only `dotnet build` / `dotnet test`, deadlined and killed on timeout.
- **VB.NET / F# language tools** — C# first; they load without breaking navigation and language tools
  refuse them with a clear message rather than guessing.

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Two rules that aren't negotiable: **a tool without an E2E
test isn't done**, and **a tool that doesn't beat the built-in it replaces doesn't ship**.

Security policy: [SECURITY.md](SECURITY.md).

## 📄 License

MIT Licensed. See [LICENSE](LICENSE).

<p align="center">
  <sub>Built on <a href="https://github.com/dotnet/roslyn">Roslyn</a> and the
  <a href="https://github.com/modelcontextprotocol/csharp-sdk">MCP C# SDK</a>.</sub>
</p>
