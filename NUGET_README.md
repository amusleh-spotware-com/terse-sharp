# TerseSharp

### Your agent stops reading whole C# files.

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server that lets a coding agent navigate,
read, edit and refactor a .NET solution **semantically** — no `Read`, no `Grep`, no line-number
`Edit`, no shelling out. **72 tools. One install. No IDE, no licence, no network.**

[![CI](https://img.shields.io/github/actions/workflow/status/amusleh-spotware-com/terse-sharp/ci.yml?branch=main&label=CI)](https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

---

## The same four questions, before and after

| Question | Built-in tools | TerseSharp |
| --- | --- | --- |
| What's on this 2,000-line type? | `Read` → **~6,000 tokens** | `get_type_outline` → **~450** |
| Who calls this method? | `Grep` + follow-up reads → **~4,000** | `find_usages` → **~200** |
| Rename it across the solution | ~5,000 tokens, **misses the interface** | `rename_symbol` → **~150**, correct |
| Why is the build red? | **~8,000 tokens** of MSBuild spew | `build` → **~600** |

Roslyn already knows all four answers. TerseSharp hands them over in the shape the agent needs: a
signature list instead of a file, real call sites instead of string matches, a solution-wide rename
instead of a regex sweep, deduplicated diagnostics instead of build logs.

---

## 🎨 XAML that knows about your C#

TerseSharp holds the XAML tree **and** the Roslyn compilation in one process — so it answers the two
questions no text tool can. **WPF · Avalonia (`.axaml`) · WinUI · MAUI**, dialect detected from the
markup namespace.

**Does this binding actually bind?** WPF has *no* compile-time binding check at all — a typo fails
silently to debug output. `xaml_bindings validate=true` resolves the data context from `x:DataType`
or `d:DataContext`, maps the XAML prefix through its `clr-namespace:`, and walks every path segment
against the real symbol:

```
BoundView.xaml:7   EXACT  TextBlock.Text  {Binding Symbol}    OK Symbol on OrderViewModel
BoundView.xaml:9   EXACT  TextBlock.Text  {Binding Symbl}     ERROR no member 'Symbl'; nearest 'Symbol'
BoundView.xaml:10  EXACT  TextBlock.Text  {Binding Sel.Symbol} OK Sel.Symbol on OrderViewModel
```

**Where does this resource come from?** One call instead of reading `App.xaml` and every merged
dictionary in order:

```
xaml_resolve AccentBrush
Views/OrderView.xaml:5      SolidColorBrush  scope=local
Views/Themes/Dark.xaml:4    SolidColorBrush  scope=theme
```

Plus: **`rename_symbol` rewrites XAML too** — rename a code-behind handler and the `Click="…"` follows;
rename a bound property and `{Binding …}` follows, but only where an `x:Class` or `x:DataType` proves
it. `xaml_set_property` edits an attribute in place without reformatting the file.
`xaml_codebehind`, `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_validate`, `xaml_find`.

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

**🔒 Make it stick.** The most expensive failure mode is an agent that has TerseSharp installed and
reaches for `Read`/`Grep` anyway — every token the server saves on a call the agent never makes is
zero. `terse install --guard` registers `terse guard` as a Claude Code `PreToolUse` hook that
**denies** the built-in and names the tool to use instead:

| | |
| --- | --- |
| **Denied** | `Read`/`Write`/`Edit`/`MultiEdit` on `.cs`, `.razor`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`, `.xaml`, `.axaml`, `.resx`, `.resw` · `Glob`/`Grep` scoped to them · `grep`/`cat`/`sed`/`rg` on them · `dotnet build`, `dotnet test`, `msbuild`, `vstest`, `dotnet format`, `dotnet clean` — anywhere in a compound command. A resource file is redirected to `resx_get`/`resx_find`/`resx_set` |
| **Allowed** | `.css`, `.csv`, `.cshtml`, `.csx` — matching is by file **extension**, not substring, so Blazor and MAUI repos keep working |
| **Denied** | `dotnet format`, `dotnet clean` — `format`, `cleanup fix=…`, `cleanup verify=true` and `clean` replace them |
| **Allowed** | `dotnet restore`, `pack`, `publish`, `run`, `tool` — no TerseSharp tool replaces these, and a denial that names no alternative is a wall |
| **Never blocks on failure** | malformed hook input allows the call, so a guard fault cannot wedge a session |

Pair it with `--skill`: the skill teaches the swaps, the guard enforces them.

**🎮 Unity:** works on Unity game code too — Unity generates a real `.sln` with
`Assembly-CSharp.csproj`, so outlines, `find_usages`, symbol-addressed edits and compile-gated rename
across your `MonoBehaviour`s all work. Open the project in the editor once so the project files exist.
Scene graph, inspector values and play-mode state are out of scope: TerseSharp answers questions about
your **C# code**, not the editor.

With no arguments the server walks up from the current directory, finds your `.sln` / `.slnx` /
`.slnf` / `.csproj`, and loads it.

Claude Code reads `~/.claude.json`, or `$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set —
`terse install` and `terse doctor` follow it, `--skill` lands in `$CLAUDE_CONFIG_DIR/skills` (else
`~/.claude/skills`), and `doctor` prints the config path it read.

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

## What each tool replaces

| Instead of | Use | Why |
| --- | --- | --- |
| `Read` a `.cs` file | `get_file_outline` | types + members + line ranges, no bodies |
| `Read` to see one method | `get_symbol_source` | that member only |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` | real references, one line per file with a `src`/`test` marker; `containers=true` also names the member each usage sits in |
| `Edit` a `.cs` file | `replace_symbol_body` | addressed by symbol id, immune to line drift |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs |
| `Read` a `.resx` file | `resx_get` | keys and values per culture; a missing translation prints `MISSING` |
| `Grep` a resource key | `resx_find` · `resx_usages` | across every family, or every C#/XAML/Razor site that names it |
| `Edit` a `.resx` file | `resx_set` · `resx_remove` · `resx_rename` | schema header, ordering, indentation, line endings and BOM preserved |
| `dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `dotnet test` | `run_tests` | counters plus each failure's message, expected/actual and one source frame |
| `dotnet format` | `format`, `cleanup fix=all`, `cleanup verify=true` | compile-gated code fixes and a one-line verdict, never raw CLI output |
| `dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's file locks |

## The 73 tools

Every response is one record per line, with an explicit `truncated`/`total` and an `EXACT` or
`HEURISTIC` tag. Paths are workspace-relative.

- **Workspace** — `load_workspace`, `workspace_status`, `list_workspaces`, `unload_workspace`, `list_projects`
- **Navigation** — `search_symbols`, `get_symbol`, `get_file_outline`, `get_type_outline`, `get_symbol_source`, `find_usages`, `find_implementations`, `explore_symbol`, `impact_of`
- **.NET semantics grep cannot reach** — `find_registrations` (DI: open generics, factories, `Add*` extensions), `list_endpoints` (ASP.NET Core `Map*`)
- **Analyze & clean** — `analyze`, `format`, `cleanup`, `clean`, `get_diagnostics`
- **Edit** — `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`
- **Refactor** — `extract_interface`, `move_type_to_file`, `move_type_to_namespace`, `change_signature`, `undo_last_change`
- **Projects & solutions** — `solution_projects`, `solution_add_project`, `solution_remove_project`, `project_create`, `project_properties`, `project_set_property`, `project_add_reference`, `project_remove_reference`, `package_list`, `package_add`, `package_remove`
- **XAML** — `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_resolve`, `xaml_styles`, `xaml_bindings`, `xaml_validate`, `xaml_find`, `xaml_codebehind`, `xaml_localization`, `xaml_set_property`, `xaml_add_element`, `xaml_remove_element`
  — `xaml_resolve` reports every declaration of a resource key across the workspace with its scope, and
  `xaml_bindings validate=true` checks each binding path against the `x:DataType` or `d:DataContext`
  type resolved through Roslyn
- **Localization (`.resx`/`.resw`)** — `resx_files`, `resx_get`, `resx_find`, `resx_usages`, `resx_set`, `resx_remove`, `resx_rename`, `resx_validate`
  — `resx_get` prints every key with its value per culture and `MISSING` where a translation is absent,
  `resx_validate` reports missing translations, placeholder mismatches, duplicate names, orphans, empty
  values and a stale designer, and the writers rewrite only the `<data>` element they address, keeping the
  schema header, ordering, indentation, line endings and byte order mark intact
- **Files** — `read_text`, `write_text`, `edit_text`, `find_files`, `search_text`, `search_regex`
- **Build & test** — `build`, `run_tests`, `rerun_failed`, `list_tests`

### Analysis without a licence

`analyze` runs the compiler plus every analyzer your projects already reference - CA rules,
StyleCop, SonarAnalyzer, Roslynator, anything in your `PackageReference` list - down to `info` and
`hidden` severity, which a normal build hides. It also reports dead code in the same list -
unreferenced private members as `TERSE001`, plus the compiler's unused-field and unreachable-code
hints - so one call covers everything. `cleanup` removes unused `using` directives, sorts what
remains System-first and reformats to your `.editorconfig`. `cleanup fix=style|analyzers|all` also applies the code fixes of every analyzer the project references - the in-process equivalent of `dotnet format style` and `dotnet format analyzers` - compile-gated, rolled back if it breaks the build, and reporting `UNFIXED <id>` for anything no fixer covers. `format verify=true` and `cleanup verify=true` replace `--verify-no-changes` with a one-line verdict, `path=` takes a file, a directory or a glob, and generated code is never rewritten. `clean` replaces `dotnet clean`: it deletes `bin` and `obj` and reports `projects=`, `files=` and `freedBytes=` instead of MSBuild output, releasing the workspace's own file locks first when they block the delete; it is not covered by `undo_last_change`. All Roslyn: no IDE, no external tool,
no licence, no network.

Every response is one record per line, with an explicit `truncated`/`total` and an `EXACT`
(Roslyn-resolved) or `HEURISTIC` (text/index) tag.

## Safety

- **Symbol-addressed edits** — no `old_string` echo, no line numbers to drift.
- **`dryRun` on every mutation** returns the unified diff and writes nothing.
- **Compile-gated** — an edit that introduces a *new* compile error is rolled back and the error
  returned. Pre-existing errors never block an edit. `allowErrors: true` opts out.
- **Short symbol references** — an outline prints `OrderService.Submit(Order)` rather than a
  200-character documentation id, and every tool that takes a `symbolId` accepts that name back.
  A member a short name cannot address unambiguously — a constructor, operator, indexer, generic or
  explicit interface implementation — keeps its documentation id, so every reference an outline prints
  resolves. `ids=full` prints ids for everything; an ambiguous name lists the candidates rather than
  guessing.
- **Truncation that steers** — a truncated listing says which parameter narrows it.
- **Diff-only responses** — mutations return the diff, a changed-line count and
  `errors=N (+D) warnings=N (+D)`, never the file. A `dryRun` that would be rolled back says so and
  names the errors it would introduce.
- **Workspace containment** — paths compare by whole segment, so root `C:\repo` does not contain
  `C:\repoEvil`.
- **`--read-only`** makes every mutating tool refuse and touch nothing.

## Parallel worktrees

Run several agents at once across several git worktrees of one repo, and across unrelated repos —
one server holding many workspaces (LRU, default 4), or many processes, or both. Every answer names
its worktree and branch, and an ambiguous request returns `AMBIGUOUS_WORKSPACE` listing the
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
