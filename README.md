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
  <img src="https://img.shields.io/badge/tools-64-26C281.svg" alt="64 tools"/>
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
| 🎨 **XAML that knows your C#** | WPF · Avalonia · WinUI · MAUI. Type-checked bindings, a workspace-wide resource graph, and renames that carry into the markup. |
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

Regenerate the project files after adding assemblies or packages, then call `load_workspace` again (or
restart the server) so the new projects are picked up.

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
| `Edit` a `.xaml`              | `xaml_set_property` |
| `Bash: dotnet build` / `test` | `build` · `run_tests` |

**CLI text tools are built-ins too.** `grep`, `rg`, `find`, `cat`, `head`, `sed`, `awk`, `ls` do not
escape this gate because they run in a shell.

**An `ERROR` is not permission to switch toolchains.** Read the `remedy:` line and fix the call.
`AmbiguousSymbol`, `UNRESOLVED_CONTEXT` and `HEURISTIC` mean *narrow the question*, not *use Grep*.

When you do drop to a built-in, say why in the same message — a silent drop is the breach.
```

### 3️⃣ Enforce it in the harness

Instructions can be read and then ignored; a hook cannot. Claude Code can **block** the call rather
than ask nicely — a `PreToolUse` hook in `.claude/settings.json` that denies `Read`/`Grep`/`Edit` on
C# paths and names the tool to use instead. This is the only level that survives a long session,
because it does not depend on the model remembering.

---

## 🧰 The tools

64 tools. Every response is one record per line, with an explicit `truncated`/`total` and an
`EXACT` / `HEURISTIC` tag. Paths are workspace-relative. Truncation names the parameter that narrows it.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **.NET semantics grep cannot reach** | `find_registrations` · `list_endpoints` |
| **Analyze & clean** | `analyze` · `format` · `cleanup` · `get_diagnostics` |
| **Edit** | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Files** | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Build & test** | `build` · `run_tests` · `rerun_failed` · `list_tests` |

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
| `Edit` a `.xaml` file | `xaml_set_property` | addressed by element, formatting preserved |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `Bash: dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` | `run_tests` | counters plus each failure's message, expected/actual and one source frame |

</details>

### Analysis without a licence

`analyze` runs the **compiler plus every analyzer your projects already reference** — CA rules,
StyleCop, SonarAnalyzer, Roslynator, whatever is in your `PackageReference` list — down to `info` and
`hidden` severity, which a normal build hides. It reports **dead code** in the same list, so one call
covers everything. `cleanup` removes unused `using` directives, sorts what remains System-first and
reformats to your `.editorconfig`. All Roslyn: **no IDE, no external tool, no licence, no network.**

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
| XAML outline, names, resources, bindings, validation, search | ✅ |
| XAML resource graph, typed binding validation, dialect fixtures | ✅ |
| `xaml_codebehind`, `xaml_set_property`, XAML-aware `rename_symbol` and `find_usages` | ✅ |
| Short symbol references, name resolution, truncation steering | ✅ |
| Token budget harness | ✅ |
| `explore_symbol`, `impact_of`, `find_registrations`, `list_endpoints` | ✅ |
| XAML element insert/remove, dead-resource detection, `terse install --guard` | ✅ |
| `xaml_styles` (implicit + keyed + `BasedOn` chain), `xaml_localization` (`x:Uid`→resx) | ✅ |
| Shared warm workspace daemon across processes | 🔜 |
| Content-addressed index, trigram search, file watcher | 🔜 |

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
