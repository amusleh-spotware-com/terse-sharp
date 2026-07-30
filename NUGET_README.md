# TerseSharp

**Your agent stops reading whole C# files.**

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server that lets a coding agent navigate,
read, edit and refactor a .NET solution **semantically** — no `Read`, no `Grep`, no line-number
`Edit`, no shelling out.

[![CI](https://img.shields.io/github/actions/workflow/status/amusleh-spotware-com/terse-sharp/ci.yml?branch=main&label=CI)](https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

> **v0.2.2 — 51 tools working end to end.** Verified by 117 tests (29 unit + 88 E2E), where every E2E
> test drives a real server process over the real stdio transport against a real solution and asserts
> response values. **Not yet built:** the content-addressed index, trigram search and file watcher.

## Why

An agent working a C# solution spends most of its context on the wrong shape of data:

| Question | With built-in tools |
| --- | --- |
| What's on `OrderService`? | `Read OrderService.cs` → ~6,000 tokens |
| Who calls `Submit`? | `Grep "Submit"` + 3 more reads → ~4,000 tokens |
| Rename `Submit` → `SubmitAsync` | grep + 9 context-echoing edits, misses the interface |
| Why is the build red? | full MSBuild output → ~8,000 tokens |

Roslyn already knows all four answers semantically. TerseSharp hands them over in the shape the agent
needs: a signature list instead of a file, real call sites instead of string matches, a solution-wide
rename instead of a regex sweep, deduplicated diagnostics instead of build logs.

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
terse doctor                        # verify SDK, MSBuild, workspace load, client registration
```

With no arguments the server walks up from the current directory, finds your `.sln` / `.slnx` /
`.slnf` / `.csproj`, and loads it.

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
| `Grep` to find callers | `find_usages` | real references; no comments, no string matches |
| `Edit` a `.cs` file | `replace_symbol_body` | addressed by symbol id, immune to line drift |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs |
| `dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `dotnet test` | `run_tests` | failures only; a green run is one line |

## The 51 tools

- **Workspace** — `load_workspace`, `workspace_status`, `list_workspaces`, `unload_workspace`, `list_projects`
- **Navigation** — `search_symbols`, `get_symbol`, `get_file_outline`, `get_type_outline`, `get_symbol_source`, `find_usages`, `find_implementations`
- **Analyze & clean** — `analyze`, `format`, `cleanup`, `get_diagnostics`
- **Edit** — `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`
- **Refactor** — `extract_interface`, `move_type_to_file`, `move_type_to_namespace`, `change_signature`, `undo_last_change`
- **Projects & solutions** — `solution_projects`, `solution_add_project`, `solution_remove_project`, `project_create`, `project_properties`, `project_set_property`, `project_add_reference`, `project_remove_reference`, `package_list`, `package_add`, `package_remove`
- **XAML** — `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_bindings`, `xaml_validate`, `xaml_find`
- **Files** — `read_text`, `write_text`, `edit_text`, `find_files`, `search_text`, `search_regex`
- **Build** — `build`, `run_tests`

### Analysis without a licence

`analyze` runs the compiler plus every analyzer your projects already reference - CA rules,
StyleCop, SonarAnalyzer, Roslynator, anything in your `PackageReference` list - down to `info` and
`hidden` severity, which a normal build hides. It also reports dead code in the same list -
unreferenced private members as `TERSE001`, plus the compiler's unused-field and unreachable-code
hints - so one call covers everything. `cleanup` removes unused `using` directives, sorts what
remains System-first and reformats to your `.editorconfig`. All Roslyn: no IDE, no external tool,
no licence, no network.

Every response is one record per line, with an explicit `truncated`/`total` and an `EXACT`
(Roslyn-resolved) or `HEURISTIC` (text/index) tag.

## Safety

- **Symbol-addressed edits** — no `old_string` echo, no line numbers to drift.
- **`dryRun` on every mutation** returns the unified diff and writes nothing.
- **Compile-gated** — an edit that introduces a *new* compile error is rolled back and the error
  returned. Pre-existing errors never block an edit. `allowErrors: true` opts out.
- **Diff-only responses** — mutations return the diff and a changed-line count, never the file.
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
