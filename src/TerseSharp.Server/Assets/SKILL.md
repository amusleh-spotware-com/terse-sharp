---
name: terse-sharp
description: Use when reading, searching, navigating, editing, refactoring, building or testing C#/.NET, XAML, .resx localization or Razor/Blazor in a solution served by the TerseSharp MCP server. Teaches which TerseSharp tool replaces which built-in, and how to drive all 83 of them, so a .cs file is never read whole, a symbol is never found by text search, and a .xaml, .resx or .razor file is never edited by line number.
---

# TerseSharp

TerseSharp answers C# and XAML questions **semantically**, from a Roslyn workspace that is already
loaded. Reading a `.cs` file whole, or grepping for a type name, costs 10-30x more tokens and returns
matches that are not references.

## 🚫 HARD GATE — the built-ins are the last resort, not the first

Before **every** `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call, answer one
question:

> **Is the target a `.cs`, `.razor`, `.cshtml`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`/`.slnf`, `.xaml`,
> `.axaml`, `.paml`, `.resx` or `.resw` file, or a question about C# symbols, references, diagnostics,
> builds or tests?**

**If yes, the built-in is forbidden.** Not "discouraged" — forbidden. There is a TerseSharp tool for
it in the table below.

**The shell does not launder it.** `grep`, `rg`, `find`, `fd`, `cat`, `head`, `tail`, `sed`, `awk`,
`ls`, `dir`, `tree`, `wc`, `nl`, `findstr`,
`type`, `dotnet build`, `dotnet test`, `dotnet msbuild` and `msbuild` run through `Bash` are built-ins
too and are covered by the same gate — including later in a compound command
(`cd src && dotnet test`).

`dotnet format` and `dotnet clean` are covered too: `format`, `cleanup fix=…`, `cleanup verify=true` and `clean` replace them. `dotnet restore`, `pack`, `publish`, `run` and `tool` are **not** covered: no
TerseSharp tool replaces them, so shelling out is the right call.

**Banned reasoning.** Every one of these has produced a breach: "just this once" · "Grep is faster" ·
"I only need one line" · "the tool errored so I'll use Grep" · "I
already started with Read, I'll stay consistent" · "it's a tiny file" · "I'll just check quickly".

**"The workspace looked stale" is not on that list because it is no longer true.** The server watches
the tree and compares content before it changes anything, so an external edit, a `git checkout`, or a
file you just created is already in the answer. Never `Read` a `.cs` file to check whether the tool
saw it, and never reload out of superstition — `workspace_status` shows the counters if you genuinely
doubt it.

**An `ERROR` is not permission to switch toolchains.** Every failure carries a `remedy:` line — read it
and fix the *call*. A rejected glob means fix the glob. `AmbiguousSymbol` means pick a candidate.
`UNRESOLVED_CONTEXT` and `HEURISTIC` mean narrow the question. None of them means "fall back to Grep".

**If you do drop to a built-in, say so in the same message, with the reason.** The only valid reasons:
the file is outside any loaded workspace, or the server is genuinely unreachable after a real attempt.
A silent drop is the breach, even when the reason would have been valid.

**Tripwires — stop and re-read this gate if any fires:**
- You are about to `Read` a `.cs`, `.xaml` or `.resx` file.
- Your built-in calls on C# outnumber your TerseSharp calls for this task.
- You have used only `search_text` and no `search_symbols`, `find_usages` or `get_file_outline` — you
  are text-grepping through a semantic server.
- You are about to `Edit` a `.xaml`, `.resx` or `.razor` by line number.
- You are about to open a `*_razor.g.cs` under `obj/` — that file is generated; edit the `.razor`.

