<h1 align="center">TerseSharp</h1>

<p align="center">
  <b>Roslyn for your coding agent — 86 tools over MCP.</b><br/>
  Fewer tokens. Fewer turns of the loop. Only code you would merge.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/v/TerseSharp.svg?logo=nuget&label=NuGet" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/TerseSharp"><img src="https://img.shields.io/nuget/dt/TerseSharp.svg?logo=nuget&label=downloads" alt="Downloads"/></a>
  <a href="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml"><img src="https://github.com/amusleh-spotware-com/terse-sharp/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="MIT"/></a>
  <img src="https://img.shields.io/badge/.NET-11-512BD4.svg?logo=dotnet&logoColor=white" alt=".NET 11"/>
  <img src="https://img.shields.io/badge/tools-86-26C281.svg" alt="86 tools"/>
  <img src="https://img.shields.io/badge/tokens-10--30×_fewer-26C281.svg" alt="10-30x fewer tokens"/>
</p>

<p align="center">
  <a href="#-save-tokens">Save tokens</a> ·
  <a href="#-go-faster">Go faster</a> ·
  <a href="#-ship-better-code">Ship better code</a> ·
  <a href="#-your-stack">Your stack</a> ·
  <a href="#-install">Install</a> ·
  <a href="#-all-86-tools">All tools</a>
</p>

---

## What it is

Coding agents read C# the hard way. They open whole files, grep for names, and guess at line numbers.
TerseSharp gives them the compiler's view instead. It loads your solution into Roslyn once and answers
by meaning, not by text.

> **Without it:** grep `Submit` → 40 hits → read a 2,000-line file → read three callers → edit by line number.
> **With it:** `find_usages` → the 6 real references, ~200 tokens, one call.

| | | |
|:---:|:---:|:---:|
| 💸 **Save tokens** | ⚡ **Go faster** | 🧪 **Ship better code** |
| **4.56M tokens** not spent in one measured week, **2.9×** the whole bill | every round trip it removes saves **6.1 s** of model wait | every edit is **compile-gated** and checked against your **code health** bar |

---

## 💸 Save tokens

We replayed **508 real Claude Code sessions** on a 31,000-file solution. Each of the **6,045** TerseSharp
calls was re-priced against the built-in tool it replaced, run for real.

| | Tokens |
|---|---:|
| What TerseSharp actually cost | **2.41M** |
| What `Read`, `Grep` and the shell would have cost | **6.97M** |
| **Saved** | **4.56M (2.9× the bill)** |

The saving compounds. Each token in context was re-read 33 times from cache, so 4.56M tokens not sent
means about **150M tokens never re-read**.

| You ask | Built-in tools | TerseSharp | Saving |
|---|---|---|:---:|
| What is on this 2,000-line type? | read it all, ~6,000 tokens | `get_type_outline`, ~450 | **13×** |
| Who calls this method? | grep and follow-ups, ~4,000 | `find_usages`, ~200 | **20×** |
| Rename it everywhere | ~5,000, and it misses the interface | `rename_symbol`, ~150, correct | **30×** |
| Why is the build red? | ~8,000 tokens of MSBuild log | `build`, ~600 | **13×** |
| What did I just change? | the whole patch | `diff_symbols`, the changed declarations | **10×** |

**Success is quiet.** A successful edit answers in one line. The diff is there only when you ask for it
with `verbose=true`. Failures, rollbacks and warnings always print in full.

<details>
<summary>Per-tool breakdown and the pessimistic floor</summary>

| Call | Built-in cost | TerseSharp cost | |
|---|---:|---:|---:|
| `find_implementations` × 72 | 192k | 4k | **44.9×** |
| `find_usages` × 274 | 1.26M | 111k | **11.4×** |
| ranged `read_text` × 446 | 1.59M | 308k | **5.2×** |
| `get_file_outline` × 162 | 402k | 112k | **3.6×** |
| `search_symbols` × 520 | 860k | 272k | **3.2×** |
| `get_symbol_source` × 630 | 1.26M | 405k | **3.1×** |

Priced with every assumption pushed *against* TerseSharp, the saving is still **2.09M tokens, 1.9×**.
Two tools, `diff_symbols` and `list_tests`, cost more than the raw command in that week. That is logged
in the backlog, not hidden.
</details>

---

## ⚡ Go faster

