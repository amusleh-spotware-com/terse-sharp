<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>Your agent stops reading whole C# files.</b><br/>
  A Roslyn-powered <a href="https://modelcontextprotocol.io">MCP</a> server that lets a coding agent
  navigate, read, edit and refactor a .NET solution <b>semantically</b> — no <code>Read</code>, no
  <code>Grep</code>, no line-number <code>Edit</code>, no shelling out.
</p>

<p align="center">
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/release.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/release.yml/badge.svg" alt="Release"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/v/TerseSharp.svg?logo=nuget&label=NuGet" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/dt/TerseSharp.svg?logo=nuget&label=downloads" alt="Downloads"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="MIT"/></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet&logoColor=white" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/Roslyn-5.6-512BD4.svg" alt="Roslyn 5.6"/>
  <img src="https://img.shields.io/badge/MCP-C%23_SDK_2.0-8A2BE2.svg" alt="MCP C# SDK 2.0"/>
  <a href="CONTRIBUTING.md"><img src="https://img.shields.io/badge/PRs-welcome-brightgreen.svg" alt="PRs welcome"/></a>
</p>

<p align="center">
  <a href="#-why">Why</a> ·
  <a href="#-install">Install</a> ·
  <a href="#-the-tools">Tools</a> ·
  <a href="#-the-numbers">Numbers</a> ·
  <a href="#-vs-the-alternatives">Comparison</a> ·
  <a href="#-how-its-fast">How it's fast</a> ·
  <a href="#-status">Status</a> ·
  <a href="RELEASING.md">Releasing</a>
</p>

---

## 🤔 Why

An agent working a C# solution spends most of its context on the wrong shape of data:

```
"What's on OrderService?"        →  Read OrderService.cs           →  ~6,000 tokens
"Who calls Submit?"              →  Grep "Submit" + 3 more Reads   →  ~4,000 tokens
"Rename Submit to SubmitAsync"   →  grep + 9 context-echoing Edits →  ~5,000 tokens, misses the interface
"Fix the build"                  →  dotnet build, full MSBuild spew → ~8,000 tokens
```

Roslyn already knows all four answers **semantically**. TerseSharp hands them over in the shape the
agent needs — a signature list instead of a file, 12 real call sites instead of 47 string matches, a
solution-wide rename instead of a regex sweep, deduplicated diagnostics instead of build logs.

**Prime directive: save tokens, increase speed.** Every design decision is settled by that rule.

## 🚀 Install

One command. No IDE, no licence, no Node, no Python, no language server, no API key, no network.

```bash
dotnet tool install -g TerseSharp
```

Register it with your agent — TerseSharp writes the config itself, you don't hand-edit JSON:

```bash
terse install                       # detects installed clients and registers with all of them
terse install --client claude-code  # or pick one: claude-code | cursor | vscode | windsurf
terse install --skill               # also install the agent skill (teaches tool-for-built-in swaps)
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

## 🔒 Making your agent actually use it

The most expensive failure mode is not a slow tool — it is an agent that has TerseSharp installed and
reaches for `Read`, `Grep` and line-`Edit` anyway out of habit. Every token the server saves on a call
the agent never makes is zero. Three levels, weakest to strongest:

**1. Ship the skill** (teaches the swaps, costs nothing until it is needed):

```bash
terse install --skill
```

**2. Write the rule into the agent's instructions.** Put a gate at the top of your `CLAUDE.md`,
`AGENTS.md` or `.cursorrules` — phrased as a hard rule with the loopholes named, because a soft
preference loses to habit:

```markdown
## 🚫 HARD GATE — C#/.NET goes through terse-sharp, built-ins LAST

Before EVERY `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call, answer:
**"Is the target a `.cs`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`, `.xaml` or `.axaml` file?"**
If yes → you are FORBIDDEN from the built-in. No "just this once", no "Grep is faster".

| Never | Always |
|---|---|
| `Read` a `.cs` / `.xaml` | `get_file_outline` · `get_symbol_source` · `xaml_outline` |
| `Grep` a type or member | `search_symbols` · `find_usages` · `find_implementations` |
| `Glob` / `ls` | `find_files` |
| `Edit` a `.cs` | `replace_symbol_body` · `replace_symbol` · `add_member` · `rename_symbol` |
| `Bash: dotnet build` / `test` | `build` · `run_tests` |

**CLI text tools are built-ins too.** `grep`, `rg`, `find`, `cat`, `head`, `sed`, `awk`, `ls` do not
escape this gate because they run in a shell.

A tool returning `ERROR` once is not a licence to switch toolchains — read the `remedy:` line and fix
the call. `AmbiguousSymbol`, `UNRESOLVED_CONTEXT` and `HEURISTIC` mean *narrow the question*, not
*fall back to Grep*.