## Replace the built-in on the left

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline(path)` | every type and member with signatures and line ranges, no bodies |
| `Read` to see one method | `get_symbol_source(symbolId)` | that member only |
| `Read` to learn a class's API | `get_type_outline(symbolId)` | member list, no bodies |
| `Grep` for a type or member name | `search_symbols(query)` | declarations only; CamelHump (`OSvc` finds `OrderService`) |
| `Grep` to find callers | `find_usages(symbolId)` | real references, one line per file, each marked `src` or `test` |
| `Grep` for implementers | `find_implementations(symbolId)` | resolved through the interface |
| `Glob` / `ls` | `find_files(glob)` | `bin`, `obj`, `.git`, `node_modules` excluded |
| `Grep` in non-code files | `search_text` / `search_regex` | tagged `HEURISTIC` |
| `Read` a non-`.cs` file | `read_text(path)` | line ranges, bounded response |
| `Edit` a `.cs` file | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` | addressed by symbol, immune to line drift, compile-gated |
| `Edit`/`Write` a non-`.cs` file | `edit_text` · `write_text` | refuses an ambiguous match |
| `Write` a **new** `.cs` file | `write_text(path, content, force: true)` | no symbol tool creates a file; the new type is resolvable on the very next call |
| find-and-replace a name | `rename_symbol(symbolId, newName)` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `Read` a `.xaml` file | `xaml_outline(path)` | element tree with `x:Name`/`x:Key`, no attributes |
| `Edit` a `.xaml` file | `xaml_set_property(path, target, property, value)` | addressed by element, formatting preserved |
| `Read` a `.xaml.cs` to see what the markup wires | `xaml_codebehind(path)` | `x:Class` plus every handler |
| hunting a resource through `App.xaml` | `xaml_resolve(key)` | every declaration with its scope, one call |
| eyeballing a `{Binding}` | `xaml_bindings(path, validate: true)` | each path type-checked through Roslyn |
| "where is `IFoo` registered?" | `find_registrations(query)` | open generics, factories and `Add*` extensions defeat grep |
| "what endpoints exist?" | `list_endpoints()` | every `Map*` with the member it sits in |
| orienting on a symbol | `explore_symbol(symbolId)` | signature, doc, reach, implementations, XAML sites in one call |
| judging a rename before doing it | `impact_of(symbolId)` | every affected file, XAML site and recompiling project |
| "why does this control look like that" | `xaml_styles(typeName)` | implicit and keyed styles with the `BasedOn` chain |
| "is this element translated" | `xaml_localization()` | every `x:Uid` joined to its `.resx`/`.resw` entry |
| `Read` a `.resx`/`.resw` | `resx_get(path, cultures)` | every key with its value per culture; absent ones print `MISSING` |
| `Grep` a resource key | `resx_find(query)` | key, value or comment, across every family |
| "is this key still used" | `resx_usages(key)` | designer property through Roslyn, plus `GetString`, localizer, `x:Uid`, Razor |
| "which strings are untranslated" | `resx_validate()` | missing, placeholder mismatch, duplicate, orphan, empty, stale designer |
| `Edit` a `.resx`/`.resw` | `resx_set` · `resx_remove` · `resx_rename` | one `<data>` element rewritten; header, order, indentation, line endings and BOM kept |
| `Read` a `.razor` or `.cshtml` file | `razor_outline(path)` | directives, component tree and `@code` members, each component resolved to its type |
| "how do I use this component" | `razor_component(name)` | every `[Parameter]`, which are `[EditorRequired]`, from source **or** a referenced package |
| `Grep` a tag, directive or route in markup | `razor_find(query, kind)` | component, element, attribute, directive, expression or route |
| `Edit` a `.razor` file | `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` | element-addressed, formatting preserved, compile-gated through the Razor generator |
| "is this `@bind` real" | `razor_bindings(path, validate: true)` | each `@bind`/`@on`/`@ref`/`asp-for` resolved against the component type |
| "what breaks at render" | `razor_validate()` | unknown parameter, duplicate route, unregistered `@inject` — none of which the compiler reports |
| `Bash: dotnet build` / `msbuild` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` / `vstest` | `run_tests` | counters plus each failure's message, expected/actual, one source frame |
| re-running what broke | `rerun_failed` | replays the previous failures only |
| `dotnet test --list-tests` | `list_tests(contains)` | names without running |
| `dotnet format whitespace` / an IDE inspection | `analyze` · `format` · `cleanup` | compiler + every referenced analyzer + dead code |
| `dotnet format style` / `dotnet format analyzers` | `cleanup fix=style\|analyzers\|all` | applies the referenced analyzers' code fixes, compile-gated, `UNFIXED <id>` for what no fixer covers |
| `dotnet format --verify-no-changes` | `format verify=true` · `cleanup verify=true` | one verdict line (`clean` or `VERIFY_FAILED n`), no diff |
| `Bash: dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's file locks |
| editing a `.csproj` by hand | `project_*` · `package_*` · `solution_*` | CPM-aware, containment-checked |

