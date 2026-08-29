<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>Your coding agent finishes sooner.</b><br/>
  A Roslyn-powered <a href="https://modelcontextprotocol.io">MCP</a> server that answers C#/.NET
  <b>semantically</b> — so your agent stops reading whole files, stops grepping for symbols, and stops
  turning the loop three times to learn one thing.
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

## ⏱️ Why it makes agentic development faster

An agentic task is a loop: emit a call, wait, read the answer, emit the next one. Two things decide
how long it takes — **how many times the loop turns**, and **how much each answer costs to read.**

Mined from one week of real Claude Code sessions — 305 transcripts, 36,075 tool calls — **every round
trip costs 6.1 s of model latency before the tool even runs.** So a tool that *deletes a call* beats
one that merely shortens a response, and both beat a faster server.

**Fewer turns of the loop:**

| Instead of | One call |
|---|---|
| grep, open the hit, open its callers | `find_usages` — real references, each tagged `EXACT` or `HEURISTIC` |
| read the file to find the member, then read the member | `get_file_outline` → paste the id straight into `get_symbol_source` |
| one search per identifier | `search_text queries=[…]` — 8 literals, one pass, records tagged `q1`..`qN` |
| a text hit, then an outline, then a ranged read to find which method it is in | `search_text containers=true` — the declaration each hit sits in, on the record itself |
| `git describe` to find where HEAD sits before a release | `history describe=true` — nearest tag, commits since it, short sha, dirty flag, one line |
| one read per file | `read_text paths=[…]` — up to 10 files, one answer |
| one edit call per site | `edit_text edits=[…]` — 25 edits across files, one write |
| edit, build, find you broke a caller, edit again | `replace_symbol symbolIds=[…]` — the member and its callers land in **one** compile gate |
| grep the test tree and guess what to run | `impact_of tests=true` — ready-made `run_tests test=` arguments |
| `dotnet test` per project, serially | `run_tests projects=[…]` — concurrent, one process per core |

**And the waiting is shorter.** A bare `run_tests` over a solution builds **once**, then runs each
test assembly directly where its runner allows — no MSBuild evaluation and no VSTest host per project
— measured **38 % faster** over five alternating pairs. From the second consecutive call of the same
tool, the response adds one 14-token line naming the plural you should have passed.

## 📊 4.6M tokens saved in one week — measured, not marketed

**508 Claude Code sessions**, one developer, one week, one **31,000-file** C# solution — replayed from
the raw transcripts. Every one of the **6,045 TerseSharp calls** was re-priced against the built-in it
replaced: `Read` against the real file on disk, `Grep` against a real `ripgrep` run of the same query,
`build` / `run_tests` / `git diff` against **3,767 real `dotnet` and `git` invocations**.

| | tokens |
|---|---|
| What the 6,045 TerseSharp calls actually cost | **2.41M** |
| What `Read` / `Grep` / `Bash` would have cost for the same answers | **6.97M** |
| **Burned for nothing, had it not been installed** | **4.56M — 2.9× the entire bill** |

**Then it compounds.** Every token put into context was re-sent **33×** (4.41B cache-read against
132.6M cache-write), so 4.56M tokens never injected are **~150M never re-read**. Across that whole week
the agent reached for a built-in **5 times with `Grep`, 11 with `Edit`** — an agent that distrusts its
MCP server falls back to the shell and spends *more* than with no server at all; this one didn't.

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
`Read offset/limit`, searches priced at the **median** grep output instead of the mean — and the saving
is still **2.09M tokens, 1.9×**. `analyze`, `search_text`, every edit tool and the whole `.resx` / XAML
/ Razor surface were scored **zero**, and no grep→`Read` follow-up chain was charged to the built-ins.

**What lost.** `diff_symbols` (13 calls) and `list_tests` (3) cost *more* than the raw command.
Measured, logged in the backlog, not hidden.
</details>

## 🚀 Install

```bash
dotnet tool install -g TerseSharp
terse install            # registers with every client it detects
```

