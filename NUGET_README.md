# TerseSharp

**Roslyn for your coding agent. 88 tools over MCP.**

Your agent stops reading whole files, stops grepping for symbols, and stops turning the loop three times
to learn one thing — so it finishes sooner, costs less, and can only emit code you would merge.

```
Grep "Submit" -> 40 hits -> Read OrderService.cs (2,000 lines) -> Read the callers -> Edit by line
find_usages OrderService.Submit   ->  6 real references, each tagged EXACT, ~200 tokens, one call
```

## Five reasons it is installed

| | What it does | The number |
|---|---|---|
| **Token saving** | Answers semantically instead of dumping text — outlines, symbol ids, one record per line | **4.56M tokens** never sent in one measured week — **2.9× the entire bill** |
| **Speed** | Deletes round trips: batched reads, batched edits, one compile gate, concurrent test projects | a round trip costs **6.1 s** of model latency before the tool runs; `run_tests` **38 % faster** |
| **Code quality** | Every edit is compile-gated and rolled back if it breaks; `gate` runs analyze → format → cleanup → analyze again in one call | a broken edit never reaches your branch |
| **Control** | `.terse.json` policy rejects code that compiles but isn't mergeable; the guard denies `Read`/`Grep`/`dotnet build`; `--read-only` freezes everything | **12 rules**, `TERSE100`–`TERSE111`, ReSharper's own defaults |
| **Your stack** | Blazor/Razor, XAML for MAUI · WPF · WinUI · Avalonia, ASP.NET Core Minimal APIs, `.resx` localization, DI containers | answers **what the compiler itself cannot check** |

## Token saving — 4.6M tokens in one week, measured

**508 Claude Code sessions**, one developer, one week, one **31,000-file** C# solution — replayed from the
raw transcripts. Every one of the **6,045 TerseSharp calls** was re-priced against the built-in it
replaced: `Read` against the **real file on disk**, `Grep` against a **real `ripgrep` run of the same
query**, `build` / `run_tests` / `git diff` against **3,767 real `dotnet` and `git` invocations**.

| | tokens |
|---|---|
| What the 6,045 TerseSharp calls actually cost | **2.41M** |
| What `Read` / `Grep` / `Bash` would have cost for the same answers | **6.97M** |
| **Burned for nothing, had it not been installed** | **4.56M — 2.9× the entire bill** |

Every token put into context was re-sent **33×** (4.41B cache-read against 132.6M cache-write), so 4.56M
tokens never injected are **~150M never re-read**.

Push every assumption *against* TerseSharp — ranged reads priced as a perfect `Read offset/limit`,
searches priced at the **median** grep output — and the saving is still **2.09M tokens, 1.9×**. Two tools
lost: `diff_symbols` (13 calls) and `list_tests` (3) cost more than the raw command. Measured, logged in
the backlog, not hidden.

**The fallback rate is the real result.** Across the whole week the agent reached for a built-in **5 times
with `Grep`, 11 with `Edit`**. An agent that distrusts its MCP server falls back to the shell and spends
*more* than with no server at all; this one didn't.

**Per question, this is what the difference looks like:**

| Question | Built-in tools | TerseSharp | |
|---|---|---|---|
| What's on this 2,000-line type? | `Read` → **~6,000 tok** | `get_type_outline` → **~450 tok** | **13×** |
| Read a whole `.cs` file | `Read` → the entire text | `read_text` answers the **outline** unless you ask for text | **3×** |
| Who calls this method? | `Grep` + follow-ups → **~4,000 tok** | `find_usages` → **~200 tok** | **20×** |
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed **declarations** | **10×** |
| Which rows does this checked-in table hold? | `Read` the whole `.md`, then grep it | `read_text columns="Finding,Tool" cellChars=60` | **~10×** |
| What does this budgeted doc cost in tokens? | a build plus the E2E suite → **~10 min** | `read_text tokens=true` → **~3 s** | **200×** |
| Does this `{Binding}` bind? | **no static answer exists in WPF** | `xaml_bindings validate=true` | ∞ |

Asserted by a token-budget suite on every push, not estimated.

**Success costs nothing.** Every mutating tool answers in one line per changed file — `verbose=true`
returns the diff, `dryRun=true` previews it. Any caveat, rollback, timeout or zero-result run prints in
full, because condensing a result that carried a warning is a wrong answer the agent cannot detect.

