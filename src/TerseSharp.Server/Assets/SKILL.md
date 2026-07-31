---
name: terse-sharp
description: Use when reading, searching, navigating, editing or refactoring C#/.NET code in a solution served by the TerseSharp MCP server. Teaches which TerseSharp tool replaces which built-in tool, so a .cs file is never read whole and a symbol is never found by text search.
---

# TerseSharp — use these instead of the built-ins

TerseSharp answers C# questions **semantically** from a Roslyn workspace. Reading a `.cs` file whole,
or grepping for a type name, costs 10-30x more tokens and returns matches that are not references.

## Replace the built-in on the left with the tool on the right

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline(path)` | every type and member with signatures and line ranges, no bodies |
| `Read` to see one method | `get_symbol_source(symbolId)` | that member only |
| `Read` to learn a class's API | `get_type_outline(symbolId)` | member list, no bodies |
| `Grep` for a type or member name | `search_symbols(query)` | declarations only; supports CamelHump (`OSvc` finds `OrderService`) |
| `Grep` to find callers | `find_usages(symbolId)` | real references, one line per file, each marked `src` or `test`; `containers=true` also names the member each usage sits in |
| `Grep` for implementers | `find_implementations(symbolId)` | resolved through the interface |
| `Glob` / `ls` | `find_files(glob)` | `bin`, `obj`, `.git`, `node_modules` excluded; `**/Views/*.xaml` spans directories, `*` and `?` stop at a separator |
| `Grep` in non-code files | `search_text` / `search_regex` | results tagged `HEURISTIC` |
| `Read` a `.xaml` file | `xaml_outline(path)` | element tree with `x:Name`/`x:Key`, no attributes |
| hunting a resource through `App.xaml` and every merged dictionary | `xaml_resolve(key)` | every declaration of the key with its scope, in one call |
| eyeballing a `{Binding}` | `xaml_bindings(path, validate: true)` | each path checked against the `x:DataType`/`d:DataContext` type through Roslyn |
| `Edit` a `.cs` file | `replace_symbol_body` / `replace_symbol` / `add_member` | addressed by symbol id, so line drift cannot break it |
| find-and-replace a name | `rename_symbol(symbolId, newName)` | solution-wide, includes interfaces, overrides and doc crefs |
| `Edit` a non-`.cs` file | `edit_text(path, oldText, newText)` | refuses an ambiguous match |
| `Bash: dotnet build` | `build` | deduplicated diagnostics, no MSBuild spew |
| `Bash: dotnet test` | `run_tests` | counters plus each failure's message, expected/actual and one source frame |
| re-running the ones that broke | `rerun_failed` | replays the previous run's failures, nothing else |
| `dotnet test --list-tests` | `list_tests(contains)` | names only, without running them |

## Working rules

1. **Start with `load_workspace`** (or let the server auto-discover). `workspace_status` shows what is
   loaded, on which git branch and worktree.
2. **Pass the reference back, do not re-search.** An outline prints `OrderService.Submit(Order)`;
   every tool that takes a `symbolId` accepts that name, the full documentation id
   (`M:Trading.OrderService.Submit(Trading.Order)`), a bare `Submit`, or any qualifier in between.
   A name that matches several symbols returns `AmbiguousSymbol` listing their ids — pick one, do not
   guess. Members a short name cannot address (constructors, operators, indexers, generics, explicit
   interface implementations) keep their documentation id in the outline; `ids=full` prints ids for
   everything.
3. **Read the confidence tag.** `EXACT` came from the Roslyn semantic model. `HEURISTIC` came from a
   text or index match — verify before acting on it.
4. **`dryRun: true` first on any edit you are unsure about.** You get the unified diff and nothing is
   written.
5. **Edits are compile-gated, and every edit reports its diagnostics.** Each mutation and each
   `dryRun` carries `errors=N (+D) warnings=N (+D)` for the changed projects and their dependents, so
   you do not need a separate `analyze` afterwards. A `dryRun` that *would* be rolled back says
   `WARNING … would be rolled back` and names the errors — the `(+0)` delta alone is not proof it is
   safe. An edit that introduces a new compile error is rolled back and the error returned. Pass
   `allowErrors: true` only when you are mid-refactor on purpose; it also skips the analysis.
6. **Several worktrees or repos open?** Pass `workspace:` with a path or worktree name. If it is
   ambiguous the server returns `AMBIGUOUS_WORKSPACE` and lists them rather than guessing — never
   assume it picked the right one.
7. **Truncation is explicit and tells you what to do.** `truncated=true, total=N` means there are
   more, and the same line names the parameter that narrows it — `- narrow with glob=`,
   `- narrow with minSeverity=, ids= or path=`. Follow that rather than re-running with a bigger
   `maxResults` and paying for the whole list.
8. **A tool never answers something it cannot prove.** `UNRESOLVED_CONTEXT` on a binding, `HEURISTIC`
   on a text match, `AmbiguousSymbol` on a name, `SaturatedName` when a name matches too many symbols
   to be safe — each means *the server declined to guess*, not that the thing does not exist. Narrow
   the question instead of treating it as a negative result.

## Running tests

`run_tests` reports `passed= failed= skipped= total= durationMs=` on every run, then one block per
failure: the message, the expected and actual values, and one workspace-relative `file:line` frame.
Fix the test from that block — do not shell out to `dotnet test` for the stack trace.

| Goal | Call |
|---|---|
| whole solution | `run_tests` |
| one project | `run_tests(project)` |
| one test, or a class/namespace prefix | `run_tests(test)` — not combined with `filter` |
| one case of a parameterized test | `run_tests(test)` with the case name — runs the whole theory, since the runner's `FullyQualifiedName` carries no arguments |
| a raw VSTest expression | `run_tests(filter)` |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` |
| the slowest N | `run_tests(slowest: 10)` |
| names without running | `list_tests(contains)` |

`test=` is a **substring** match, so a name that is a prefix of another (`…Submits` vs `…SubmitsTwice`)
runs both — check `total=` to see what actually ran, and use `filter="FullyQualifiedName=<name>"` when
you need exactly one.

`total=0` with a `WARNING` line means **nothing ran** — a filter typo, not a green suite. A run that
produced no results at all reports `FAILED …, no test results were produced` and never `0 failures`.

## When a tool refuses

Errors are `ERROR <Code>` plus a `remedy:` line. `SymbolNotFound` suggests the nearest names;
`AmbiguousSymbol` lists the candidate ids and says how many of the total it is showing;
`SaturatedName` means the name matched too many symbols to resolve safely — qualify it;
`OutOfWorkspace` means the path escaped the workspace root; `ReadOnly` means the server runs with
`--read-only`. Read the `remedy:` line and fix the call — do not fall back to `Read`/`Grep`, which is
the one outcome this server exists to prevent.

## XAML

`xaml_outline` (`depth=`, `filter=named|keyed`), `xaml_names` (`x:Name` and `x:Uid`), `xaml_resources`,
`xaml_resolve(key)`, `xaml_bindings(path, validate)`, `xaml_validate(path | scope: "solution")` and
`xaml_find(query, kind)` cover WPF, Avalonia (`.axaml`), WinUI and MAUI; the dialect is detected from
the root markup namespace and reported on every outline and validation.

`xaml_validate` reports duplicate `x:Key`/`x:Name` and resources that resolve to **no** declaration
anywhere under the workspace root — a key defined in `App.xaml` or a merged dictionary is not an
error. If any XAML file fails to parse it says so and switches resource checking off rather than
reporting every key in that file as missing.

`xaml_bindings(validate: true)` resolves the data context from `x:DataType` or
`d:DataContext="{d:DesignInstance …}"`, including inheritance from an ancestor, and walks each path
segment against the real symbol. WPF has no compile-time binding check at all, so this is the only
static answer available there. `UNRESOLVED_CONTEXT` means the context could not be determined — it is
not a claim that the binding is wrong.
