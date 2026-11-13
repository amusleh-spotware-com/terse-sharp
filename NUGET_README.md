# TerseSharp

**Roslyn for your coding agent — 86 tools over MCP.**
Fewer tokens. Fewer turns of the loop. Only code you would merge.

Coding agents read C# the hard way. They open whole files, grep for names, and guess at line numbers.
TerseSharp gives them the compiler's view instead. It loads your solution into Roslyn once and answers
by meaning, not by text.

> **Without it:** grep `Submit` → 40 hits → read a 2,000-line file → read three callers → edit by line number.
> **With it:** `find_usages` → the 6 real references, ~200 tokens, one call.

| 💸 Save tokens | ⚡ Go faster | 🧪 Ship better code |
|---|---|---|
| **4.56M tokens** not spent in one measured week, **2.9×** the whole bill | every round trip it removes saves **6.1 s** of model wait | every edit is **compile-gated** and checked against your **code health** bar |

## Install

```bash
dotnet tool install -g TerseSharp
terse install --skill --guard
```

Restart your agent and ask it about your code. TerseSharp finds your `.sln`, `.slnx` or `.csproj` on its
own. It works with Claude Code, Cursor, VS Code, Windsurf and any MCP client over stdio. No IDE, no
licence, no API key. It runs on the .NET 11 runtime; a solution pinned to the .NET 10 SDK still loads.
`terse doctor` checks the setup.

## 💸 Save tokens

We replayed **508 real Claude Code sessions** on a 31,000-file solution. Each of the **6,045** TerseSharp
calls was re-priced against the built-in tool it replaced, run for real.

| | Tokens |
|---|---:|
| What TerseSharp actually cost | **2.41M** |
| What `Read`, `Grep` and the shell would have cost | **6.97M** |
| **Saved** | **4.56M (2.9× the bill)** |

| You ask | Built-in tools | TerseSharp | Saving |
|---|---|---|:---:|
| What is on this 2,000-line type? | read it all, ~6,000 tokens | `get_type_outline`, ~450 | **13×** |
| Who calls this method? | grep and follow-ups, ~4,000 | `find_usages`, ~200 | **20×** |
| Rename it everywhere | ~5,000, and it misses the interface | `rename_symbol`, ~150, correct | **30×** |
| Why is the build red? | ~8,000 tokens of MSBuild log | `build`, ~600 | **13×** |
| What did I just change? | the whole patch | `diff_symbols`, the changed declarations | **10×** |

With every assumption pushed against TerseSharp, the saving is still **2.09M tokens, 1.9×**. A successful
edit answers in one line; failures, rollbacks and warnings always print in full.

## ⚡ Go faster

Measured over 36,075 real tool calls, **every turn of the agent's loop costs 6.1 seconds** before the
tool even runs. The fastest call is the one the agent never needs to make.

- **One call instead of a chain.** `get_symbol usages=true` answers what a symbol is, who uses it and what implements it.
- **Batches everywhere.** Read 10 files, apply 25 edits, or search 10 terms in one call.
- **Edits that land together.** Change a signature and its callers in one compile-gated step.
- **Fast tests.** `run_tests` builds once and runs test projects in parallel, **38% faster**. A repeat
  with nothing changed answers instantly.
- **Parallel-safe reads.** Read tools are marked read-only, so clients run them side by side.

## 🧪 Ship better code

**Every edit is compile-gated.** An edit that adds a compile error is rolled back, in the changed project
and in every project that depends on it. Results are tagged `EXACT` or `HEURISTIC`, and the server never
guesses.

**Code health, scored on every edit.** Every edited file gets a score from 1 to 10, in the style of
CodeScene's Code Health. A file starts at 10 and each smell takes points off:

| Smell | Triggers when a method… | Penalty |
|---|---|:---:|
| Brain Method | is complex **and** large (on top of the two below, so 2.5 in all) | 1.5 |
| Deep Nested Complexity | nests 4 or more levels of `if`, loops or `switch` | 0.75 |
| Complex Method | has a cyclomatic complexity of 10 or more | 0.5 |
| Large Method | has more than 70 lines | 0.5 |
| Bumpy Road | has 2 or more separate blocks of nested logic | 0.5 |
| Excess Function Arguments | takes more than 4 parameters | 0.25 |
| Complex Conditional | joins 3 or more conditions with `&&` / `\|\|` | 0.25 |
| Code Duplication | repeats another method of the file with only names and literals changed | 0.5 |
| Low Cohesion | *(a type)* splits into 3 or more groups of members that share no state and never call each other | 1.0 |
| Large File | *(the file)* is over 1,000 lines | 1.0 |

The default bar is **9**. An edit that creates a file below it, takes a file below it, or makes an
unhealthy file worse, is reported as `TERSE114`, naming each smell and the method that carries it. `analyze` alone adds developer congestion and change coupling from the last 200 commits as `HEURISTIC` `TERSE114` lines; an edit never runs git. Like every policy rule it
**warns by default**. Make it a hard stop, move the bar or switch it off in `.terse.json`:

```json
{ "policy": { "rules": { "codeHealth": { "limit": 9, "action": "reject" } } } }
```

**Fifteen policy rules, your standards.** A `policy` section in `.terse.json` checks every edit: cognitive
complexity (`TERSE100`), statements per method, methods per type, constructor dependencies, parameters,
method name length, meaningless suffixes, naming, `async void`, condition operands, chained references,
nesting depth (default 3), comments (`TERSE112`), XML doc comments (`TERSE113`) and code health
(`TERSE114`). Each is `reject`, `warn` or `off`, every rule that is on by default warns, and only what an edit
**introduces** counts. The file cascades like `.editorconfig`, and `terse install` writes your home copy.

**One call for the whole quality gate.** `gate` runs `analyze` → `format` → `cleanup` → `analyze` on
the files you changed and answers one line. `cleanup verify=true fix=ci` checks both CI format commands
in one call.

## 🛡️ Stays on the rails

- **The guard.** `terse install --guard` blocks `Read`, `Grep`, `cat` and `dotnet build` on C#, and
  hands the agent the exact TerseSharp call to make instead, with the `ToolSearch select:` that loads its schema.
- **Read-only mode.** `terse serve --read-only` makes every write refuse.
- **A smaller surface.** A solution with no XAML, Razor or `.resx` advertises **55 tools, ≤25,700 tokens**.
  The full list stays under a **30,900-token ceiling**, checked on every push.

## 🧩 Your stack

| | What you get |
|---|---|
| **Blazor / Razor** | `razor_validate` catches a parameter that compiles but throws at render. |
| **XAML** — MAUI, WPF, WinUI, Avalonia | `xaml_bindings validate=true` checks every `{Binding}` path, which WPF never does. |
| **ASP.NET Core** | `list_endpoints` lists every route; `find_registrations` finds DI registrations. |
| **Localization** | `.resx` / `.resw` families, missing translations, placeholder mismatches. |

## 🧰 All 86 tools

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigate** | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` |
| **Beyond grep** | `find_registrations` · `list_endpoints` |
| **Analyze & clean** | `analyze` · `format` · `cleanup` · `gate` · `clean` · `get_diagnostics` |
| **Edit** | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & packages** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Localization** | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Files** | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Git** | `changed_files` · `diff_symbols` · `diff_text` · `history` |
| **Build & test** | `build` · `run_tests` · `rerun_failed` · `list_tests` |

## Links

- Source, docs and issues: https://github.com/amusleh-spotware-com/terse-sharp
- Changelog: https://github.com/amusleh-spotware-com/terse-sharp/blob/main/CHANGELOG.md

MIT licensed. Built on Roslyn and the MCP C# SDK.