## Speed — fewer turns of the loop

An agentic task is a loop: emit a call, wait, read the answer, emit the next one. Two things decide how
long it takes — **how many times the loop turns**, and **how much each answer costs to read.**

Mined from one week of real Claude Code sessions — 305 transcripts, 36,075 tool calls — **every round trip
costs 6.1 s of model latency before the tool even runs.** So a tool that *deletes a call* beats one that
merely shortens a response.

| Instead of | One call |
|---|---|
| grep, open the hit, open its callers | `find_usages` — real references, each tagged `EXACT` or `HEURISTIC` |
| read the file to find the member, then read the member | `get_file_outline` → paste the id straight into `get_symbol_source` |
| one search per identifier | `search_text queries=[…]` — 8 literals, one pass, records tagged `q1`..`qN` |
| a text hit, an outline, then a ranged read to find the method it sits in | `search_text containers=true` — the declaration on the record itself |
| trace a symbol by hand across the solution | `explore_symbol` · `impact_of` — definition, usages, implementations, blast radius |
| `git describe` to find where HEAD sits before a release | `history describe=true` — nearest tag, commits since it, short sha, dirty flag, one line |
| one read per file | `read_text paths=[…]` — up to 10 files, one answer |
| one ranged read per anchor in the same file | `read_text ranges=["42", "101-102"]` — discontinuous ranges, one answer |
| one edit call per site | `edit_text edits=[…]` — 25 edits across files, one write |
| edit, build, find you broke a caller, edit again | `replace_symbol symbolIds=[…]` — the member and its callers land in **one** compile gate |
| grep the test tree and guess what to run | `impact_of tests=true` — ready-made `run_tests test=` arguments |
| `dotnet test` per project, serially | `run_tests projects=[…]` — concurrent, one process per core |

**And the waiting itself is shorter.** A bare `run_tests` over a solution builds **once**, then runs each
test assembly directly where its runner allows — no MSBuild evaluation and no VSTest host per project —
measured **38 % faster** over five alternating pairs. A green `run_tests` repeated with nothing written
since is not re-run at all: it answers `UNCHANGED` with the previous verdict and its age. From the second
consecutive call of the same tool, the response adds one 14-token line naming the plural you should have
passed.

Every read tool declares the MCP `readOnlyHint` annotation and every deleting tool declares
`destructiveHint`, so a client that gates parallel dispatch on those hints — Claude Code does — fans the
reads out instead of running them one at a time. The build and test tools are deliberately off that list:
a build dispatched beside an edit is a race, not a saving.

## Code quality — the gate your agent cannot skip

An agent that writes code it cannot verify is a liability. TerseSharp holds the Roslyn compilation, so it
verifies *before* it reports success.

- **Compile-gated edits.** Every C#, Razor and refactoring edit is applied, re-analyzed, and **rolled back
  if it introduces a compile error** — in the changed projects *and* every project that transitively
  depends on them. The agent never gets to say "done" over a red tree. `undo_last_change` reverses the
  last one; `allowErrors=true` is the explicit opt-out.
- **Semantic, never textual.** Real references, not string matches — every record tagged `EXACT` or
  `HEURISTIC`, so you always know what you are trusting.
- **No silently-ignored arguments.** A parameter a tool does not declare is refused by name; a listing
  that quietly dropped your `maxResults` is a wrong answer the agent cannot detect.
- **Never guesses.** Where it cannot prove an answer it says so — `AmbiguousSymbol`, `SaturatedName`,
  `UNRESOLVED_CONTEXT`, `NOT rewritten`. A false positive costs an agent more than no answer.
- **Always fresh.** A file you just created, or an edit from your IDE, is already in the next answer — no
  reload, no stale index.

One call runs the whole ladder in the mandated order and answers one verdict line:

```
gate                 ->  analyze at info severity -> format -> cleanup fix=all -> analyze again
                     ->  clean  analyzed=42 fixed=3 remaining=0
gate dryRun=true     ->  verify instead of writing: nothing is modified
```

`analyze` reaches the info-severity CA/IDE rules a build never prints — `CA1822`, `CA1859`, `CA1806`,
`CA1865` — `format` and `cleanup` apply the fixable set through the same compile-gated path, and
`get_diagnostics` sweeps the whole solution for the consumer you broke. `build` answers a green build in
one line and a red one with **error-severity diagnostics only**; the warnings are a count until you ask
for them.

