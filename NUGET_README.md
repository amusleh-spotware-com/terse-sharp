# TerseSharp

### The bridge between your coding agent and your C# codebase.

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server that lets an agent navigate, read,
edit, refactor, build and test a .NET solution **semantically** — no `Read`, no `Grep`, no
line-number `Edit`, no shelling out. **86 tools. One install. No IDE, no licence, no network.**

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
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed declarations | **10×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

Asserted by a token-budget suite in CI on every commit, not estimated.

- 💰 **Money.** Ten type reads cost ~60,000 input tokens with `Read`; ~4,500 here — billed every
  session, on every repo, for every agent you run.
- ⏱️ **Time.** No IDE to launch, no language server handshake. The workspace loads **once**, and
  repeat XAML / `.resx` / DI / file-path questions come from an index that **reads no file at all**.
- 🎯 **Fewer wrong edits.** A grep-driven rename misses the interface and hits the comment. An edit
  that introduces a compile error is **rolled back** before the agent reports it done.
- 🔁 **Fewer round trips.** `explore_symbol` folds signature + docs + usage counts + implementations
  + XAML sites into one call; `impact_of` answers *"what breaks if I change this"* — every
  referencing file and every project that recompiles — **before** the rename.

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
`$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set. Prefer to configure it by hand:

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
**denies** the built-in and names the tool to use instead:

- **Denied** — `Read`/`Write`/`Edit`/`MultiEdit` on `.cs`, `.razor`, `.cshtml`, `.razor.css`,
  `.razor.js`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`, `.xaml`, `.axaml`, `.resx`, `.resw`;
  `Glob`/`Grep` scoped to them; a shell text read or listing on them (`grep`, `rg`, `cat`, `head`,
  `tail`, `sed`, `awk`, `findstr`, `type`, `find`, `fd`, `ls`, `dir`, `tree`, `wc`, `nl`, plus the
  PowerShell forms); and `dotnet build`, `test`, `msbuild`, `vstest`, `format`, `clean` — anywhere in
  a compound command. A denial names the matching family: `resx_*`, `razor_*`, and
  `xaml_find`/`xaml_resolve`/`xaml_styles` before `find_files` for XAML.
- **Names a tool that can do it** — `Write`/`Edit` on a `.cs` path that does not exist yet names
  `write_text(path, content, force=true)`; a denial with no legal move is what produces a silent
  fallback. Every `.cs` write denial adds that the file is picked up automatically, with no reload.
- **Allowed** — plain `.css`, `.js`, `.csv`, `.csx` (matching is by file **extension**, not
  substring), and `dotnet restore`, `pack`, `publish`, `run`, `tool`: no TerseSharp tool replaces
  those, and a denial that names no alternative is a wall. Malformed hook input allows the call, so a
  guard fault cannot wedge a session.

Pair it with `--skill`: the skill teaches the swaps, the guard enforces them.

**🎮 Unity:** works on Unity game code too — Unity generates a real `.sln` with
`Assembly-CSharp.csproj`, so outlines, `find_usages`, symbol-addressed edits and compile-gated rename
across your `MonoBehaviour`s all work. Open the project in the editor once so the project files
exist; scene graph, inspector values and play-mode state are out of scope.

**✂️ Success costs nothing.** All 30 mutating tools answer a successful edit in **one line per
changed file** (`path  changedLines=N`), not a diff of text the agent just wrote; `edit_text` and
`write_text` print the file name alone, because the caller supplied the path. `verbose=true` restores
the full report, `dryRun=true` is never condensed — there the diff *is* the answer — and **every
caveat prints in full regardless**: a rollback, a new compile error, `0 files changed`,
`compileGate=unavailable`, `workspace=stale`, `UNFIXED`, `designerStale`, or the `NOT rewritten` list
a XAML-aware rename leaves. Reads carry no ceremony either: no header echoing the tool name and your
arguments, a first line that is the count (`4 usages in 2 files`, or
`4/17 usages truncated - narrow with maxResults=`), a line number only where the numbering jumps, and
`get_symbol_source` dedented and blank-free.

**🔔 Staying current.** One `HEAD` request to GitHub's `releases/latest` — empty body, no token — at
most once every 24 hours, on a background task that never blocks the handshake, cached in
`~/.terse/update`. When a newer release exists the next tool response carries one extra last line:

```
UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp
```

It appears once per server process and never repeats. After you update, the next `terse serve`
rewrites the installed `SKILL.md` and re-applies the `terse guard` hook so both match the new binary.
`TERSE_UPDATE=0` turns the check and the refresh off.

## What each tool replaces

