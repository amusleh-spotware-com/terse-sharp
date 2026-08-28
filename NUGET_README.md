# TerseSharp

**Your coding agent finishes sooner.**

A Roslyn-powered [MCP](https://modelcontextprotocol.io) server that answers C#/.NET **semantically** —
so your agent stops reading whole files, stops grepping for symbols, and stops turning the loop three
times to learn one thing.

## Why it makes agentic development faster

An agentic task is a loop: emit a call, wait, read the answer, emit the next one. Two things decide how
long it takes — **how many times the loop turns**, and **how much each answer costs to read.**

Mined from one week of real Claude Code sessions — 305 transcripts, 36,075 tool calls — **every round
trip costs 6.1 s of model latency before the tool even runs.** So a tool that *deletes a call* beats one
that merely shortens a response.

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

**And the waiting is shorter.** A bare `run_tests` over a solution builds **once**, then runs each test
assembly directly where its runner allows — no MSBuild evaluation and no VSTest host per project —
measured **38 % faster** over five alternating pairs. From the second consecutive call of the same tool,
the response adds one 14-token line naming the plural you should have passed.

## 4.6M tokens saved in one week — measured, not marketed

**508 Claude Code sessions**, one developer, one week, one **31,000-file** C# solution — replayed from
the raw transcripts. Every one of the **6,045 TerseSharp calls** was re-priced against the built-in it
replaced: `Read` against the **real file on disk**, `Grep` against a **real `ripgrep` run of the same
query**, `build` / `run_tests` / `git diff` against **3,767 real `dotnet` and `git` invocations**.

| | tokens |
|---|---|
| What the 6,045 TerseSharp calls actually cost | **2.41M** |
| What `Read` / `Grep` / `Bash` would have cost for the same answers | **6.97M** |
| **Burned for nothing, had it not been installed** | **4.56M — 2.9× the entire bill** |

Every token put into context was re-sent **33×** (4.41B cache-read against 132.6M cache-write), so
4.56M tokens never injected are **~150M never re-read**.

Push every assumption *against* TerseSharp — ranged reads priced as a perfect `Read offset/limit`,
searches priced at the **median** grep output — and the saving is still **2.09M tokens, 1.9×**. Two
tools lost: `diff_symbols` (13 calls) and `list_tests` (3) cost more than the raw command. Measured,
logged in the backlog, not hidden.

**The fallback rate is the real result.** Across the whole week the agent reached for a built-in **5
times with `Grep`, 11 with `Edit`**. An agent that distrusts its MCP server falls back to the shell and
spends *more* than with no server at all; this one didn't.

## Install

```bash
dotnet tool install -g TerseSharp
terse install            # registers with every client it detects
```

Restart your agent and ask it something about your code — with no arguments the server walks up from the
current directory, finds your `.sln` / `.slnx` / `.slnf` / `.csproj` and loads it.

```bash
terse install --client cursor   # not detected? claude-code | cursor | vscode | windsurf
terse install --skill --guard   # teach your agent the tools, and block Read/Grep on C# (recommended)
terse doctor                    # SDK, MSBuild, workspace load, client registration, per-phase latency
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, no network call to answer a question. Inside a
session, `workspace_status verbose=true` answers `doctor`'s four actionable checks — `roslyn`, `assets`,
`guard coverage`, `phases` — without leaving the MCP.

To configure MCP by hand:

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
`terse doctor` prints the path it read. **Unity** works — Unity generates a real `.sln` with
`Assembly-CSharp.csproj`, so outlines, `find_usages` and compile-gated rename across your
`MonoBehaviour`s all work. **Updates** are one `HEAD` request to GitHub at most once a day, on a
background task; `TERSE_UPDATE=0` turns it off.

## What each answer costs

| Question | Built-in tools | TerseSharp | |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → **~6,000 tok** | `get_type_outline` → **~450 tok** | **13×** |
| Read a whole `.cs` file | `Read` → the entire text | `read_text` answers the **outline** unless you ask for text | **3×** |
| Who calls this method? | `Grep` + follow-ups → **~4,000 tok** | `find_usages` → **~200 tok** | **20×** |
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed **declarations** | **10×** |
| Which rows does this checked-in table hold? | `Read` the whole `.md`, then grep it | `read_text columns="Finding,Tool"` | **~10×** |
| What does this budgeted doc cost in tokens? | a build plus the E2E suite → **~10 min** | `read_text tokens=true` → **~3 s** | **200×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

Asserted by a token-budget suite on every push, not estimated.

- **Semantic, never textual.** Real references, not string matches — every record tagged `EXACT` or
  `HEURISTIC`, so you always know what you're trusting.
- **Compile-gated edits.** An edit that introduces a compile error is rolled back before the agent
  reports it done; `undo_last_change` reverses the last one.
- **No silently-ignored arguments.** A parameter a tool doesn't declare is refused by name — a listing
  that quietly dropped your `maxResults` is a wrong answer the agent can't detect.
- **Always fresh.** A file you just created, or an edit from your IDE, is already in the answer.
- **Never guesses.** Where it can't prove an answer it says so — a false positive costs an agent more
  than no answer.

## Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and names
the replacement:

```
TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text.
```

A denial is not only a prohibition. It returns `additionalContext` — the **complete replacement call,
arguments filled in from the command it just denied** — which Claude Code places beside the tool result:
`Call this instead: get_file_outline path="src/App/OrderService.cs"`. **A batch is not denied whole for
one covered command in it.** When a compound command mixes commands the server answers with commands it
does not, the hook returns `updatedInput` with the covered ones stripped out and no `permissionDecision`
at all — the rest of the batch runs under your normal permission rules, and `additionalContext` names
both what was stripped and the tool call that answers it. The rewrite is only attempted where it is
provably sound: every top-level separator is `&&`, `;` or a newline, and a pipeline holding a covered
stage is dropped whole. A command carrying `||`, a background `&`, a subshell, a redirect, a
substitution, a comment, any backslash escape, a mixed `;`/`&&` run or a shell keyword is denied
whole, as before, and that denial names both halves of the
re-issue.

It covers `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln` and friends; the shell text
tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them; `dotnet build`/`test`/`format`/`clean`,
`dotnet watch build`/`test`, `msbuild`, `dotnet list package`; a **bare `sleep`** — a segment whose
command word is `sleep`, outside a `while`/`until`/`for` loop — because waiting is not work and nothing
replaces it; and the working-tree half of git —
`git status` and `git diff` in every flag and `-C` form, `git diff --cached` routed to
`changed_files staged=true`, a bare `git ls-files` to `find_files tracked=true`, and a `git tag`
**listing** to `history tags=true`. Git rows fire only when the directory the command addresses sits
under a `.sln`/`.slnx`/`.slnf`/`.csproj`, because the hook is installed user-wide. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, and git mutation (`blame`, `add`, `commit`, `push`, `tag`
creation) are allowed — nothing here replaces those. Malformed hook input allows the call, so a guard
fault can never wedge a session. `TERSE_GUARD_LOG=<path>` appends one JSON line per decision.

`terse install --skill` ships Claude Code the skill that teaches the swaps. On any other agent, a short
rule in `CLAUDE.md` / `AGENTS.md` / `.cursorrules` does the same job.

## The tools

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

`build`, `run_tests`, `rerun_failed` and `list_tests` drive **both** test hosts: VSTest, and
Microsoft.Testing.Platform when `global.json` selects it, as xunit.v3, MSTest and NUnit projects use.

**The advertised surface shrinks three ways**, all optional. A solution holding no `.xaml`, `.razor` or
`.resx` never sees those 31 tools — **57 tools, ≤22,650 tokens** instead of 88 and ≤27,800. A
`.terse.json` beside your `.sln` disables groups (`analysis` `build` `edit` `file` `git` `navigation`
`project` `razor` `refactor` `resx` `workspace` `xaml`) or individual `names`. And
`terse serve --tools core` advertises the 21 tools that answer most questions. A hidden tool is
unadvertised, not removed — it still answers when called by name.

## Make it refuse code that breaks your standards

An agent writes code that compiles and still isn't code you'd merge. A `policy` section in the same
`.terse.json` makes TerseSharp **reject the edit and say why** — off unless you add it.

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

Twelve rules, `TERSE100`–`TERSE111`: cognitive complexity, method statements, methods per type,
constructor dependencies, parameter count, method-name length, meaningless suffixes, naming per
declaration kind, `async void`, condition operands, chained references, nesting depth. **Every default
is ReSharper's** — the limits mirror `MaximumMethodStatements`, `MaximumMethodsInClass`,
`MaximumConstructorDependencies`, `MinimumMeaningfulMethodNameLength` and
`MeaninglessClassNameSuffixes` — and cognitive complexity is a **percentage of a threshold**, exactly
as the JetBrains CognitiveComplexity plugin reports it: default `150%` of `10`, its own *Refactor me?*
band.

A rejection names the rule, the declaration, the measured value against the allowed one, and a `fix:`
line. **Only what the edit introduces counts**, so a legacy file never blocks a clean edit to it. A
rule can `reject`, `warn` or be `off`; `allowPolicy=true` forces an edit through and the response names
every rule it bypassed; `"allowOverride": false` takes that away. `analyze` reports the same findings
across code you already have.

## Markup and localization the compiler can't check

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers what no
text tool can.

**Does this binding actually bind?** WPF has no compile-time binding check at all — a typo fails
silently to debug output. `xaml_bindings validate=true` walks every path segment against the real
symbol. **The Blazor bug nothing else catches:** an attribute matching no `[Parameter]` compiles clean
and throws the first time the component renders — `razor_validate` reports it at the `.razor` line.
**Translation bugs no compiler catches:** `resx_validate` reports missing values and placeholder
mismatches across a whole `.resx` family, instead of the ~36,000 tokens it costs to read one. And
`rename_symbol` carries into markup — rewriting `Click="…"` and `{Binding …}` only where an `x:Class` or
`x:DataType` *proves* the reference; anything else is listed `NOT rewritten` rather than guessed.

## Safety and freshness

Every mutating tool takes `dryRun=true` (bar `undo_last_change`, which *is* the undo). C#, Razor and
refactoring edits are compile-gated — an edit that introduces a compile error is rolled back — and are
reversible with `undo_last_change`. The `.resx`, `.xaml`, Razor and project/package/solution writers are
surgical file writes outside undo, so preview those with `dryRun`. `--read-only` makes every mutating
tool refuse and touch nothing.

Four solutions stay loaded at once (`--max-workspaces`, `TERSE_MAX_WORKSPACES`), an idle one gives its
compilations back after 15 minutes (`--idle-minutes`), `--no-watch` turns the file watcher off, and
responses are bounded and declare their truncation. Every answer names its worktree and branch, and an
ambiguous request lists the candidates instead of guessing. VB and F# projects load without breaking
navigation, but the language tools are C#-first.

## Contributing

Two rules that aren't negotiable: **a tool without an E2E test isn't done**, and **a tool that doesn't
beat the built-in it replaces doesn't ship**. The easiest way to help: clone the repo and run
`/mine-sessions` in Claude Code — it reads your own session logs, measures where the tools cost you
calls, minutes or tokens, and appends the findings to `IMPROVEMENTS.md`. Open a PR with just that file.

## Links

- [GitHub](https://github.com/amusleh-spotware-com/terse-sharp)
- [Changelog](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CONTRIBUTING.md)
- [Security](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/SECURITY.md)

## License

MIT — see [LICENSE](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE).

Built on [Roslyn](https://github.com/dotnet/roslyn) and the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