## Control — what your agent may and may not emit

Three independent layers, each off or on as you choose.

### 1. Reject code that compiles and still isn't code you'd merge

A 40-branch method, an `OrderManager`, an `async void`. A `policy` section in a `.terse.json` beside your
solution makes TerseSharp **reject the edit and say why** — off entirely unless you add it.

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

Twelve rules, `TERSE100`–`TERSE111`: cognitive complexity, method statements, methods per type,
constructor dependencies, parameter count, method-name length, meaningless suffixes, naming per
declaration kind, `async void`, condition operands, chained references, nesting depth. **Every default is
ReSharper's** — the limits mirror `MaximumMethodStatements`, `MaximumMethodsInClass`,
`MaximumConstructorDependencies`, `MinimumMeaningfulMethodNameLength` and `MeaninglessClassNameSuffixes` —
and cognitive complexity is a **percentage of a threshold**, exactly as the JetBrains CognitiveComplexity
plugin reports it: default `150%` of `10`, its own *Refactor me?* band.

**Only what the edit introduces counts**, so a legacy file never blocks a clean edit to it. A rule can
`reject`, `warn` or be `off`; `allowPolicy=true` forces an edit through and the response names every rule
it bypassed; `"allowOverride": false` takes that away. `analyze` reports the same findings across code you
already have.

### 2. Stop the agent falling back to `Read`, `Grep` and `dotnet build`

An agent that has TerseSharp installed and reaches for a built-in out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and names
the replacement:

```
TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text.
```

A denial is not only a prohibition. It returns `additionalContext` — the **complete replacement call,
arguments filled in from the command it just denied** — which Claude Code places beside the tool result:
`Call this instead: get_file_outline path="src/App/OrderService.cs"`. And **a batch is not denied whole
for one covered command in it**: when a compound command mixes commands the server answers with commands
it does not, the hook returns `updatedInput` with the covered ones stripped and no `permissionDecision` at
all, so the rest runs under your normal permission rules. The rewrite is only attempted where it is
provably sound: every top-level separator is `&&`, `;` or a newline, and a pipeline holding a covered stage
is dropped whole. A command carrying `||`, a background `&`, a subshell, a redirect, a substitution, a
comment, any backslash escape, a mixed `;`/`&&` run or a shell keyword is denied whole, as before.

It covers `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln` and friends; the shell text tools
(`grep`, `cat`, `sed`, `ls`, …) that name one of them — and, inside a .NET tree, any of them that names a
path operand at all, while a piped `head -40` reading stdin still runs; `dotnet build`/`test`/`format`/`clean`,
`dotnet watch build`/`test`, `msbuild`, `dotnet list package`; a **bare `sleep`** — a segment whose command
word is `sleep`, outside a `while`/`until`/`for` loop — because waiting is not work and nothing replaces
it; and the working-tree half of git — `git status` and `git diff` in every flag and `-C` form,
`git diff --cached` routed to `changed_files staged=true`, a bare `git ls-files` to
`find_files tracked=true`, and a `git tag` **listing** to `history tags=true`. A denial names the replacing
call with the command's own arguments translated — `git log --oneline -1` answers `history maxResults=1`.

Git rows fire only when the directory the command addresses sits under a `.sln`/`.slnx`/`.slnf`/`.csproj`,
because the hook is installed user-wide. Plain `.css`, `.js`, `dotnet restore`/`pack`/`publish`/`run`, and
git mutation (`blame`, `add`, `commit`, `push`, `tag` creation) are allowed — nothing here replaces those.
Malformed hook input allows the call, so a guard fault can never wedge a session. `TERSE_GUARD_LOG=<path>`
appends one JSON line per decision.

`terse install --skill` ships Claude Code the skill that teaches the swaps. On any other agent, a short
rule in `CLAUDE.md` / `AGENTS.md` / `.cursorrules` does the same job.

### 3. Decide which tools exist at all

```bash
terse serve --read-only      # every mutating tool refuses and touches nothing
terse serve --tools core     # advertise the 21 tools that answer most questions
```

