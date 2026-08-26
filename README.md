<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>The bridge between your coding agent and your C# codebase.</b><br/>
  A Roslyn-powered <a href="https://modelcontextprotocol.io">MCP</a> server so your agent navigates,
  edits, refactors, builds and tests .NET <b>semantically</b> — instead of reading whole files and
  grepping for symbols.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/v/TerseSharp.svg?logo=nuget&label=NuGet" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/dt/TerseSharp.svg?logo=nuget&label=downloads" alt="Downloads"/></a>
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="MIT"/></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet&logoColor=white" alt=".NET 10"/>
  <img src="https://img.shields.io/badge/tools-88-26C281.svg" alt="88 tools"/>
  <img src="https://img.shields.io/badge/tokens-10--30×_fewer-26C281.svg" alt="10-30x fewer tokens"/>
</p>

## 📊 4.6M tokens saved in one week — measured, not marketed

**508 Claude Code sessions**, one developer, one week, one **31,000-file** C# solution — replayed
from the raw transcripts. Every one of the **6,045 TerseSharp calls** was re-priced against the
built-in it replaced: `Read` against the **real file on disk**, `Grep` against a **real `ripgrep` run
of the same query**, `build` / `run_tests` / `git diff` against **3,767 real `dotnet` and `git`
invocations** mined from those same logs.

| | tokens |
|---|---|
| What the 6,045 TerseSharp calls actually cost | **2.41M** |
| What `Read` / `Grep` / `Bash` would have cost for the same answers | **6.97M** |
| **Burned for nothing, had it not been installed** | **4.56M — 2.9× the entire bill** |

**Then it compounds.** In those same sessions every token put into context was re-sent **33×**
(4.41B cache-read against 132.6M cache-write). 4.56M tokens never injected are **~150M tokens never
re-read**.

<details>
<summary>Per-call breakdown, the floor, and the two tools that lost</summary>

| The call | Built-in would cost | TerseSharp cost | |
|---|---:|---:|---:|
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
to the built-ins, so the real number is above both figures.

**What lost.** `diff_symbols` (13 calls) and `list_tests` (3) cost *more* than the raw command.
Measured, logged in the backlog, not hidden.

**The fallback rate is the real result.** Across the whole week the agent reached for a built-in
**5 times with `Grep`, 11 with `Edit`** — and 55 of its 88 `Read` calls were PNG screenshots, which
no C# tool replaces. An agent that distrusts its MCP server falls back to the shell and spends *more*
than with no server at all; this one didn't.
</details>

## 🚀 Install

```bash
dotnet tool install -g TerseSharp
terse install            # registers with every client it detects
```

That's it. Restart your agent and ask it something about your code — with no arguments the server
walks up from the current directory, finds your `.sln` / `.slnx` / `.slnf` / `.csproj` and loads it.

```bash
terse install --client cursor   # not detected? pick one: claude-code | cursor | vscode | windsurf
terse install --skill --guard   # teach your agent the tools, and block Read/Grep on C# (recommended)
terse doctor                    # verify SDK, MSBuild, workspace load, client registration, per-phase latency
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, and no network call to answer a question. From
inside an agent session, `workspace_status verbose=true` answers `doctor`'s four actionable checks —
`roslyn`, `assets`, `guard coverage`, `phases` — without leaving the MCP.

<details>
<summary>Configure MCP by hand, build from source, Unity, updates</summary>

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

Claude Code reads `~/.claude.json`, or `$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set;
`terse doctor` prints the path it read.

```bash
git clone https://github.com/amusleh-spotware-com/terse-sharp && cd terse-sharp
dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg
dotnet tool install -g TerseSharp --add-source artifacts/nupkg --prerelease
```

**Unity** works: Unity generates a real `.sln` with `Assembly-CSharp.csproj`, so outlines,
`find_usages` and compile-gated rename across your `MonoBehaviour`s all work. Open the project in the
editor once so the project files exist.

**Updates** are one `HEAD` request to GitHub at most once a day, on a background task; a newer release
adds one line to the next tool response. `TERSE_UPDATE=0` turns it off.
</details>

## 💸 What it saves you

| Question | Built-in tools | TerseSharp | |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → **~6,000 tok** | `get_type_outline` → **~450 tok** | **13×** |
| Read a whole `.cs` file | `Read` → the entire text | `read_text` answers the **outline** unless you ask for the text | **3×** |
| Who calls this method? | `Grep` + follow-ups → **~4,000 tok** | `find_usages` → **~200 tok** | **20×** |
| Where are these 8 ids? | one `grep`/search **per literal** | `search_text queries=[…]` → one pass, records tagged `q1`..`qN` | **8 calls → 1** |
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed **declarations** | **10×** |
| Which rows does this checked-in table hold? | `Read` the whole `.md`, then grep it | `read_text columns="Finding,Tool"` → one line per row | **~10×** |
| Which tests can this change break? | `Grep` the test tree, then guess | `impact_of tests=true` → ready `run_tests test=` arguments | **2 calls → 1** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

<sub>Asserted by a token-budget suite on every push, not estimated.</sub>

