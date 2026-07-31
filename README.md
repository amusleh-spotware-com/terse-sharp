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

> [!NOTE]
> **v0.2.2 — 51 tools working end to end.** Verified by **132 tests (39 unit + 93 E2E)**, where every
> E2E test drives a real server process over the real stdio transport against a real solution and
> asserts response values, and a token-budget suite asserts the response sizes below.
> **Not yet built:** the content-addressed index, trigram search and file watcher.

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

## 🧰 The tools

51 tools. Every response is one record per line, with an explicit `truncated`/`total` and an
`EXACT` (Roslyn-resolved) or `HEURISTIC` (text/index) tag.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` |
| **Analyze & clean** | `analyze` · `format` · `cleanup` · `get_diagnostics` |
| **Edit** | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_bindings` · `xaml_validate` · `xaml_find` |
| **Files** | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Build** | `build` · `run_tests` |

### Analysis without a licence

`analyze` runs the **compiler plus every analyzer your projects already reference** — the CA rules,
StyleCop, SonarAnalyzer, Roslynator, whatever is in your `PackageReference` list — down to `info`
and `hidden` severity, which a normal build hides. It also reports **dead code** in the same list —
unreferenced private members as `TERSE001`, plus the compiler's own unused-field and unreachable-code
hints — so one call covers everything. `cleanup` removes unused `using` directives, sorts what
remains System-first and reformats to your `.editorconfig`. All of it is Roslyn: **no IDE, no
external tool, no licence, no network.**

**What each one replaces**

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline` | types + members + line ranges, no bodies |
| `Read` to see one method | `get_symbol_source` | that member only |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` | real references; no comments, no string matches |
| `Edit` a `.cs` file | `replace_symbol_body` | addressed by symbol id, immune to line drift |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs |
| `Bash: dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` | `run_tests` | failures only; a green run is one line |

### Safety

- **Symbol-addressed edits.** `replace_symbol_body(symbolId, body)` — no `old_string` echo, no line
  numbers to drift.
- **`dryRun` on every mutation** returns the unified diff and writes nothing.
- **Compile-gated.** An edit that introduces a *new* compile error is rolled back and the error
  returned. `allowErrors: true` opts out when you're mid-refactor on purpose.
- **Diff-only responses.** Mutations return the diff and a changed-line count, never the file.
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
3. **Stable handles.** `M:Trading.OrderService.Submit(Trading.Order)` survives every edit.
4. **Bounded, compact responses.** Text, not JSON. Explicit truncation.
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