An MCP server's fixed cost is its tool list, attached to every request — and past a certain size it
measurably costs tool-selection accuracy. `workspace_status` prints `advertised=<n> tools <t> tokens` —
asserted on every push against the list the server really sent, under a **29,800-token ceiling** — and lists
the whole surface beside it under `verbose=true`, so what a narrowing saves is read off the running server
rather than estimated. The surface shrinks three ways, all optional.

- **Automatically.** A solution holding no `.xaml`, `.razor` or `.resx` never sees those 31 tools —
  **57 tools, ≤24,600 tokens**. Load one that does and they come back, announced with
  `notifications/tools/list_changed`.
- **Per project.** The same `.terse.json`, found by walking up from the server's directory and never above
  the repository root, disables whole groups (`analysis` `build` `edit` `file` `git`
  `navigation` `project` `razor` `refactor` `resx` `workspace` `xaml`) or individual `names`, which outrank
  their group. That file measures **64 tools, ≤25,400 tokens**. The guard follows it: a built-in whose
  every replacement you disabled is allowed again. An unknown key is reported rather than silently
  dropped, and the file is read once at startup, so restart your agent after changing it.
- **By profile.** `--tools core`, or `TERSE_TOOLS=core`; `--tools all` opts out of every narrowing.

A hidden tool is unadvertised, not removed — it still answers when called by name.

## Your stack — Blazor, XAML, Minimal APIs, localization

TerseSharp holds the markup tree **and** the Roslyn compilation in one process, so it answers questions the
compiler itself cannot.

**Blazor / Razor.** `razor_outline`, `razor_component`, `razor_find`, `razor_bindings` and
`razor_codebehind` read a `.razor` file as a component — parameters, `@code`, `@bind`, event callbacks, the
partial class behind it — and `razor_set_attribute`, `razor_set_directive`, `razor_add_element` and
`razor_remove_element` edit it without line numbers. `razor_validate` catches **the Blazor bug nothing else
does**: an attribute matching no `[Parameter]` compiles clean and throws the first time the component
renders.

```
RZR002  src/App/Components/Dashboard.razor:14  Card.Bogus  UNKNOWN_PARAMETER  Card has no [Parameter] with that name - InvalidOperationException at render
```

**XAML — MAUI · WPF · WinUI · Avalonia.** `xaml_outline`, `xaml_names`, `xaml_find`, `xaml_resources`,
`xaml_resolve`, `xaml_styles`, `xaml_codebehind` and `xaml_localization` navigate the visual tree, the
resource graph and the code-behind bridge; `xaml_set_property`, `xaml_add_element` and `xaml_remove_element`
edit it; `xaml_validate` checks the document. WPF has **no compile-time binding check at all** — a typo
fails silently to debug output — so `xaml_bindings validate=true` walks every path segment against the real
symbol:

```
src/Views/BoundView.xaml:7  EXACT  TextBlock.Text  {Binding Symbol}  OK Symbol on Trading.OrderViewModel
src/Views/BoundView.xaml:9  EXACT  TextBlock.Text  {Binding Symbl}   ERROR no member 'Symbl' of 'Symbl' on Trading.OrderViewModel; nearest 'Symbol'
```

**ASP.NET Core — Minimal APIs, controllers, SignalR, DI.** `list_endpoints` returns **every** endpoint
registration in the solution — `MapGet`, `MapPost`, `MapControllers`, `MapHub` and friends — with the member
each one sits in, plus every Blazor `@page` route, instead of grepping `Program.cs` and hoping the
registrations all live there:

```
src/App/Components/Order.razor:1  EXACT      @page   /order/{Id:int}  in Order
src/Api/Routes.cs:22              HEURISTIC  MapGet  in Composition.Routes  MapGet("/orders/{id}", …)
```

`find_registrations` answers the other half — where a type is registered in a DI container, including
`AddSingleton`/`AddScoped`/`AddTransient`, keyed and `TryAdd` variants, open generics, factories and your own
`Add*` extension methods. Grep cannot answer that: the registration is rarely spelled the way the type is.

**`.resx` / `.resw` localization.** `resx_files`, `resx_get`, `resx_find` and `resx_usages` read a whole
resource family; `resx_set`, `resx_remove` and `resx_rename` edit it. `resx_validate` reports missing values
and placeholder mismatches across the family — instead of the ~36,000 tokens it costs to read one file.