## The whole surface, by job

**Workspace** — `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` ·
`list_projects`. Start with `workspace_status`; the server usually auto-discovers the solution. Its
last line reports freshness — `watch=active gen=c12/p1/x3/r0 pending=0 lastSyncMs=8 gaps=0`: the
watcher state, the per-kind generation counters (Code / Project / Xaml / Resx), how many paths are
waiting to be examined, and how many watcher events were lost. `load_workspace(reload: true)` forces a
re-read from disk; you should almost never need it. The line after it reports the workspace index —
`index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-)
documents=9/128 parses=9`.

**Navigate** — `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` ·
`get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of`.

**.NET semantics grep cannot reach** — `find_registrations` (DI) · `list_endpoints` (ASP.NET Core).

**Analyse** — `analyze` (compiler + analyzers + dead code, down to `info`; `sinceLast=true` reports
only what appeared since the previous run of the same scope, plus what was fixed) ·
`get_diagnostics` · `format` (whitespace; `verify=true` for a one-line verdict, `path=` takes a file, a directory or a glob) · `cleanup` (`fix=usings` by default; `fix=style|analyzers|all` applies the referenced analyzers' code fixes with `ids=` and `severity=` filters, reports `UNFIXED <id>` for what no fixer covers, and never rewrites generated code) · `clean` (deletes `bin`/`obj`, `dryRun=true` to preview, not covered by `undo_last_change`).

**Edit** — `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol`
· `undo_last_change`.

**Refactor** — `extract_interface` · `move_type_to_file` · `move_type_to_namespace` ·
`change_signature`.

**Projects** — `solution_projects` · `solution_add_project` · `solution_remove_project` ·
`project_create` · `project_properties` · `project_set_property` · `project_add_reference` ·
`project_remove_reference` · `package_list` · `package_add` · `package_remove`.

**XAML** — `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` ·
`xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` ·
`xaml_set_property` · `xaml_add_element` · `xaml_remove_element`.

**Localization** — `resx_files` (every `.resx`/`.resw` family with its cultures, counts, missing total and
designer) · `resx_get` (keys and values per culture; `MISSING` where a translation is absent; `values=false`
lists keys only) · `resx_find` (key, value or comment) · `resx_usages` (Roslyn-resolved designer property
plus the textual forms, with `composedLookups=` so an empty answer is never claimed as proof) · `resx_set`
(one key or `entries` as `Key=Value` lines; creates a missing culture file from the neutral header) ·
`resx_remove` · `resx_rename` · `resx_validate` (`RESX001` missing · `RESX002` placeholder mismatch ·
`RESX003` unused, `includeUnused` only · `RESX004` duplicate · `RESX005` orphan · `RESX006` empty ·
`RESX007` trimmed whitespace · `RESX008` unsorted · `RESX009` stale designer).
**Razor / Blazor** — `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` ·
`razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` ·
`razor_remove_element` · `razor_set_directive`.

**Files** — `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex`.

**Build and test** — `build` · `clean` · `run_tests` · `rerun_failed` · `list_tests`.

## Working rules

1. **Address a symbol by the name a response printed.** An outline prints
   `OrderService.Submit(Order)`; every tool taking a `symbolId` accepts that, the full documentation
   id (`M:Trading.OrderService.Submit(Trading.Order)`), a bare `Submit`, or any qualifier in between.
   A name matching several symbols returns `AmbiguousSymbol` listing their ids — **pick one, never
   guess**. Constructors, operators, indexers, generics and explicit interface implementations keep
   their documentation id in outlines, because a name cannot address them.
2. **Read the confidence tag.** `EXACT` came from the Roslyn semantic model. `HEURISTIC` came from a
   text or index match — verify before acting on it.
3. **`dryRun: true` first on any edit you are unsure about.** You get the unified diff, the diagnostic
   counts, and nothing is written.
4. **Every edit reports its diagnostics.** Each mutation and each `dryRun` carries
   `errors=N (+D) warnings=N (+D)` for the changed projects and their dependents — you do not need a
   separate `analyze` afterwards. A `dryRun` that *would* be rolled back says
   `WARNING … would be rolled back` and names the errors; a `(+0)` delta alone is **not** proof the
   edit is safe.
5. **Edits are compile-gated.** An edit introducing a new compile error is rolled back and the error
   returned. `allowErrors: true` opts out — use it only mid-refactor on purpose.
6. **Truncation tells you what to do.** `truncated=true, total=N` is followed by
   `- narrow with <parameter>`. Follow that, rather than re-running with a bigger `maxResults` and
   paying for the whole list.
7. **Several worktrees or repos open?** Pass `workspace:`. An ambiguous request returns
   `AmbiguousWorkspace` listing the candidates rather than guessing — never assume it picked right.
8. **A tool never answers something it cannot prove.** `UNRESOLVED_CONTEXT`, `HEURISTIC`,
   `AmbiguousSymbol`, `SaturatedName` all mean *the server declined to guess*, not that the thing does
   not exist. Narrow the question; do not treat it as a negative result.
9. **External edits are picked up automatically.** A file you or the user just created or changed —
   through `write_text`, an IDE, `git checkout`, a formatter — is visible to every semantic tool on
   the next call. Never re-`Read` a file to check, never reload "just in case". Creating a `.cs` file
   is `write_text(path, content, force: true)`; `add_member` and `replace_symbol` work on it
   immediately. When `undo_last_change` answers `nothing to undo - N snapshot(s) were dropped after an
   external change to …`, that is the server refusing to overwrite someone else's edit — re-apply the
   change deliberately instead of retrying the undo.

10. **`resx_*` edits are outside `undo_last_change`.** Its history holds Roslyn solution snapshots, and a
    `.resx`, `.resw` or `.xaml` write is a file write. Use `dryRun: true` first; the diff is your undo.

11. **Ask a repeat XAML or resx question freely — the second call is free.** `xaml_resolve`,
    `xaml_validate`, `xaml_styles`, `xaml_localization`, `xaml_find`, every `resx_*` tool,
    `find_registrations` and `list_endpoints` share **one index per workspace** that refreshes itself
    when a file changes. The first call builds it; every call after that reads no file at all until
    something on disk moves, and then only the changed files are re-parsed. So do **not** batch
    questions "to save a scan", do not cache answers yourself, and never fall back to globbing or
    grepping the tree because you think re-asking is expensive — `find_files` on `**/*.xaml` answers
    "which files exist", which is almost never the question; `xaml_resolve`, `xaml_styles` and
    `xaml_find` answer "where is this key / style / name", from the same index, for less.
    The exception, so you can plan around it: `xaml_find`, the XAML sweep inside `find_usages` /
    `rename_symbol` / `explore_symbol`, and `xaml_validate includeUnused=true` need the parsed
    document of every file, not just its index record, so beyond 128 cached documents they re-parse.
    Those four are worth asking once and keeping; the rest are free to repeat.

## Localization (`.resx` / `.resw`)

Never `read_text` a `.resx`: `resx_get` gives the same keys for a fraction of the tokens, and
`cultures: "all"` puts every translation of a key on one line with `MISSING` where one is absent.

`resx_validate` is the tool with no built-in equivalent. `RESX002` compares the placeholder set of each
translation against the neutral value and separates the two failures — a **missing** `{n}` leaves text
unfilled, an **extra** `{n}` makes `string.Format` throw in that locale only. `RESX003` (unused) is
`includeUnused: true`, always `HEURISTIC`, and turns advisory when `composedLookups > 0`, because a key
built at runtime (`GetString("Error_" + code)`) cannot be seen. Never delete a key on `RESX003` alone.

The writers are surgical: only the addressed `<data>` element is rewritten, so the schema header,
`resheader` rows, entry order, indentation, line endings and byte order mark survive; a result that would
not parse is refused. Typed and binary entries (`type=`, `mimetype=`) are reported `TYPED`/`BINARY` and
passed through — `resx_set` on one is refused rather than corrupting it. `resx_remove` covers every file of
the family unless you pass `culture:`, and refuses while the key is still referenced unless `force: true`.
`resx_rename` is all-or-nothing across the family plus the references it can prove.

A culture file is recognised by a lowercase BCP-47 segment (`Strings.fr.resx`, `Strings.pt-BR.resx`);
`Order.Web.resx` is a neutral file, not a `Web` culture. WinForms designer resources are detected and left
out of the translation lint. Adding a key to a family with a `*.Designer.cs` reports `designerStale=true`:
regenerate it before referencing the key from C#, or the build will not see it.

## XAML

Covers **WPF, Avalonia (`.axaml`), WinUI and MAUI**; the dialect is detected from the root markup
namespace and reported on every outline and validation.

`xaml_resolve`, `xaml_validate`, `xaml_styles`, `xaml_localization` and `xaml_find` all answer from
**one** resource index per workspace. `xaml_resolve`, `xaml_validate`, `xaml_styles` and
`xaml_localization` answer from its per-file records, so the second and every later question about the
same solution costs no file read at all — resolve five keys as five calls rather than trying to batch
them, and never glob the tree instead. `xaml_find` needs the parsed documents, so on a solution with
more than 128 XAML files it re-parses beyond the cache; ask it once and keep the answer.

`xaml_validate` reports duplicate `x:Key`/`x:Name` and resources that resolve to **no** declaration
anywhere under the workspace root — a key defined in `App.xaml` or a merged dictionary is not an
error. Pass `scope: "solution"` to check every file. If a XAML file fails to parse it says so and
switches resource checking off rather than reporting every key in that file as missing.

`xaml_bindings(validate: true)` resolves the data context from `x:DataType` or
`d:DataContext="{d:DesignInstance …}"`, including inheritance from an ancestor, and walks each path
segment against the real symbol. WPF has no compile-time binding check at all, so this is the only
static answer available there. `UNRESOLVED_CONTEXT` means the context could not be determined — it is
not a claim that the binding is wrong.

`rename_symbol` rewrites XAML too: rename a code-behind handler and the `Click="…"` follows, rename a
bound property and `{Binding …}` follows — but **only** where an `x:Class` or `x:DataType` proves the
reference. Anything else is listed `NOT rewritten`; **read that list after every rename.**
`find_usages` shows the same XAML sites, so check the blast radius before renaming.

`xaml_set_property`, `xaml_add_element` and `xaml_remove_element` address an element by the path
`xaml_outline` prints, by `#Name` or by `key=Key`, edit in place so formatting survives, and refuse an
edit whose result would not parse. An ambiguous target is refused with the count, never guessed.

`xaml_validate scope=solution includeUnused=true` also reports `x:Key` and `x:Name` declarations that
no XAML attribute and no C# string literal references — `HEURISTIC`, because reflection can reach
them.

## Razor and Blazor

Razor is compiled by a **Roslyn source generator**, so the loaded workspace already knows the type of
every `<Card />`. Every Razor answer is reported at the `.razor` line — a path under `obj/` or a
`*_razor.g.cs` name never appears in a response, and you must never edit one.

`razor_outline` prints the file's directives, its element tree and the members declared in `@code`,
tagging each component `EXACT <type>` when it resolves and `HEURISTIC unresolved` when it does not —
an unresolved capitalised tag is a real defect (it renders as raw HTML), not a tool failure.

`razor_validate` owns the checks the compiler does not make: `RZR001` unknown component · `RZR002` an
attribute that matches no `[Parameter]` (compiles clean, throws at render) · `RZR003` a missing
`[EditorRequired]` · `RZR004` a `@bind` with no setter · `RZR005` a route parameter with no property ·
`RZR006` two components on one route · `RZR007` a mistyped `@ref` · `RZR008` an orphan `.razor.css` ·
`RZR009` an `@inject` nothing registers (`HEURISTIC`) · `RZR010` markup that will not parse. Razor's
own `RZ####` diagnostics come from `build`, not from `get_diagnostics`.

Razor edits are **compile-gated**: the tool writes the new text into the workspace, the generator
re-runs, and an edit that adds a compile error is rolled back with the error at its `.razor` line.
`dryRun: true` shows the diff and the diagnostic counts without writing; `allowErrors: true` skips
the regeneration when you are mid-refactor.

`razor_outline` hides plain HTML by default — it lists directives, components, anything wired with
`@bind`/`@on*`/`@ref`, and the `@code` members. Pass `elements: true` for the whole tree.

**The C# edit tools work on `@code` members.** `replace_symbol_body`, `replace_symbol`,
`delete_symbol` and `add_member` recognise a member declared in a `.razor` and edit the Razor source
through the generator's mapping — you do not need a Razor-specific tool for the code half of a
component. `rename_symbol` on a component renames the **file** (its class name comes from the file
name), its `.razor.cs`/`.razor.css`/`.razor.js` siblings and every markup usage; reload the workspace
afterwards.

`workspace_status` reports `razor=<n> files generator=ok|unavailable`. **`generator=unavailable`
means the Razor source generator did not run** — usually the target SDK is newer than the Roslyn the
server ships. Component and parameter answers are then unavailable rather than empty, and
`razor_validate` says so as `RZR000` instead of reporting rules it cannot compute.

## Running tests

`run_tests` reports `passed= failed= skipped= total= durationMs=` on every run, then one block per
failure: the message, expected and actual values, and one workspace-relative `file:line` frame. Fix
the test from that block — do not shell out to `dotnet test` for the stack trace.

| Goal | Call |
|---|---|
| whole solution | `run_tests` |
| one project | `run_tests(project)` |
| one test, or a class/namespace prefix | `run_tests(test)` — not combined with `filter` |
| a raw VSTest expression | `run_tests(filter)` |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` |
| the slowest N | `run_tests(slowest: 10)` |
| names without running | `list_tests(contains)` |

`test=` is a **substring** match, so a name that is a prefix of another (`…Submits` vs
`…SubmitsTwice`) runs both — check `total=`, and use `filter="FullyQualifiedName=<name>"` for exactly
one.

`total=0` with a `WARNING` means **nothing ran** — a filter typo, not a green suite. A run that
produced no results reports `FAILED …, no test results were produced` and never `0 failures`.

## When a tool refuses

Errors are `ERROR <Code>` plus a `remedy:` line. `SymbolNotFound` suggests the nearest names;
`AmbiguousSymbol` lists the candidates and says how many of the total it shows; `SaturatedName` means
the name matched too many symbols to resolve safely — qualify it; `OutOfWorkspace` means the path
escaped the workspace root; `ReadOnly` means the server runs with `--read-only`.

Read the `remedy:` and fix the call. Falling back to `Read`/`Grep` is the one outcome this server
exists to prevent.
