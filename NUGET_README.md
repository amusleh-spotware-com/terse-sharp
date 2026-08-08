# TerseSharp

### The bridge between your coding agent and your C# codebase.

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server so your agent navigates, edits,
refactors, builds and tests .NET **semantically** — instead of reading whole files and grepping for
symbols. **86 tools. One install. No IDE, no licence, no language server.**

[![CI](https://img.shields.io/github/actions/workflow/status/amusleh-spotware-com/terse-sharp/ci.yml?branch=main&label=CI)](https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

## Install

```
dotnet tool install -g TerseSharp
terse install            # registers with Claude Code, Cursor, VS Code, Windsurf
```

That's it. Restart your agent and ask it something about your code — with no arguments the server
walks up from the current directory, finds your `.sln` / `.slnx` / `.slnf` / `.csproj` and loads it.

```
terse install --client cursor   # not detected? pick one: claude-code | cursor | vscode | windsurf
terse install --skill --guard   # teach your agent the tools, and block Read/Grep on C# (recommended)
terse doctor                    # verify SDK, MSBuild, workspace load, client registration
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, and no network call to answer a question — the
only request it ever makes is one `HEAD` to GitHub's `releases/latest`, at most once a day, to tell
you an update exists. `TERSE_UPDATE=0` turns that off.

Prefer to configure MCP by hand:

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

## What it saves you

| Question | Built-in tools | TerseSharp | |
| --- | --- | --- | --- |
| What's on this 2,000-line type? | `Read` → **~6,000 tokens** | `get_type_outline` → **~450** | **13×** |
| Read a whole `.cs` file | `Read` → the entire text | `read_text` answers the **outline** unless you ask for the text | **3×** |
| Who calls this method? | `Grep` + follow-up reads → **~4,000** | `find_usages` → **~200** | **20×** |
| Rename it across the solution | ~5,000 tokens, **misses the interface** | `rename_symbol` → **~150**, correct | **30×** |
| Why is the build red? | **~8,000 tokens** of MSBuild spew | `build` → **~600** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed declarations | **10×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

Asserted by a token-budget suite in CI on every commit, not estimated.

- **Semantic, never textual.** Real references, not string matches — every record tagged `EXACT` or
  `HEURISTIC`, so you always know what you're trusting.
- **Compile-gated edits.** An edit that introduces a compile error is rolled back before the agent
  reports it done, and `undo_last_change` reverses the last one. `replace_symbol` takes a whole batch
  of edits across files, so a signature change lands **with** the callers it breaks.
- **No silently-ignored arguments.** A parameter a tool doesn't declare is refused by name, with every
  accepted spelling — a listing that quietly dropped your `maxResults` is a wrong answer the agent
  can't detect.
- **Always fresh.** A file you just created, or an edit from your IDE, is already in the answer.
- **Never guesses.** Where it can't prove an answer it says so — a false positive costs an agent more
  than no answer.

## Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and
names the tool to use instead — covering `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`,
`.sln` and friends, the shell text tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them,
`dotnet build`/`test`/`format`/`clean` and `dotnet watch build`/`test`, and the working-tree half of
git — `git status` and `git diff`, answered by `changed_files` and `diff_symbols`, plus a bare
`git ls-files`, answered by `find_files tracked=true`, and only when the
working directory sits under a `.sln`/`.slnx`/`.slnf`/`.csproj`, since the hook is user-wide and
those tools cannot answer in a repository TerseSharp does not serve. A denied command also tells the
agent not to run it in `Bash` again. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, `git ls-files` with any option, and git history and mutation
(`log`, `blame`, `show`, `add`, `commit`, `push`) are allowed, because nothing here replaces those; malformed hook input allows the
call, so a guard fault can never wedge a session; and you remove the guard by deleting the
`terse guard` entry from Claude Code's `settings.json`. Pair it with `--skill`, which ships Claude Code the skill that teaches the
swaps — on any other agent, put the same rule in `AGENTS.md` or `.cursorrules`.

## The tools

`terse serve --tools core` (or `TERSE_TOOLS=core`) advertises a 21-tool subset instead of the full
catalogue, which is attached to every request and past a certain size measurably costs tool-selection
accuracy. It hides nothing: every other tool still answers when called by name, and `workspace_status`
reports which profile is running.

**86 tools.** One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a
success that costs nothing — every mutating tool answers in one line per changed file, with
`verbose=true` for the diff and `dryRun=true` to preview it. Any caveat prints in full.

| Group | Tools |
| --- | --- |
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** — replaces `Read`/`Grep` | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **What grep can't reach** | `find_registrations` (DI: open generics, factories, `Add*` extensions) · `list_endpoints` (ASP.NET Core `Map*`) |
| **Analyze & clean** — replaces `dotnet format` | `analyze` · `format` · `cleanup` · `clean` · `get_diagnostics` |
| **Edit** — replaces `Edit` on a `.cs` | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** — WPF · Avalonia · WinUI · MAUI | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Localization** (`.resx`/`.resw`) | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Files** — replaces `Glob`/`ls`/`cat` | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Git** — replaces `git status`/`git diff` | `changed_files` · `diff_symbols` · `diff_text` |
| **Build & test** — replaces `dotnet build`/`test` | `build` · `run_tests` · `rerun_failed` · `list_tests` |

## Markup and localization the compiler can't check

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers what no
text tool can: `xaml_bindings validate=true` type-checks every `{Binding}` path against the real
symbol (WPF has no compile-time binding check at all), `razor_validate` catches the attribute matching
no `[Parameter]` that compiles clean and throws at render, `resx_validate` reports missing
translations and placeholder mismatches across a whole family, and `rename_symbol` carries a rename
into the markup — but only where an `x:Class` or `x:DataType` *proves* the reference.

## Safety and freshness

- **Symbol-addressed edits** — no `old_string` echo, no line numbers to drift, and `dryRun` on every
  mutation returns the diff and writes nothing.
- **Compile-gated** — a C#, Razor or refactoring edit that introduces a *new* compile error is rolled
  back, and the C# and refactoring ones are reversible with `undo_last_change`. The `.resx`, `.xaml`,
  Razor and project/package/solution writers are surgical file writes outside undo, so preview those
  with `dryRun`. `--read-only` makes every mutating tool refuse and touch nothing.
- **Follows the disk** — a `FileSystemWatcher` nominates changed paths and a content comparison
  decides, so a dropped OS event can delay a refresh but never corrupt one.
- **Bounded memory** — four solutions stay loaded at once, and one idle for 15 minutes gives its
  compilations back.
- **Parallel worktrees** — every answer names its worktree and branch, and an ambiguous request lists
  the candidates instead of guessing.

## Links

- [Source, full documentation and issues](https://github.com/amusleh-spotware-com/terse-sharp)
- [Changelog](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CONTRIBUTING.md)
- [Security policy](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/SECURITY.md)

## License

MIT Licensed. See [LICENSE](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE).

Built on [Roslyn](https://github.com/dotnet/roslyn) and the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