| Instead of | Use | Why |
| --- | --- | --- |
| `Read` a `.cs` file | `get_file_outline` · `get_symbol_source` | types, members and line ranges — or one member, never the file; `symbolIds=[…]` returns several in one response, an unresolvable id inline as `NOT_RESOLVED <id>` |
| `Grep` a type or member name | `search_symbols` | declarations only; CamelHump (`OSvc` → `OrderService`) |
| `Grep` to find callers | `find_usages` · `find_implementations` | real references, each marked `src` or `test` |
| three calls to learn a symbol | `explore_symbol` · `impact_of` | signature, docs, usage counts, implementations and XAML sites in one call — and every file and project a change would touch |
| grepping `Program.cs` for DI | `find_registrations` · `list_endpoints` | open generics, factory delegates and `Add*` extensions grep cannot see |
| `Grep -C3`, then read around the hit | `search_text(query, context=3)` · `search_regex` | the surrounding lines arrive on the hit's own record; `unique=true` collapses repeats to `x<count>`; `root=` searches outside the workspace |
| `Glob` / `ls` | `find_files` | `glob=`, alias `pattern=`; `bin`, `obj`, `.git`, `artifacts`, `TestResults`, `node_modules` and symlinks skipped |
| `tail -n 200` on a log | `read_text(path, tail=200)` | the last N lines; a clipped read ends with `next: startLine=…`, and `maxChars=` bounds a file whose lines are too long for `maxLines` |
| `Edit` a `.cs` file | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` | addressed by symbol, immune to line drift; several declarations land as one compile-gated edit |
| creating or deleting a file | `write_text(path, content, force: true)` · `write_text(path, delete: true)` | containment-checked; the new type resolves on the very next call |
| `Edit` a `.md` section | `edit_text(path, section: "## Commands")` | no `oldText`, so no read-then-match round trip — `read_text(headings=true)` gives the map |
| `Read`/`Edit` a `.resx` | `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` | keys and values per culture with `MISSING` marked; the schema header, ordering, indentation, line endings and BOM preserved |
| `Read`/`Edit` a `.razor` | `razor_outline` · `razor_component` · `razor_find` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` | the component tree with every `<Card />` resolved to its type; element-addressed edits, and the generator re-runs so a broken one is rolled back |
| `Read`/`Edit` a `.xaml` | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_codebehind` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` | element tree and names, edits addressed by element with formatting preserved |
| find-and-replace a name | `rename_symbol` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `git status` / `git diff` | `changed_files` · `diff_symbols` · `diff_text` | one line per file, then every hunk mapped onto the declaration containing it as a symbol id; the raw hunks only when you ask. All three take `baseRef=` |
| `dotnet build` / `test` | `build` · `run_tests` · `rerun_failed` · `list_tests` | deduplicated diagnostics, no MSBuild spew; green is one line whatever it warned about, red lists errors only; `project=` takes a project **name** or a path, `configuration=`/`targetFramework=` map to `-c`/`-f` |
| `dotnet format` / `clean` | `format` · `cleanup fix=all` · `clean` | compile-gated code fixes, a one-line verdict, freed-byte counters — never raw CLI output |
| an IDE inspection sweep | `analyze` · `get_diagnostics` | compiler + every referenced analyzer + dead code, down to `info` |
| `Glob` for `*.sln` in an unfamiliar repo | `load_workspace discover=true` · `list_workspaces` · `workspace_status` · `unload_workspace` · `list_projects` | every solution and project under a directory, shallowest first, loading none |
| editing a `.csproj` by hand | `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` · `solution_projects` · `solution_add_project` · `solution_remove_project` | CPM-aware and containment-checked |
| reshaping a type by hand | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `get_symbol` · `get_type_outline` | Roslyn refactorings, compile-gated — and `undo_last_change` reverses the last symbol edit |

Markup and localization also answer the questions no text tool can: `xaml_bindings validate=true`
type-checks every `{Binding}` path, `xaml_resolve` reports every declaration of a resource key with
its `scope=local|theme`, `xaml_styles` prints the implicit and keyed styles with the `BasedOn` chain,
`xaml_localization` joins every `x:Uid` to its resource entry, `xaml_validate` reports duplicate and
unresolved keys, `razor_bindings` and `razor_validate` catch the attribute that matches no
`[Parameter]` (which compiles clean and throws at render), `razor_codebehind` reports what the markup
wires, and `resx_files` and `resx_validate` report missing translations, placeholder mismatches and
duplicates across a whole family.

## Safety and freshness

- **Symbol-addressed edits** — no `old_string` echo, no line numbers to drift. `dryRun` on every
  mutation returns the unified diff and writes nothing.
- **Compile-gated** — a C#, Razor or refactoring edit that introduces a *new* compile error is rolled
  back and the error returned; pre-existing errors never block an edit, and `allowErrors: true` opts
  out. The `.resx`, `.xaml` and `project_*`/`package_*`/`solution_*` writers are file writes: surgical
  and formatting-preserving, but outside the compile gate and outside `undo_last_change`.
- **Short symbol references** — an outline prints `OrderService.Submit(Order)` rather than a
  200-character documentation id, and every tool that takes a `symbolId` accepts that name back. A
  member a short name cannot address keeps its documentation id, so every printed reference resolves;
  an ambiguous name lists the candidates rather than guessing.
- **Workspace containment** — paths compare by whole segment, so root `C:\repo` does not contain
  `C:\repoEvil`. `--read-only` makes every mutating tool refuse and touch nothing.
- **The workspace follows the disk** — a `FileSystemWatcher` nominates changed paths and a **content
  comparison** decides, so a dropped or out-of-order OS event can delay a refresh but never corrupt
  one. Sync is lazy, so a `git checkout` storm costs one reload, not one per file; a changed
  `.csproj`/`.props`/`.sln`/`global.json` or a watcher overflow reloads rather than guesses; and
  `undo_last_change` drops a snapshot an external change overtook instead of reverting someone else's
  work.
- **Memory is a budget, not a cache** — four solutions stay loaded at once (`--max-workspaces`), a
  loaded workspace costs roughly 3 GB on a 148-project, 31,000-document solution, unloading ends with
  a compacting collection (measured 3418 MB → 652 MB), and a workspace idle for 15 minutes gives its
  compilations back (`--idle-minutes`), reported as `idle=<n>m compilations=dropped`.
- **Parallel worktrees** — many workspaces at once, across repos and git worktrees. Every answer names
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
