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
  <img src="https://img.shields.io/badge/tools-86-26C281.svg" alt="86 tools"/>
  <img src="https://img.shields.io/badge/tokens-10--30×_fewer-26C281.svg" alt="10-30x fewer tokens"/>
</p>

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
terse doctor                    # verify SDK, MSBuild, workspace load, client registration
terse call get_file_outline --workspace App.slnx --json '{"path":"src/App/Order.cs"}'
```

No IDE, no licence, no Node, no Python, no API key, and no network call to answer a question.

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
| Rename across the solution | **~5,000 tok**, misses the interface | `rename_symbol` → **~150 tok**, correct | **30×** |
| Why is the build red? | **~8,000 tok** of MSBuild spew | `build` → **~600 tok** | **13×** |
| What did I just change? | `git diff` → the whole patch | `diff_symbols` → the changed **declarations** | **10×** |
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

## 🔒 Make your agent actually use it

An agent that has TerseSharp installed and reaches for `Read` and `Grep` out of habit saves nothing.
`terse install --guard` registers a Claude Code `PreToolUse` hook that **denies** the built-in and
names the tool to use instead:

```
TerseSharp guard: Read on 'src/App/OrderService.cs' is C#/.NET source.
Use the terse-sharp MCP instead - get_file_outline, get_symbol_source, xaml_outline or read_text.
```

It covers `.cs`, `.razor`, `.xaml`, `.axaml`, `.resx`, `.csproj`, `.sln` and friends, the shell text
tools (`grep`, `cat`, `sed`, `ls`, …) that name one of them, `dotnet build`/`test`/`format`/`clean`,
`dotnet watch build`/`test` and `msbuild`, and the working-tree half of git — `git status` and
`git diff`, in every flag and `-C` form, answered by `changed_files` and `diff_symbols` — but only
when the working directory sits under a `.sln`/`.slnx`/`.slnf`/`.csproj`, since the hook is installed
user-wide and those tools cannot answer in a repository TerseSharp does not serve. A denied command
also tells the agent not to run it in `Bash` again. Plain `.css`, `.js`,
`dotnet restore`/`pack`/`publish`/`run`, and git history and mutation (`log`, `blame`, `show`, `add`,
`commit`, `push`) are allowed — nothing here replaces those. Malformed hook input allows the call, so
a guard fault can never wedge a session, and you remove the guard by deleting the `terse guard` entry
from Claude Code's `settings.json`.

`terse install --skill` ships Claude Code the skill that teaches the swaps. On any other agent, a
short rule in `CLAUDE.md` / `AGENTS.md` / `.cursorrules` does the same job: *"C#/.NET goes through
terse-sharp; `Read`/`Grep`/`Edit` on `.cs`, `.xaml`, `.razor` or `.resx` is forbidden."*

## 🧰 The tools

**86 tools.** One record per line, workspace-relative paths, an explicit `truncated`/`total`, and a
success that costs nothing — every mutating tool answers in one line per changed file, with
`verbose=true` for the diff and `dryRun=true` to preview it. Any caveat prints in full.

| Group | Tools |
|---|---|
| **Workspace** | `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` · `list_projects` |
| **Navigation** — replaces `Read`/`Grep` | `search_symbols` · `get_symbol` · `get_file_outline` · `get_type_outline` · `get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of` |
| **What grep can't reach** | `find_registrations` (DI: open generics, factories, `Add*` extensions) · `list_endpoints` (ASP.NET Core `Map*`) |
| **Analyze & clean** — replaces `dotnet format` | `analyze` · `format` · `cleanup` · `clean` · `get_diagnostics` |
| **Edit** — replaces `Edit` on a `.cs` | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol` |
| **Refactor** | `extract_interface` · `move_type_to_file` · `move_type_to_namespace` · `change_signature` · `undo_last_change` |
| **Projects & solutions** | `solution_projects` · `solution_add_project` · `solution_remove_project` · `project_create` · `project_properties` · `project_set_property` · `project_add_reference` · `project_remove_reference` · `package_list` · `package_add` · `package_remove` |
| **XAML** — WPF · Avalonia · WinUI · MAUI | `xaml_outline` · `xaml_names` · `xaml_resources` · `xaml_resolve` · `xaml_styles` · `xaml_bindings` · `xaml_validate` · `xaml_find` · `xaml_codebehind` · `xaml_localization` · `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Localization** (`.resx`/`.resw`) | `resx_files` · `resx_get` · `resx_find` · `resx_usages` · `resx_set` · `resx_remove` · `resx_rename` · `resx_validate` |
| **Razor / Blazor** | `razor_outline` · `razor_component` · `razor_find` · `razor_bindings` · `razor_codebehind` · `razor_validate` · `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |
| **Files** — replaces `Glob`/`ls`/`cat` | `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex` |
| **Git** — replaces `git status`/`git diff` | `changed_files` · `diff_symbols` · `diff_text` |
| **Build & test** — replaces `dotnet build`/`test` | `build` · `run_tests` · `rerun_failed` · `list_tests` |

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
isn't done**, and **a tool that doesn't beat the built-in it replaces doesn't ship**. Changes are in
[CHANGELOG.md](CHANGELOG.md), releases in [RELEASING.md](RELEASING.md), security in
[SECURITY.md](SECURITY.md). MIT Licensed — see [LICENSE](LICENSE).

<p align="center">
  <sub>Built on <a href="https://github.com/dotnet/roslyn">Roslyn</a> and the
  <a href="https://github.com/modelcontextprotocol/csharp-sdk">MCP C# SDK</a>.</sub>
</p>