When you do drop to a built-in, say why in the same message — a silent drop is the breach.
```

**3. Enforce it in the harness.** Claude Code can *block* the call rather than ask nicely — a
`PreToolUse` hook in `.claude/settings.json` that denies `Read`/`Grep`/`Edit` on C# paths and names
the tool to use instead. That is the only level that survives a long session, because it does not
depend on the model remembering.

## 🧰 The tools

54 tools. Every response is one record per line, with an explicit `truncated`/`total` and an
`EXACT` (Roslyn-resolved) or `HEURISTIC` (text/index) tag. Paths are workspace-relative.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` |
| **Analyze & clean** | `analyze` · `format` · `cleanup` · `get_diagnostics` |
| **Edit** | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_bindings` · `xaml_validate` · `xaml_find` |
| **Files** | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Build & test** | `build` · `run_tests` · `rerun_failed` · `list_tests` |

### Analysis without a licence

`analyze` runs the **compiler plus every analyzer your projects already reference** — the CA rules,
StyleCop, SonarAnalyzer, Roslynator, whatever is in your `PackageReference` list — down to `info`
and `hidden` severity, which a normal build hides. It also reports **dead code** in the same list —
unreferenced private members as `TERSE001`, plus the compiler's own unused-field and unreachable-code
hints — so one call covers everything. `cleanup` removes unused `using` directives, sorts what
remains System-first and reformats to your `.editorconfig`. All of it is Roslyn: **no IDE, no
external tool, no licence, no network.**

### XAML that knows about your C#

TerseSharp holds the XAML tree **and** the Roslyn compilation in one process, so it can answer the two
questions a text tool cannot.

**Where does this resource come from?** Resolving one `{StaticResource AccentBrush}` by hand means
reading `App.xaml`, then every `MergedDictionaries` entry in order, then the theme dictionaries — and
order decides the winner, so you cannot stop at the first hit:

```
xaml_resolve AccentBrush
2 declarations (truncated=false, total=2)

scanned=7 files
src/Views/OrderView.xaml:5  HEURISTIC  SolidColorBrush  scope=local
src/Views/Themes/Dark.xaml:4  HEURISTIC  SolidColorBrush  scope=theme
```

The same index backs `xaml_validate`: a key is reported unresolved only when it is declared in **no**
XAML file under the workspace root, so the check does not fire on every real application.

**Does this binding actually bind?** WPF has no compile-time binding check at all — a typo fails
silently to debug output. `xaml_bindings validate=true` resolves the data context from `x:DataType`
(Avalonia, MAUI, WinUI) or `d:DataContext="{d:DesignInstance …}"` (WPF), maps the XAML prefix through
its `clr-namespace:`/`using:` declaration, and walks each path segment against the real symbol:

```
src/Views/BoundView.xaml:7   EXACT  TextBlock.Text  {Binding Symbol}           OK Symbol on Fixture.Trading.Views.OrderViewModel
src/Views/BoundView.xaml:9   EXACT  TextBlock.Text  {Binding Symbl}            ERROR no member 'Symbl' of 'Symbl' on …OrderViewModel; nearest 'Symbol'
src/Views/BoundView.xaml:10  EXACT  TextBlock.Text  {Binding Selected.Symbol}  OK Selected.Symbol on …OrderViewModel
```

With no data context in scope the record says `UNRESOLVED_CONTEXT` and stays `HEURISTIC`. It never
reports an error it cannot prove — a false "your binding is broken" costs more than no answer.

Dialects are detected from the real markup namespace (`https://github.com/avaloniaui`,
`.../dotnet/2021/maui`, the WinUI `using:` prefix form, WPF), with a fixture per dialect in CI.

### Tests an agent can act on

`run_tests` parses the run's TRX report, so a red run comes back with the counters and, per failure,
the exception or assertion message, the expected and actual values, and **one** stack frame —
workspace-relative — instead of a stack trace:

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

| Goal | Call |
|---|---|
| Whole solution | `run_tests` |
| One project | `run_tests project="tests/Unit/Unit.csproj"` |
| One test | `run_tests test="Ns.OrderTests.Submits"` — substring match, so check `total=`; a parameterized case runs its whole theory |
| One class or namespace | `run_tests test="Ns.OrderTests"` |
| Raw VSTest expression | `run_tests filter="Category=Fast"` |
| Skip the rebuild | `run_tests noBuild=true` |
| Only what just failed | `rerun_failed` |
| Names without running | `list_tests contains="Order"` |
| Slowest tests | `run_tests slowest=10` |

**What each one replaces**

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline` | types + members + line ranges, no bodies |
| `Read` to see one method | `get_symbol_source` | that member only |
| quoting a 200-character symbol id | `OrderService.Submit(Order)` | every reference an outline prints resolves back; ids are kept where a name cannot address the member |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` | real references, each naming the member it sits in and whether it is `src` or `test` |
| `Edit` a `.cs` file | `replace_symbol_body` | addressed by symbol id, immune to line drift |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs |
| `Bash: dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` | `run_tests` | counters plus each failure's message, expected/actual and one source frame |

### Safety

- **Symbol-addressed edits.** `replace_symbol_body(symbolId, body)` — no `old_string` echo, no line
  numbers to drift.
- **`dryRun` on every mutation** returns the unified diff and writes nothing.
- **Compile-gated.** An edit that introduces a *new* compile error is rolled back and the error
  returned. `allowErrors: true` opts out when you're mid-refactor on purpose.