An agent task is a loop: call a tool, wait, read, call again. Measured over 36,075 real tool calls,
**every turn of that loop costs 6.1 seconds** before the tool even runs. The fastest call is the one
the agent never needs to make.

- **One call instead of a chain.** `get_symbol usages=true` answers what a symbol is, who uses it and what implements it.
- **Batches everywhere.** Read 10 files, apply 25 edits, or search 10 terms in one call.
- **Edits that land together.** Change a signature and its callers in one compile-gated step.
- **Fast tests.** `run_tests` builds once and runs test projects in parallel, **38% faster**. A repeat
  with nothing changed answers instantly.
- **No polling.** A long run notifies the agent when it finishes.
- **Parallel-safe reads.** Read tools are marked read-only, so clients run them side by side.

---

## 🧪 Ship better code

TerseSharp holds the compilation, so it checks the work *before* it reports success.

### Every edit is compile-gated

An edit that adds a compile error is **rolled back**, in the changed project and in every project that
depends on it. `undo_last_change` reverses the last edit. Every result is tagged `EXACT` (proven by
Roslyn) or `HEURISTIC` (a text match), and the server never guesses: when it cannot prove an answer, it
says so.

### Code health, scored on every edit

Every edited file gets a **code health score from 1 to 10**, in the style of
[CodeScene's Code Health](https://codescene.io/docs/guides/technical/code-health.html). A file starts at
10, and each code smell takes points off:

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
unhealthy file worse, is reported as `TERSE114`, with each smell and the method that carries it:

```
WARNING policy  TERSE114  src/Orders.cs:1  <file>  code health 7.5 (Brain Method Orders.Classify, Complex Method Orders.Classify, Large Method Orders.Classify) is below 9
```

Like every policy rule, it **warns by default**. Turn it into a hard stop, move the bar, or switch it
off in `.terse.json`:

```json
{ "policy": { "rules": { "codeHealth": { "limit": 9, "action": "reject" } } } }
```

### Fifteen policy rules, your standards

A `policy` section in `.terse.json` checks every edit against your team's rules. It cascades like
`.editorconfig`: your home file sets your defaults, and a project overrides only what it changes.
`terse install` writes the home file for you.

| Rule | Default |
|---|---|
| `TERSE100` cognitive complexity | 150% of 10 |
| `TERSE101` statements per method | 10 |
| `TERSE102` methods per type | 10 |
| `TERSE103` constructor dependencies | 5 |
| `TERSE104` parameters | 5 |
| `TERSE105` method name length | 3 |
| `TERSE106` meaningless suffix (`Manager`, `Helper`…) | on |
| `TERSE107` naming per declaration kind | on |
| `TERSE108` `async void` | on |
| `TERSE109` operands in one condition | 3 |
| `TERSE110` chained references | off |
| `TERSE111` nesting depth | 3 |
| `TERSE112` comments | on, even with no `.terse.json` |
| `TERSE113` XML doc comments | on, even with no `.terse.json` |
| `TERSE114` code health | 9 |

Every rule can be `reject`, `warn` or `off`, and every rule that is on by default warns rather than rejects. Only what an edit
**introduces** counts, so an old file never blocks a clean change. `allowPolicy=true` pushes one edit
through and names each rule it skipped. Most limits copy ReSharper's own defaults.

### One call for the whole quality gate

`gate` runs `analyze` → `format` → `cleanup` → `analyze` again on the files you changed and answers
one line: `clean analyzed=42 fixed=3 remaining=0`. `analyze` reaches the info-level rules a build never
prints. `cleanup verify=true fix=ci` checks both CI format commands in one call. `get_diagnostics` finds
the consumer you broke elsewhere in the solution.

---

## 🛡️ Stays on the rails

- **The guard.** `terse install --guard` adds a hook that blocks `Read`, `Grep`, `cat` and
  `dotnet build` on C#, and hands the agent the exact TerseSharp call to make instead.
- **Folder rules.** Nested `CLAUDE.md` files reach the agent the first time it touches that folder.
- **Read-only mode.** `terse serve --read-only` makes every write refuse.
- **A smaller surface.** A solution with no XAML, Razor or `.resx` advertises **55 tools, ≤25,700 tokens**.
  The full list stays under a **30,900-token ceiling**, checked on every push. `--tools core` narrows
  it further, and `.terse.json` can hide any group or tool.

<details>
<summary>What exactly the guard blocks and allows</summary>

It blocks built-in reads and edits of `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln`
and friends. It blocks shell text tools (`grep`, `cat`, `sed`, `ls`…) inside a .NET tree. It blocks
`dotnet build`, `test`, `format`, `clean` and `list package`, a bare `sleep`, and `git status`, `diff`,
`log`, `ls-files`, tag listings and `ls-remote --tags`. Each denial names the replacing call with your own
arguments filled in. A compound command keeps running with only the replaced part removed, when that
is provably safe. `dotnet restore`/`pack`/`publish`/`run`, `git blame` and every git command that
changes history still run. `terse uninstall` removes it, and `TERSE_GUARD_LOG=<path>` logs each
decision.
</details>

---

## 🧩 Your stack

| | What you get |
|---|---|
| 🔷 **Blazor / Razor** | Components, parameters and `@bind` as structure. `razor_validate` catches a parameter that compiles but throws at render. |
| 🟣 **XAML** — MAUI, WPF, WinUI, Avalonia | Visual tree, resources and code-behind. `xaml_bindings validate=true` checks every `{Binding}` path, which WPF itself never does. |
| 🟢 **ASP.NET Core** | `list_endpoints` lists every `Map*` route and `@page`. `find_registrations` finds where a type is registered in DI. |
| 🟡 **Localization** | `.resx` / `.resw` families, missing translations and placeholder mismatches. |
| 🔁 **Rename** | `rename_symbol` carries into XAML and Razor where the link can be proven, and lists what it did not touch. |

Unity projects work too. VB and F# projects load, but the tools are C#-first.

---

## 🚀 Install

```bash
dotnet tool install -g TerseSharp
terse install --skill --guard
```

Restart your agent and ask it about your code. TerseSharp finds your `.sln`, `.slnx` or `.csproj` on its
own. It works with Claude Code, Cursor, VS Code, Windsurf and any MCP client over stdio. No IDE, no
licence, no API key. It runs on the .NET 11 runtime; a solution pinned to the .NET 10 SDK still loads.
`terse doctor` checks the setup.

<details>
<summary>Manual MCP setup, building from source, updates</summary>

```json
{ "mcpServers": { "terse-sharp": { "command": "terse", "args": ["serve", "--workspace", "C:/path/to/App.slnx"] } } }
```

Build from source with `dotnet pack src/TerseSharp.Server -c Release -o artifacts/nupkg`, then, from a
directory outside the repository (its `nuget.config` maps package sources, which `--add-source` refuses),
`dotnet tool install -g TerseSharp --add-source <repo>/artifacts/nupkg --prerelease`. TerseSharp checks for a
new release at most once a day; `TERSE_UPDATE=0` turns that off.
</details>

---

## 🧰 All 86 tools

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigate** — replaces `Read`/`Grep` | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` |
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

Each tool's parameters are in its own description, and the shipped agent skill teaches when to use which.

---

## ❓ FAQ

<details>
<summary>Will it change my code behind my back?</summary>

Only when your agent calls a writing tool. Every writer takes `dryRun=true` for a preview, C# edits roll
back on a compile error, and `--read-only` turns all writes off.
</details>

<details>
<summary>Does it handle huge solutions and several worktrees?</summary>

Yes. Up to four solutions stay loaded (`--max-workspaces`), idle ones release their memory after an
hour, and every answer names its worktree and branch. An ambiguous request lists the candidates instead
of guessing.
</details>

<details>
<summary>What is out of scope?</summary>

Databases, debugging and profiling. Git history is read-only.
</details>

---

## 🤝 Contributing

Two rules: **a tool without an E2E test is not done**, and **a tool that does not beat the built-in it
replaces does not ship**. See [CONTRIBUTING.md](CONTRIBUTING.md).

The easiest way to help is to run `/mine-sessions` in Claude Code. It measures where the tools cost you
calls, minutes or tokens, and adds the findings to [IMPROVEMENTS.md](IMPROVEMENTS.md) (closed ones live in
[IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md)). Open a PR with that file.

[CHANGELOG.md](CHANGELOG.md) · [RELEASING.md](RELEASING.md) · [SECURITY.md](SECURITY.md) · MIT licensed, see [LICENSE](LICENSE).

<p align="center">
  <sub>Built on <a href="https://github.com/dotnet/roslyn">Roslyn</a> and the
  <a href="https://github.com/modelcontextprotocol/csharp-sdk">MCP C# SDK</a>.</sub>
</p>