- 🧠 **Semantic, never textual.** Real references, not string matches — every record tagged `EXACT` or
  `HEURISTIC`, so you always know what you're trusting.
- 🛡️ **Compile-gated edits.** An edit that introduces a compile error is rolled back before the agent
  reports it done, and `undo_last_change` reverses the last one. `replace_symbol` takes a whole batch
  of edits across files, so a signature change lands **with** the callers it breaks.
- 🧾 **No silently-ignored arguments.** A parameter a tool doesn't declare is refused by name, with
  every accepted spelling — a listing that quietly dropped your `maxResults` is a wrong answer the
  agent can't detect.
- 🔄 **Always fresh.** A file you just created, or an edit from your IDE, is already in the answer.
- 🚫 **Never guesses.** Where it can't prove an answer it says so — a false positive costs an agent
  more than no answer.

## 🪶 What it costs you

An MCP server's fixed cost is its tool list, attached to every request. TerseSharp is the one that
measures its own — `workspace_status` prints `advertised=<n> tools <t> tokens`, and under `verbose=true`
the whole surface beside it (`advertised=57 tools 21780 tokens of 88 tools 26851`), so what a narrowing
saves is read off the running server. The total is held under a **26 900-token ceiling over 88 tools** by a
budget test on every push — and it shrinks three ways.
All three are optional: the default advertises everything.

- **Automatically.** A solution holding no `.xaml`, `.razor` or `.resx` never sees those 31 tools —
  **57 tools, ≤21 780 tokens**. Load a solution that does hold them and they come back, announced
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

## 🔒 Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and
names the tool to use instead:

```
TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text.
```

A denial is not only a prohibition. It also returns `additionalContext` — the **complete replacement
call, with the arguments filled in from the command it just denied** — which Claude Code places
beside the tool result: `Call this instead: get_file_outline path="src/App/OrderService.cs"`. A
positive routing instruction at the moment the agent is about to fall back beats a negation, which is
the weaker lever.

Set `TERSE_GUARD_LOG=<path>` to append one JSON line per decision — tool, verdict, routing, reason,
`cwd`, session and transcript path, plus `standDown` when a project's `.terse.json` turned a denial
back into an allow — so a later scan can tell a denied-and-retried command from one the guard never
saw, a stood-down one from one nothing replaces, and a subagent's call from the main thread's. It is opt-in, best-effort, and a
write failure never changes the verdict.

It covers `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln` and friends, the shell text
tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them, `dotnet build`/`test`/`format`/`clean`,
`dotnet watch build`/`test`, `msbuild` and `dotnet list package`, and the working-tree half of git — `git status` and
`git diff`, in every flag and `-C` form, answered by `changed_files` and `diff_symbols`, with
`git diff --cached` routed to `changed_files staged=true` by name, plus a bare
`git ls-files`, answered by `find_files tracked=true`, and a `git tag` **listing**, answered by
`history tags=true` — but only
when the directory the command actually addresses sits under a `.sln`/`.slnx`/`.slnf`/`.csproj`: the
`-C` target, or a directory operand, before the working directory. The hook is installed user-wide and
those tools answer about the loaded workspace, so `git -C ../notes status` is allowed — nothing here
replaces it. A denied command
also tells the agent not to run it in `Bash` again. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, `git ls-files` with any option, and git mutation
(`blame`, `add`, `commit`, `push`, and every `git tag` that creates, annotates or deletes one) are allowed — nothing here replaces those. Malformed hook input allows the call, so
a guard fault can never wedge a session, and you remove the guard by deleting the `terse guard` entry
from Claude Code's `settings.json`.

`terse install --skill` ships Claude Code the skill that teaches the swaps. On any other agent, a
short rule in `CLAUDE.md` / `AGENTS.md` / `.cursorrules` does the same job: *"C#/.NET goes through
terse-sharp; `Read`/`Grep`/`Edit` on `.cs`, `.xaml`, `.razor` or `.resx` is forbidden."*

## 🧰 The tools

**88 tools.** One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a
success that costs nothing — every mutating tool answers in one line per changed file, with
`verbose=true` for the diff and `dryRun=true` to preview it. Any caveat prints in full.

**Ten of them take a plural.** `read_text paths=`, `get_file_outline paths=`, `diff_text paths=`,
`get_symbol_source symbolIds=`, `replace_symbol symbolIds=`, `search_text`/`search_regex queries=`,
`run_tests projects=`, `write_text files=` and `edit_text edits=` each answer in one call what used to
cost one call per item — `run_tests projects=` also runs them **concurrently**, one process per core
by default, each project built before the fan-out (`parallel=1` restores the serial run), and a bare
`run_tests` over a solution now does that by itself, the way an IDE does: the solution is built
**once**, then each test assembly is run directly where its runner allows — no MSBuild evaluation and
no VSTest host per project — measured **38 % faster** over five alternating pairs on a two-test-project solution — and
`write_text files=` puts every `.cs` file it writes through **one**
compile gate, so a type and the consumer it breaks land together. From the **second** consecutive call
of the same tool the response gains **one** extra line — `2 read_text calls in a row - pass paths=[...]
with the next 2+ in ONE call` — the single documented exception to "a success is one line", worth
about 14 tokens, and never emitted when the call already passed the plural.