- **Diff-only responses.** Mutations return the diff, a changed-line count and
  `errors=N (+D) warnings=N (+D)` for the changed projects and their dependents — never the file. On
  `dryRun` that makes the preview say whether the edit would break the build, so no separate `analyze`
  call is needed after an edit.
- **Workspace containment.** Every path is compared by whole path segment: root `C:\repo` does not
  contain `C:\repoEvil`.
- **`--read-only`** makes every mutating tool refuse with `ERROR ReadOnly` and touch nothing. (They
  are still advertised in `tools/list`; hiding them there is planned.)

### Parallel worktrees

Run several agents at once across several git worktrees of one repo, and across unrelated repos —
one server holding many workspaces (LRU, default 4), or many processes, or both. Every answer names
its worktree and branch, and an ambiguous request returns `AMBIGUOUS_WORKSPACE` listing the
candidates **instead of guessing** — answering from the wrong checkout is the one failure an agent
cannot detect.

## 📊 The numbers

*Asserted by the token-budget suite in CI, not estimated.*

| Question | With built-in tools | With TerseSharp | Target |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → ~6,000 tok | `get_type_outline` → ~450 tok | **13×** |
| …and the outline itself | ids were ~60% of every member line | short references, `ids=full` opts back in | **~2×** |
| Who calls this method? | `Grep` + follow-ups → ~4,000 tok | `find_usages` → ~200 tok | **20×** |
| Rename across the solution | ~5,000 tok, misses interface impls | `rename_symbol` → ~150 tok, correct | **30×** |
| Why is the build red? | ~8,000 tok of MSBuild output | `build` → ~600 tok | **13×** |
| 2 failures out of 312 tests | full test output | 2 failures + assertion lines | **10×** |

## ⚔️ Vs the alternatives

| | TerseSharp | Rider MCP | `RoslynMcpServer` | `csharp-lsp-mcp` |
|---|---|---|---|---|
| Needs a running IDE | **No** | Yes (licensed, solution open) | No | No |
| C# semantics | **Roslyn, exact** | Roslyn, exact | Roslyn, exact | via `csharp-ls` |
| Can edit / refactor | **Yes** | Yes | Partial | Rename preview |
| Compile-gated edits with rollback | **Yes** | No | No | No |
| Parallel worktrees / multi-repo | **First-class** | One solution per IDE | No | No |
| Confidence tag on every result | **Yes** | No | No | No |
| E2E test per advertised tool | **Required** | — | — | — |
| Setup | one command | IDE + licence | tool install | tool install + `csharp-ls` |

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
   `OrderService.Submit` resolves to it when it is unambiguous — an ambiguous name lists the
   candidates instead of guessing.
4. **Bounded, compact responses.** Text, not JSON. Truncation is explicit, and says which parameter
   narrows it.
5. **Data, never prose.** No preamble, no explanation, no closing summary.
6. **Concise never means incomplete.** Truncation is always declared.

## 📋 Status

| Area | State |
|---|---|
| Workspace loading, multi-workspace, worktree awareness | ✅ |
| Symbol search, outlines, usages, implementations | ✅ |
| Symbol-addressed edits, dryRun, compile gate, rollback | ✅ |
| Solution-wide rename, diagnostics | ✅ |
| Build, tests, non-C# file and text tools | ✅ |
| `terse install` / `uninstall` / `doctor` / `--skill` | ✅ |
| Extract interface, move type, change signature, undo | ✅ |
| Project, solution and package editing, full `.slnx` support | ✅ |
| `analyze` (diagnostics + analyzers + dead code) / `format` / `cleanup`, Roslyn-only | ✅ |
| XAML outline, names, resources, bindings, validation, search | ✅ |
| XAML resource graph (`xaml_resolve`), typed binding validation, dialect fixtures | ✅ |
| XAML structured edits, code-behind bridge, XAML-aware rename and `find_usages` | 🔜 |
| Token budget harness | ✅ |
| Content-addressed index, trigram search, file watcher | 🔜 |

Changes are recorded in [CHANGELOG.md](CHANGELOG.md). Versioning and the release pipeline are
described in [RELEASING.md](RELEASING.md).

## 🙅 What it deliberately doesn't do

- **Database / SQL tools** — DataGrip functionality bundled into Rider. No C# relevance, no token
  saving, and it would put credential storage and arbitrary SQL in a code server.
- **Debugging and profiling** — a debugger needs a live session and a profiler needs a trace host;
  both are separate products, and `dotnet-trace` and your IDE already do them well.
- **Unity / Unreal editor tools** — they read a live editor's state. A headless process cannot, and
  six broken tools are worse than none.
- **Commit / push** — git access is read-only. Your agent already has git.
- **Arbitrary shell execution** — only `dotnet build` / `dotnet test`, deadlined and killed on
  timeout. Your agent already has a shell.
- **VB.NET / F# language tools** — C# first; they load without breaking navigation and language
  tools refuse them with a clear message rather than guessing.

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
