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
| `Grep` to find callers | `find_usages(symbolId)` | real references; excludes comments, strings and unrelated matches |
| `Grep` for implementers | `find_implementations(symbolId)` | resolved through the interface |
| `Glob` / `ls` | `find_files(glob)` | `bin`, `obj`, `.git`, `node_modules` excluded |
| `Grep` in non-code files | `search_text` / `search_regex` | results tagged `HEURISTIC` |
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
2. **Pass symbol ids back, do not re-search.** Every result carries one, e.g.
   `M:Trading.OrderService.Submit(Trading.Order)`. It stays valid across edits.
3. **Read the confidence tag.** `EXACT` came from the Roslyn semantic model. `HEURISTIC` came from a
   text or index match — verify before acting on it.
4. **`dryRun: true` first on any edit you are unsure about.** You get the unified diff and nothing is
   written.
5. **Edits are compile-gated.** An edit that introduces a new compile error is rolled back and the
   error returned. Pass `allowErrors: true` only when you are mid-refactor on purpose.
6. **Several worktrees or repos open?** Pass `workspace:` with a path or worktree name. If it is
   ambiguous the server returns `AMBIGUOUS_WORKSPACE` and lists them rather than guessing — never
   assume it picked the right one.
7. **Truncation is explicit.** `truncated=true, total=N` means there are more; raise `maxResults`
   rather than assuming you saw everything.

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

`test=` is a **substring** match, so a name that is a prefix of another (`…Submits` vs `…SubmitsTwice`)
runs both — check `total=` to see what actually ran, and use `filter="FullyQualifiedName=<name>"` when
you need exactly one.
| a raw VSTest expression | `run_tests(filter)` |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` |
| the slowest N | `run_tests(slowest: 10)` |
| names without running | `list_tests(contains)` |

`total=0` with a `WARNING` line means **nothing ran** — a filter typo, not a green suite. A run that
produced no results at all reports `FAILED …, no test results were produced` and never `0 failures`.

## When a tool refuses

Errors are `ERROR <Code>` plus a `remedy:` line. `SymbolNotFound` suggests the nearest ids;
`OutOfWorkspace` means the path escaped the workspace root; `ReadOnly` means the server runs with
`--read-only`.
