# TerseSharp

### The bridge between your coding agent and your C# codebase.

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server so your agent navigates, edits,
refactors, builds and tests .NET **semantically** — instead of reading whole files and grepping for
symbols. **88 tools. One install. No IDE, no licence, no language server.**

[![CI](https://img.shields.io/github/actions/workflow/status/amusleh-spotware-com/terse-sharp/ci.yml?branch=main&label=CI)](https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

## 4.6M tokens saved in one week — measured, not marketed

**508 Claude Code sessions**, one developer, one week, one **31,000-file** C# solution — replayed
from the raw transcripts. Every one of the **6,045 TerseSharp calls** was re-priced against the
built-in it replaced: `Read` against the **real file on disk**, `Grep` against a **real `ripgrep` run
of the same query**, `build` / `run_tests` / `git diff` against **3,767 real `dotnet` and `git`
invocations** mined from those same logs.

| | tokens |
| --- | --- |
| What the 6,045 TerseSharp calls actually cost | **2.41M** |
| What `Read` / `Grep` / `Bash` would have cost for the same answers | **6.97M** |
| **Burned for nothing, had it not been installed** | **4.56M — 2.9× the entire bill** |

**Then it compounds.** In those same sessions every token put into context was re-sent **33×**
(4.41B cache-read against 132.6M cache-write). 4.56M tokens never injected are **~150M tokens never
re-read**.

| The call | Built-in would cost | TerseSharp cost | |
| --- | ---: | ---: | ---: |
| `find_implementations` × 72 | 192k tok | 4k tok | **44.9×** |
| `find_usages` × 274 | 1.26M tok | 111k tok | **11.4×** |
| ranged `read_text` × 446 | 1.59M tok | 308k tok | **5.2×** |
| `get_file_outline` × 162 | 402k tok | 112k tok | **3.6×** |
| `search_symbols` × 520 | 860k tok | 272k tok | **3.2×** |
| `get_symbol_source` × 630 | 1.26M tok | 405k tok | **3.1×** |
| `build` × 165 | 32k tok | 10k tok | **3.1×** |
| whole-file `read_text` × 930 | 1.04M tok | 881k tok | 1.2× |
| `run_tests` × 393 | 80k tok | 66k tok | 1.2× |

**The floor.** Push every assumption *against* TerseSharp — ranged reads priced as a perfect
`Read offset/limit`, searches priced at the **median** grep output instead of the mean — and the
saving is still **2.09M tokens, 1.9×**. `analyze`, `search_text`, every edit tool and the whole
`.resx` / XAML / Razor surface were scored **zero**, and no grep→`Read` follow-up chain was charged
to the built-ins, so the real number is above both figures. `diff_symbols` (13 calls) and
`list_tests` (3) cost **more** than the raw command — measured, logged, not hidden.

**The fallback rate is the real result.** Across the whole week the agent reached for a built-in
**5 times with `Grep`, 11 with `Edit`** — and 55 of its 88 `Read` calls were PNG screenshots, which
no C# tool replaces. An agent that distrusts its MCP server falls back to the shell and spends *more*
than with no server at all; this one didn't.

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
terse doctor                    # verify SDK, MSBuild, workspace load, client registration, per-call latency
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, and no network call to answer a question — the
only request it ever makes is one `HEAD` to GitHub's `releases/latest`, at most once a day, to tell
you an update exists. `TERSE_UPDATE=0` turns that off. From inside an agent session,
`workspace_status verbose=true` answers `doctor`'s four actionable checks — `roslyn`, `assets`,
`guard coverage`, `phases` — without leaving the MCP.

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
| Where are these 8 ids? | one `grep`/search **per literal** | `search_text queries=[…]` → one pass, records tagged `q1`..`qN` | **8 calls → 1** |
| Rename it across the solution | ~5,000 tokens, **misses the interface** | `rename_symbol` → **~150**, correct | **30×** |
| Why is the build red? | **~8,000 tokens** of MSBuild spew | `build` → **~600** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed declarations | **10×** |
| Which rows does this checked-in table hold? | `Read` the whole `.md`, then grep it | `read_text columns="Finding,Tool"` → one line per row | **~10×** |
| Which tests can this change break? | `Grep` the test tree, then guess | `impact_of tests=true` → ready `run_tests test=` arguments | **2 calls → 1** |
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

## What it costs you

An MCP server's fixed cost is its tool list, attached to every request. TerseSharp is the one that
measures its own — `workspace_status` prints `advertised=<n> tools <t> tokens`, held under a
**26 490-token ceiling over 88 tools** by a budget test on every push — and it shrinks it three ways.
All three are optional: the default advertises everything.

- **Automatically.** A solution holding no `.xaml`, `.razor` or `.resx` never sees those 31 tools —
  **57 tools, ≤21 360 tokens**. Load a solution that does hold them and they come back, announced
  with `notifications/tools/list_changed`.
- **Per project** — a `.terse.json` checked in beside your `.sln`, found by walking up from the
  directory the server runs in and never above the repository root:

  ```json
  {
    "tools": {
      "groups": { "xaml": false, "razor": false },
      "names": { "search_regex": false }
    }
  }
  ```

  Twelve groups — `analysis` `build` `edit` `file` `git` `navigation` `project` `razor` `refactor`
  `resx` `workspace` `xaml` — plus any tool name under `names`, which outranks its group. That file
  measures **64 tools, 22 097 tokens**. An unknown key is reported rather than silently dropped, and
  the guard follows the file: a built-in whose every replacement you disabled is allowed again. The
  server reads it once at startup, so restart your agent after changing it.
- **By profile.** `terse serve --tools core` (or `TERSE_TOOLS=core`) advertises the 21 tools that
  answer most questions; `--tools all` opts out of every narrowing.

A hidden tool is unadvertised, not removed — it still answers when called by name.

## Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and
names the tool to use instead — covering `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`,
`.sln` and friends, the shell text tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them,
`dotnet build`/`test`/`format`/`clean`, `dotnet watch build`/`test` and `dotnet list package`, and the working-tree half of
git — `git status` and `git diff`, answered by `changed_files` and `diff_symbols`, with `git diff --cached` routed to `changed_files staged=true` by name, plus a bare
`git ls-files`, answered by `find_files tracked=true`, and a `git tag` listing, answered by
`history tags=true`, and only when the directory the command
actually addresses sits under a `.sln`/`.slnx`/`.slnf`/`.csproj` — the `-C` target, or a directory
operand, before the working directory. The hook is user-wide and those tools answer about the loaded
workspace, so `git -C ../notes status` is allowed. A denied command also tells the
agent not to run it in `Bash` again. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, `git ls-files` with any option, and git mutation
(`blame`, `add`, `commit`, `push`, and every `git tag` that creates, annotates or deletes one) are allowed, because nothing here replaces those; malformed hook input allows the
call, so a guard fault can never wedge a session; and you remove the guard by deleting the
`terse guard` entry from Claude Code's `settings.json`. Pair it with `--skill`, which ships Claude Code the skill that teaches the
swaps — on any other agent, put the same rule in `AGENTS.md` or `.cursorrules`.

A denial also returns `additionalContext` — the complete replacement call with the arguments filled
in from the command it denied (`Call this instead: get_file_outline path="src/App/Order.cs"`) — so
the agent is routed, not merely refused. Set `TERSE_GUARD_LOG=<path>` to append one JSON line per
decision (tool, verdict, routing, reason, cwd, session, transcript, and `standDown` when a project's
`.terse.json` turned a denial back into an allow), opt-in and best-effort; a write failure never
changes the verdict.

## The tools

The full catalogue is attached to every request, and past a certain size that measurably costs
tool-selection accuracy — so **the advertised set is derived from what the solution actually
contains**. A tree with no `.xaml`/`.axaml` is not offered the 13 `xaml_*` tools, one with no
`.razor`/`.cshtml` is not offered the 10 `razor_*`, one with no `.resx`/`.resw` is not offered the 8
`resx_*`; measured on a plain C# solution that is **57 tools instead of 88, 19 091 tokens instead of
24 330 (-21.5 %)** on every request. Loading a second solution that does hold them re-advertises the
families through `notifications/tools/list_changed`, and a hidden tool still answers when called by
name. `terse serve --tools all` (or `TERSE_TOOLS=all`) advertises everything regardless;
`--tools core` still narrows to a 21-tool subset. `workspace_status` names whatever is hidden.

**88 tools.** One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a
success that costs nothing — every mutating tool answers in one line per changed file, with
`verbose=true` for the diff and `dryRun=true` to preview it. Any caveat prints in full.

**Ten of them take a plural.** `read_text paths=`, `get_file_outline paths=`, `diff_text paths=`,
`get_symbol_source symbolIds=`, `replace_symbol symbolIds=`, `search_text`/`search_regex queries=`,
`run_tests projects=`, `write_text files=` and `edit_text edits=` each answer in one call what used to
cost one call per item — `run_tests projects=` also runs them **concurrently**, one process per core
by default, each project built before the fan-out (`parallel=1` restores the serial run) — and
`write_text files=` puts every `.cs` file it writes through **one**
compile gate, so a type and the consumer it breaks land together. From the **second** consecutive call
of the same tool the response gains **one** extra line — `2 read_text calls in a row - pass paths=[...]
with the next 2+ in ONE call` — the single documented exception to "a success is one line", worth
about 14 tokens, and never emitted when the call already passed the plural.

| Group | Tools |
| --- | --- |
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** — replaces `Read`/`Grep` | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **What grep can't reach** | `find_registrations` (DI: open generics, factories, `Add*` extensions) · `list_endpoints` (ASP.NET Core `Map*`) |
| **Analyze & clean** — replaces `dotnet format` | `analyze` · `format` · `cleanup` · `gate` (all four in the mandated order, one verdict line) · `clean` · `get_diagnostics` |
| **Edit** — replaces `Edit` on a `.cs` | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** — `package_list` replaces `dotnet list package` | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` (MSBuild's **evaluated** properties, each with the file that set it) · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` (`vulnerable=` / `outdated=`) · `package_add` · `package_remove` |
| **XAML** — WPF · Avalonia · WinUI · MAUI | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Localization** (`.resx`/`.resw`) | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Files** — replaces `Glob`/`ls`/`cat` | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Git** — replaces `git status`/`git diff`/`git diff --cached`/`git log`/`git tag --list` | `changed_files` (`staged=true`, `untracked=false`) · `diff_symbols` · `diff_text` · `history` (`tags=true` for the tag list) |
| **Build & test** — replaces `dotnet build`/`test` | `build` · `run_tests` · `rerun_failed` · `list_tests` |

`build`, `run_tests`, `rerun_failed` and `list_tests` drive **both** test hosts: VSTest, and Microsoft.Testing.Platform when
`global.json` selects it (`"test": { "runner": "Microsoft.Testing.Platform" }`, as xunit.v3, MSTest and
NUnit projects use). That host rejects the whole session over one VSTest-shaped argument, so the
invocation — target switch, trx reporter, timeout, filter — is rebuilt for it rather than patched.
`list_tests` answers under both: the SDK hosts the platform's test application in server mode and
discards its `--list-tests` output, so terse resolves each test project's `TargetPath` and runs the
test module itself.

Every read tool declares the MCP `readOnlyHint` annotation and every deleting tool declares
`destructiveHint`, so a client that gates parallel dispatch on those hints — Claude Code does — can
fan the reads out instead of running them one at a time. The build and test tools are deliberately
left off that list: they run a build, and a build dispatched beside an edit is a race, not a saving.

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

## Contributing

The easiest way to help: clone the repo and run `/mine-sessions` in Claude Code. It reads your own
session logs, measures where the tools cost you tokens or round trips, and appends the findings to
`IMPROVEMENTS.md` — the open table; closed rows keep their measurement in `IMPROVEMENTS-ARCHIVE.md`.
Skim the new rows — keep the ones that look real, drop anything that leaked a path
or a secret — then open a PR with just that file. We work through the backlog every weekend, so your
friction becomes next week's release.

## Links

- [Source, full documentation and issues](https://github.com/amusleh-spotware-com/terse-sharp)
- [Changelog](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CONTRIBUTING.md)
- [Security policy](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/SECURITY.md)

## License

MIT Licensed. See [LICENSE](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE).

Built on [Roslyn](https://github.com/dotnet/roslyn) and the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