A full catalogue is attached to every request, and past a certain size that measurably costs
tool-selection accuracy — so **the advertised set is derived from what the solution actually
contains**. A tree with no `.xaml`/`.axaml` is not offered the 13 `xaml_*` tools, one with no
`.razor`/`.cshtml` is not offered the 10 `razor_*`, one with no `.resx`/`.resw` is not offered the 8
`resx_*`; measured on a plain C# solution that is **57 tools instead of 88, 19 091 tokens instead of
24 330 (-21.5 %)** on every request. Loading a second solution that does hold them re-advertises the
families through `notifications/tools/list_changed`, and a hidden tool still answers when called by
name. `terse serve --tools all` (or `TERSE_TOOLS=all`) advertises everything regardless;
`--tools core` still narrows to a 21-tool subset. `workspace_status` names whatever is hidden.

| Group | Tools |
|---|---|
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

## 🎨 Markup and localization the compiler can't check

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers what no
text tool can.

**Does this binding actually bind?** WPF has no compile-time binding check at all — a typo fails
silently to debug output. `xaml_bindings validate=true` walks every path segment against the real
symbol:

```
src/Views/BoundView.xaml:7  EXACT  TextBlock.Text  {Binding Symbol}  OK Symbol on Trading.OrderViewModel
src/Views/BoundView.xaml:9  EXACT  TextBlock.Text  {Binding Symbl}   ERROR no member 'Symbl'; nearest 'Symbol'
```

**The Blazor bug nothing else catches.** An attribute matching no `[Parameter]` compiles clean and
throws the first time the component renders — `razor_validate` reports it at the `.razor` line:

```
RZR002  src/App/Components/Home.razor:6  Card.Bogus  UNKNOWN_PARAMETER  Card has no [Parameter] with that name
```

**Translation bugs no compiler catches.** `resx_validate` reports missing values and placeholder
mismatches across a whole `.resx` family, instead of the ~36,000 tokens it costs to read one.

**Renames carry into markup.** `rename_symbol` rewrites `Click="…"` and `{Binding …}` — but only where
an `x:Class` or `x:DataType` *proves* the reference; anything else is listed `NOT rewritten` rather
than rewritten on a guess.

## ❓ FAQ

<details>
<summary>Do I need Visual Studio, Rider or a licence? Which agents work?</summary>

No licence, no IDE, no language server. Anything that speaks MCP over stdio works; `terse install`
registers Claude Code, Cursor, VS Code and Windsurf automatically.
</details>

<details>
<summary>Will it edit my code behind my back?</summary>

Only when your agent calls a mutating tool, and every one of them takes `dryRun=true` (bar
`undo_last_change`, which *is* the undo). C#, Razor and refactoring edits are compile-gated — an edit
that introduces a compile error is rolled back — and the C# and refactoring ones are also reversible
with `undo_last_change`. The `.resx`, `.xaml`, Razor and project/package/solution writers are surgical
file writes outside undo, so preview those with `dryRun`. `--read-only` makes every mutating tool
refuse and touch nothing.
</details>

<details>
<summary>Huge solutions? Parallel worktrees? VB.NET or F#?</summary>

Four solutions stay loaded at once (`--max-workspaces`, `TERSE_MAX_WORKSPACES` — a big solution costs
gigabytes, so set `1` if you only ever work in one), an idle one gives its compilations back after 15
minutes (`--idle-minutes`), `--no-watch` turns the file watcher off, and responses are bounded
and declare their truncation — a 5,000-type solution answers in the same shape as a 50-type one. Every
answer names its worktree and branch, and an ambiguous request lists the candidates instead of
guessing. VB and F# projects load without breaking navigation, but the language tools are C#-first.
</details>

<details>
<summary>Databases, debugging, profiling?</summary>

Out of scope on purpose. Git is read-only — the working tree served as tools, history left to the CLI.
Six shallow tools would be worse than none.
</details>

## 🤝 Contributing · 📄 License

See [CONTRIBUTING.md](CONTRIBUTING.md). Two rules that aren't negotiable: **a tool without an E2E test
isn't done**, and **a tool that doesn't beat the built-in it replaces doesn't ship**.

**The easiest way to help:** clone the repo and run `/mine-sessions` in Claude Code. It reads your own
session logs, measures where the tools cost you tokens or round trips, and appends the findings to
[IMPROVEMENTS.md](IMPROVEMENTS.md) — the open table; closed rows keep their measurement in
[IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md). Skim the new rows — keep the ones that look
real, drop anything
that leaked a path or a secret — then open a PR with just that file. We work through the backlog every
weekend, so your friction becomes next week's release.

Changes are in
[CHANGELOG.md](CHANGELOG.md), releases in [RELEASING.md](RELEASING.md), security in
[SECURITY.md](SECURITY.md). MIT Licensed — see [LICENSE](LICENSE).

<p align="center">
  <sub>Built on <a href="https://github.com/dotnet/roslyn">Roslyn</a> and the
  <a href="https://github.com/modelcontextprotocol/csharp-sdk">MCP C# SDK</a>.</sub>
</p>