**And rename carries across all of it.** `rename_symbol` rewrites `Click="…"` and `{Binding …}` in markup —
but **only where an `x:Class` or `x:DataType` proves the reference**. Anything else is listed
`NOT rewritten` rather than guessed, and so is every C# comment and string literal the old name survives in,
which no rename can decide.

Unity works too: Unity generates a real `.sln` with `Assembly-CSharp.csproj`, so outlines, `find_usages` and
compile-gated rename across your `MonoBehaviour`s all work — open the project in the editor once so the
project files exist. VB and F# projects load without breaking navigation, but the language tools are
C#-first.

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

No IDE, no licence, no Node, no Python, no API key, no network call to answer a question. Inside a session,
`workspace_status verbose=true` answers `doctor`'s four actionable checks — `roslyn`, `assets`,
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
`terse doctor` prints the path it read. **Updates** are one `HEAD` request to GitHub at most once a day, on
a background task; `TERSE_UPDATE=0` turns it off.

## All 88 tools

One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a success that costs
nothing.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** — replaces `Read`/`Grep` | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **What grep can't reach** | `find_registrations` (DI: open generics, factories, `Add*` extensions) · `list_endpoints` (ASP.NET Core `Map*` + Blazor `@page`) |
| **Analyze & clean** — replaces `dotnet format` | `analyze` · `format` · `cleanup` · `gate` (all four in the mandated order, one verdict line) · `clean` · `get_diagnostics` |
| **Edit** — replaces `Edit` on a `.cs` | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** — `package_list` replaces `dotnet list package` | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` (MSBuild's **evaluated** properties, each with the file that set it) · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` (`vulnerable=` / `outdated=`) · `package_add` · `package_remove` |
| **XAML** — MAUI · WPF · WinUI · Avalonia | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Localization** (`.resx`/`.resw`) | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Files** — replaces `Glob`/`ls`/`cat` | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Git** — replaces `git status`/`git diff`/`git diff --cached`/`git log`/`git tag --list` | `changed_files` (`staged=true`, `untracked=false`) · `diff_symbols` · `diff_text` · `history` (`tags=true` for the tag list) |
| **Build & test** — replaces `dotnet build`/`test` | `build` · `run_tests` · `rerun_failed` · `list_tests` |

`build`, `run_tests`, `rerun_failed` and `list_tests` drive **both** test hosts: VSTest, and
Microsoft.Testing.Platform when `global.json` selects it, as xunit.v3, MSTest and NUnit projects use.

## Safety and freshness

Every mutating tool takes `dryRun=true` (bar `undo_last_change`, which *is* the undo). C#, Razor and
refactoring edits are compile-gated — an edit that introduces a compile error is rolled back — and are
reversible with `undo_last_change`. The `.resx`, `.xaml`, Razor and project/package/solution writers are
surgical file writes outside undo, so preview those with `dryRun`. `--read-only` makes every mutating tool
refuse and touch nothing.

Four solutions stay loaded at once (`--max-workspaces`, `TERSE_MAX_WORKSPACES`), an idle one gives its
compilations back after 15 minutes (`--idle-minutes`), `--no-watch` turns the file watcher off, and
responses are bounded and declare their truncation. Every answer names its worktree and branch, and an
ambiguous request lists the candidates instead of guessing.

No licence, no IDE, no language server — anything that speaks MCP over stdio works. Git is read-only, and
databases, debugging and profiling are out of scope on purpose: six shallow tools would be worse than none.

## Contributing

Two rules that aren't negotiable: **a tool without an E2E test isn't done**, and **a tool that doesn't beat
the built-in it replaces doesn't ship**. The easiest way to help: clone the repo and run `/mine-sessions` in
Claude Code — it reads your own session logs, measures where the tools cost you calls, minutes or tokens,
and appends the findings to `IMPROVEMENTS.md`. Open a PR with just that file.

## Links

- [GitHub](https://github.com/amusleh-spotware-com/terse-sharp)
- [Changelog](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md)
- [Contributing](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CONTRIBUTING.md)
- [Security](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/SECURITY.md)

## License

MIT — see [LICENSE](https://github.com/amusleh-spotware-com/terse-sharp/blob/main/LICENSE).

Built on [Roslyn](https://github.com/dotnet/roslyn) and the
[MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).