Restart your agent and ask it something about your code — with no arguments the server walks up from
the current directory, finds your `.sln` / `.slnx` / `.slnf` / `.csproj` and loads it.

```bash
terse install --client cursor   # not detected? claude-code | cursor | vscode | windsurf
terse install --skill --guard   # teach your agent the tools, and block Read/Grep on C# (recommended)
terse doctor                    # SDK, MSBuild, workspace load, client registration, per-phase latency
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, no network call to answer a question. Inside a
session, `workspace_status verbose=true` answers `doctor`'s four actionable checks — `roslyn`,
`assets`, `guard coverage`, `phases` — without leaving the MCP.

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

## 💸 What each answer costs

| Question | Built-in tools | TerseSharp | |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → **~6,000 tok** | `get_type_outline` → **~450 tok** | **13×** |
| Read a whole `.cs` file | `Read` → the entire text | `read_text` answers the **outline** unless you ask for text | **3×** |
| Who calls this method? | `Grep` + follow-ups → **~4,000 tok** | `find_usages` → **~200 tok** | **20×** |
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed **declarations** | **10×** |
| Which rows does this checked-in table hold? | `Read` the whole `.md`, then grep it | `read_text columns="Finding,Tool" cellChars=60` | **~10×**, and ~11× again when the column is prose |
| What does this budgeted doc cost in tokens? | a build plus the E2E suite → **~10 min** | `read_text tokens=true` → **~3 s** | **200×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

<sub>Asserted by a token-budget suite on every push, not estimated.</sub>

- 🧠 **Semantic, never textual.** Real references, not string matches — every record tagged `EXACT` or
  `HEURISTIC`, so you always know what you're trusting.
- 🛡️ **Compile-gated edits.** An edit that introduces a compile error is rolled back before the agent
  reports it done; `undo_last_change` reverses the last one.
- 🧾 **No silently-ignored arguments.** A parameter a tool doesn't declare is refused by name — a
  listing that quietly dropped your `maxResults` is a wrong answer the agent can't detect.
- 🔄 **Always fresh.** A file you just created, or an edit from your IDE, is already in the answer.
- 🚫 **Never guesses.** Where it can't prove an answer it says so — a false positive costs an agent
  more than no answer.

## 🔒 Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and
names the replacement:

```
TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text.
```

A denial is not only a prohibition. It returns `additionalContext` — the **complete replacement call,
arguments filled in from the command it just denied** — which Claude Code places beside the tool
result: `Call this instead: get_file_outline path="src/App/OrderService.cs"`. A positive routing
instruction at the moment the agent is about to fall back beats a negation. **A batch is not denied
whole for one covered command in it.** When a compound command mixes commands the server answers with
commands it does not, the hook returns `updatedInput` with the covered ones stripped out and no
`permissionDecision` at all — so the rest of the batch runs under your normal permission rules, and
`additionalContext` names both what was stripped and the tool call that answers it. That rewrite is
only attempted where it is provably sound: every top-level separator is `&&`, `;` or a newline, a
pipeline containing a covered stage is dropped whole, and a command carrying `||`, a background `&`,
a subshell, a redirect, a substitution, a comment, any backslash escape, a mixed `;`/`&&` run or a
shell keyword falls back to denying the command as
before. `terse install --skill`
ships the skill that teaches the swaps; on any other agent, a short rule in `CLAUDE.md` / `AGENTS.md` /
`.cursorrules` does the same job.

<details>
<summary>Exactly what the guard denies, what it allows, and how to log or remove it</summary>

It covers `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln` and friends; the shell text
tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them — and, inside a .NET tree, any of them that
names a path operand at all, while a piped `head -40` reading stdin still runs. A denial names the
replacing call with the command's own arguments translated (`git log --oneline -1` → `history
maxResults=1`), a `2>&1` no longer forces a whole-command refusal, and a `$( )` no longer shadows the
real command. It also covers `dotnet build`/`test`/`format`/`clean`,
`dotnet watch build`/`test`, `msbuild`, `dotnet list package`; a **bare `sleep`** — a segment whose
command word is `sleep`, outside a `while`/`until`/`for` loop — because waiting is not work and nothing
replaces it; and the working-tree half of git —
`git status` and `git diff` in every flag and `-C` form, answered by `changed_files` and
`diff_symbols`, with `git diff --cached` routed to `changed_files staged=true`, a bare `git ls-files`
to `find_files tracked=true`, and a `git tag` **listing** to `history tags=true`.

Git rows fire only when the directory the command actually addresses sits under a
`.sln`/`.slnx`/`.slnf`/`.csproj` — the `-C` target or a directory operand before the working directory
— because the hook is installed user-wide, so `git -C ../notes status` is allowed. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, `git ls-files` with any option, and git mutation (`blame`,
`add`, `commit`, `push`, and every `git tag` that creates, annotates or deletes) are allowed —
nothing here replaces those. A denied command also tells the agent not to retry it in `Bash`.

Malformed hook input allows the call, so a guard fault can never wedge a session; remove the guard by
deleting the `terse guard` entry from Claude Code's `settings.json`.

`TERSE_GUARD_LOG=<path>` appends one JSON line per decision — tool, verdict, routing, reason, `cwd`,
session and transcript path, plus `standDown` when a project's `.terse.json` turned a denial back into
an allow. Opt-in, best-effort; a write failure never changes the verdict.
</details>

## 🧰 The tools

**88 tools.** One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a
success that costs nothing — every mutating tool answers in one line per changed file, with
`verbose=true` for the diff and `dryRun=true` to preview it. Any caveat prints in full.

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

Every read tool declares the MCP `readOnlyHint` annotation and every deleting tool declares
`destructiveHint`, so a client that gates parallel dispatch on those hints — Claude Code does — can fan
the reads out instead of running them one at a time. The build and test tools are deliberately off that
list: a build dispatched beside an edit is a race, not a saving.

<details>
<summary>Both test hosts, and shrinking the advertised surface</summary>

`build`, `run_tests`, `rerun_failed` and `list_tests` drive **both** test hosts: VSTest, and
Microsoft.Testing.Platform when `global.json` selects it (`"test": { "runner":
"Microsoft.Testing.Platform" }`, as xunit.v3, MSTest and NUnit projects use). That host rejects the
whole session over one VSTest-shaped argument, so the invocation — target switch, trx reporter,
timeout, filter — is rebuilt for it rather than patched. `list_tests` answers under both: the SDK
discards the platform's `--list-tests` output, so terse resolves each test project's `TargetPath` and
runs the test module itself.

An MCP server's fixed cost is its tool list, attached to every request — and past a certain size that
measurably costs tool-selection accuracy. `workspace_status` prints `advertised=<n> tools <t> tokens`,
and under `verbose=true` the whole surface beside it, so what a narrowing saves is read off the running
server. A **29,700-token ceiling over 88 tools** is asserted on every push, and it shrinks three ways —
all optional; the default advertises everything.

- **Automatically.** A solution holding no `.xaml`, `.razor` or `.resx` never sees those 31 tools —
  **57 tools, ≤22,650 tokens**. Load one that does and they come back, announced with
  `notifications/tools/list_changed`.
- **Per project** — a `.terse.json` beside your `.sln`, found by walking up from the server's directory
  and never above the repository root:

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
  measures **64 tools, ≤23,280 tokens**. An unknown key is reported rather than silently dropped, and the
  guard follows the file: a built-in whose every replacement you disabled is allowed again. Read once at
  startup, so restart your agent after changing it.
- **By profile.** `terse serve --tools core` (or `TERSE_TOOLS=core`) advertises the 21 tools that answer
  most questions; `--tools all` opts out of every narrowing.

A hidden tool is unadvertised, not removed — it still answers when called by name.
</details>

## 🚦 Make it refuse code that breaks your standards

An agent writes code that compiles and still isn't code you'd merge: a 40-branch method, an
`OrderManager`, an `async void`. The same `.terse.json` can make TerseSharp **reject the edit and say
why** — off unless you add a `policy` section, so nothing changes until you ask for it.

```json
{
  "policy": {
    "action": "reject",
    "cognitiveThreshold": 10,
    "rules": { "cognitiveComplexity": 150, "methodStatements": { "limit": 10, "action": "warn" } },
    "naming": { "interface": "^I[A-Z][A-Za-z0-9]*$" }
  }
}
```

```
ERROR PolicyViolation: the edit introduced 1 policy violation(s) and was rolled back:
TERSE100  src/Trading/OrderService.cs:41  OrderService.Reconcile  cognitive complexity 21 (210% of threshold 10) exceeds 150% (15)
  fix: split the member - each extracted part must be a real concept with a domain name, not DoThingPart1
remedy: fix the code above, or pass allowPolicy=true to apply it anyway; the response then names every rule it bypassed
```

Twelve rules, `TERSE100`–`TERSE111` — cognitive complexity, method statements, methods per type,
constructor dependencies, parameter count, method-name length, meaningless suffixes, naming per
declaration kind, `async void`, condition operands, chained references, nesting depth. **Every default
is ReSharper's**, not invented: the limits mirror `MaximumMethodStatements`, `MaximumMethodsInClass`,
`MaximumConstructorDependencies`, `MinimumMeaningfulMethodNameLength` and
`MeaninglessClassNameSuffixes`, and cognitive complexity is a **percentage of a threshold** exactly as
the JetBrains CognitiveComplexity plugin reports it — default `150%` of `10`, its own *Refactor me?*
band.

**Only what the edit introduces counts**, so a legacy file never blocks a clean edit to it. A rule can
`reject`, `warn` or be `off`; `allowPolicy=true` forces an edit through and the response names every
rule it bypassed; `"allowOverride": false` takes that away. `analyze` reports the same findings across
code you already have.

## 🎨 Markup and localization the compiler can't check

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers what no
text tool can. WPF has no compile-time binding check at all — a typo fails silently to debug output —
so `xaml_bindings validate=true` walks every path segment against the real symbol:

```
src/Views/BoundView.xaml:7  EXACT  TextBlock.Text  {Binding Symbol}  OK Symbol on Trading.OrderViewModel
src/Views/BoundView.xaml:9  EXACT  TextBlock.Text  {Binding Symbl}   ERROR no member 'Symbl'; nearest 'Symbol'
```

`razor_validate` catches the Blazor bug nothing else does — an attribute matching no `[Parameter]`
compiles clean and throws the first time the component renders, reported as `RZR002 … Card.Bogus
UNKNOWN_PARAMETER` at the `.razor` line. `resx_validate` reports missing values and placeholder
mismatches across a whole `.resx` family, instead of the ~36,000 tokens it costs to read one. And
`rename_symbol` carries into markup — rewriting `Click="…"` and `{Binding …}` only where an `x:Class`
or `x:DataType` *proves* the reference; anything else is listed `NOT rewritten` rather than guessed.

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
minutes (`--idle-minutes`), `--no-watch` turns the file watcher off, and responses are bounded and
declare their truncation — a 5,000-type solution answers in the same shape as a 50-type one. Every
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
session logs, measures where the tools cost you calls, minutes or tokens, and appends the findings to
[IMPROVEMENTS.md](IMPROVEMENTS.md) — the open table; closed rows keep their measurement in
[IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md). Skim the new rows, drop anything that leaked a path
or a secret, then open a PR with just that file. We work the backlog every weekend, so your friction
becomes next week's release.

Changes are in [CHANGELOG.md](CHANGELOG.md), releases in [RELEASING.md](RELEASING.md), security in
[SECURITY.md](SECURITY.md). MIT Licensed — see [LICENSE](LICENSE).

<p align="center">
  <sub>Built on <a href="https://github.com/dotnet/roslyn">Roslyn</a> and the
  <a href="https://github.com/modelcontextprotocol/csharp-sdk">MCP C# SDK</a>.</sub>
</p>
