---
name: terse-sharp
description: Use when reading, searching, navigating, editing, refactoring, building or testing C#/.NET, XAML, .resx localization or Razor/Blazor in a solution served by the TerseSharp MCP server. Teaches which TerseSharp tool replaces which built-in, and how to drive all 88 of them, so a .cs file is never read whole, a symbol is never found by text search, and a .xaml, .resx or .razor file is never edited by line number.
---

# TerseSharp

TerseSharp answers C# and XAML questions **semantically**, from a Roslyn workspace that is already
loaded. Reading a `.cs` file whole, or grepping for a type name, costs 10-30x more tokens and returns
matches that are not references.

## Route every question by its target

**Every C#/.NET question has a tool, and the table below names it.** Read the left column, take the
tool on the right, call it — that is the whole working rule, and it is the one thing to remember from
this document. It holds for `.cs`, `.razor`, `.cshtml`, `.csproj`, `.props`, `.targets`,
`.sln`/`.slnx`/`.slnf`, `.xaml`, `.axaml`, `.paml`, `.resx` and `.resw`, and for every question about
C# symbols, references, diagnostics, builds, tests or the working tree.

The rules that keep it that way — what the guard denies, what to do when a tool errors, and the
tripwires — are the hard gate directly **below** the table.

## The whole surface — one row per job

Read the **Job** column for what you want, the **Instead of** column for the built-in it retires, and
call what is in **Use**. Every tool the server advertises is in this table exactly once. What each
tool returns, its parameters and its defaults live in the tool's own advertised description — your
client already carries those, so this table is the job-to-tool map and nothing else.

| Job | Instead of | Use |
|---|---|---|
| **Workspace** | — | `workspace_status` |
| **Workspace** | `Bash: terse doctor` | `workspace_status(verbose: true)` |
| **Workspace** | what one tool's schema costs | `workspace_status(tools: true)` — one line per tool, tokens descending |
| **Workspace** | globbing for `*.sln` | `load_workspace(path, discover: true)` |
| **Workspace** | — | `load_workspace` |
| **Workspace** | — | `list_workspaces` |
| **Workspace** | — | `unload_workspace(path)` |
| **Workspace** | — | `list_projects(filter)` |
| **Workspace** | one `project_properties` call per project | `list_projects(properties: "IsTestProject,TargetFramework")` |
| **Workspace** | reading a `.csproj` to learn whether an edit is gated | `list_projects(path: "src/Foo.cs")` |
| **Navigate** | `Read` a `.cs` file | `get_file_outline(path)` |
| **Navigate** | `read_text` a `.cs` file no project compiles | `get_file_outline(path)` — parsed from its own text, tagged `HEURISTIC` |
| **Navigate** | `Read` **several** `.cs` files | `get_file_outline(paths: [...])` |
| **Navigate** | outlining a 45-member file to find five members | `get_file_outline(path, contains: "Total")` |
| **Navigate** | `read_text` a whole `.cs` file | it already answers the outline; `verbose: true` or a line range for the text |
| **Navigate** | `Read` a whole class's source | `get_symbol_source(symbolId, verbose: true)` on a **type** id — the default answers its member outline |
| **Navigate** | `Read` to see one method | `get_symbol_source(symbolId)` |
| **Navigate** | `Read` to see **several** methods | `get_symbol_source(symbolIds: [...])` |
| **Navigate** | one `get_type_outline` call per type | `get_type_outline(symbolIds: [...])` |
| **Navigate** | `Read` to learn a class's API | `get_type_outline(symbolId)` |
| **Navigate** | — | `get_symbol(symbolId)` |
| **Navigate** | one `get_symbol` call per symbol | `get_symbol(symbolIds: [...])` |
| **Navigate** | a name an outline printed that answers `SaturatedName` or `AmbiguousSymbol` | `get_symbol_source(symbolId, path: "src/Trading/OrderService.cs")` — `get_symbol` and `get_type_outline` take `path=` too |
| **Navigate** | `Grep` for a type or member name | `search_symbols(query)` — CamelHump works, `OSvc` finds `OrderService` |
| **Navigate** | asking the model whether a framework or NuGet member exists | `search_symbols` · `get_type_outline` · `get_symbol` · `get_symbol_source` — an exact type name no source declares falls back to the referenced assemblies |
| **Navigate** | a name the tests declare dozens of times | `search_symbols(query, scope: "src")` |
| **Navigate** | a common name that buries the one declaration you meant | `search_symbols(query, path: "src/Trading/OrderBook.cs")` |
| **Navigate** | `Grep` to find callers | `find_usages(symbolId)` |
| **Navigate** | `Grep` for implementers | `find_implementations(symbolId)` |
| **Navigate** | the outline → source → usages chain, when learning what a symbol IS | `explore_symbol(symbolId)` — ONE call |
| **Navigate** | judging a rename before doing it | `impact_of(symbolId)` |
| **Navigate** | searching for the tests a change can break | `impact_of(symbolId, tests: true)` |
| **What grep cannot reach** | "where is `IFoo` registered?" | `find_registrations(query)` |
| **What grep cannot reach** | "what endpoints exist?" | `list_endpoints()` |
| **Files** | "find the file called X" | `find_files(name: "orderrouter")` |
| **Files** | `ls` in a directory outside the workspace | `find_files(glob, root: "C:/logs")` |
| **Files** | one `find_files` call per glob | `find_files(globs: [...])` |
| **Files** | `Glob` / `ls` | `find_files(glob)` — a concrete path matching nothing answers `ABSENT`, `EXCLUDED` or `EXISTS` |
| **Files** | globbing a whole tree to learn its shape | `find_files(glob, depth: 2)` |
| **Files** | `ls -l` / `Get-Item` for a size or a timestamp | `find_files(glob, stamps: true)` |
| **Files** | `Bash: git ls-files` | `find_files(glob, tracked: true)` |
| **Files** | `Grep` in non-code files | `search_text(query)` / `search_regex(query)` |
| **Files** | `grep -n -e A -e B -e C` / one search per literal | `search_text(queries: ["I175", "I176", "I177"])` — records tagged `q1`..`qN` |
| **Files** | a search that keeps hitting a folder you do not want | `search_text(query, exclude: ".research/**")` |
| **Files** | `Grep -C3` / a search then a read | `search_text(query, context: 3)` |
| **Files** | a text hit, then "which declaration is that line in?" | `search_text(query, containers: true)` / `search_regex(query, containers: true)` — each hit becomes an id `get_symbol_source` takes |
| **Files** | `grep -w` | `search_text(query, word: true)` — `search_regex` answers it with `\b` |
| **Edits** | change a declaration's **attributes** — a tool `[Description]`, an `[Obsolete]` — without re-sending it | `edit_text(path, force: true, oldText: "<short unique fragment>")` — NOT compile-gated, so `analyze` the file after |
| **Files** | `grep -o` | `search_regex(query, matchesOnly: true)` — compose with `unique: true`; `search_text` refuses it |
| **Files** | `grep -c` / "is X in these files at all?" | `search_text(query, countOnly: true)` |
| **Files** | `grep -r` in a log folder outside the repo | `search_text(query, root: "C:/logs")` |
| **Files** | `sort \| uniq -c` over repeated log lines | `search_text(query, unique: true)` |
| **Files** | `Bash: git show <ref>:<path>` | `read_text(path, ref: "main")` · `get_file_outline(path, ref: "main")` |
| **Navigate** | the pre-change body of ONE member | `get_symbol_source(symbolId, path, ref: "main")` — `path=` required at a ref |
| **Files** | `Read` a non-`.cs` file | `read_text(path)` |
| **Files** | `Read` **several** files | `read_text(paths: [...])` |
| **Files** | `tail -n 200 log.txt` | `read_text(path, tail: 200)` |
| **Files** | four ranged reads for four anchors in ONE file | `read_text(path, ranges: ["42", "101-102"])` |
| **Edit text** | a read-modify-write on a tree another session is also editing | `read_text(path, stamp: true)` then `edit_text(path, …, ifUnchangedSince: "<that stamp>")` |
| **Files** | `wc -c file` for a size you want *while* reading | `read_text(path, bytes: true)` |
| **Files** | guessing what a budgeted document costs before its test runs | `read_text(path, tokens: true)` |
| **Files** | a file whose lines are enormous | `read_text(path, maxChars: 20000)` |
| **Files** | `Read` a whole `.md` to find a section | `read_text(path, headings: true)` then `read_text(path, section: "## Commands")` — a map over 40 sections folds to the levels that fit; `verbose: true` lists every level |
| **Files** | reading a whole `.md` whose content is one long table | `read_text(path, columns: "Finding,Tool")` |
| **Files** | a projection whose first column is PROSE, when you only want the row ids | `read_text(path, columns: "Finding", cellChars: 60)` |
| **Files** | `Bash: git checkout -- <path>` after a bad write | `write_text(path, ref: "HEAD")` |
| **Files** | a scratch `.cs` probe outside every workspace root | `write_text(path, content, force: true)` |
| **Files** | `Bash: rm file` · `Bash: rmdir` | `write_text(path, delete: true)` — an EMPTY directory is removed too |
| **Edit text** | `Edit` a `.md` section | `edit_text(path, section: "## Commands", newText: …)` — no `oldText` needed |
| **Edit text** | re-reading a file to see what an edit landed | `edit_text(path, oldText, newText, context: 2)` — POST-edit lines, not a diff |
| **Edit text** | reading a section out of one file and writing it into another | `edit_text(path, section: "## Open", toPath: "other.md")` |
| **Edit text** | anchoring on `### Added` to add a changelog entry | `edit_text(path, section: "### Added", occurrence: 1, place: "prepend", newText: …)` — `read_text` takes `occurrence=` too |
| **Edit text** | one `edit_text row=` call per row when closing a whole backlog, plus one more for its changelog line | `edit_text(path, rows: [{row, newText}, ...], toPath: "IMPROVEMENTS-ARCHIVE.md", edits: [{path: "CHANGELOG.md", oldText, newText}])` — `edits=` entries name OTHER files |
| **Edit text** | cutting one table row out of one markdown file and appending it to another | `edit_text(path, row: "I286", toPath: "IMPROVEMENTS-ARCHIVE.md", newText: "\| … \|")` — matched by its first cell |
| **Edit text** | three or more `edit_text` calls on the **same** file | `edit_text(path, edits: [{oldText, newText}, …])` |
| **Edit text** | one `edit_text` call per file across **several** files | `edit_text(edits: [{oldText, newText, path}, …])` — an entry carries its own `path` and `force` |
| **Edit text** | one `write_text` call per new file | `write_text(files: [{path, content}, …])` — every `.cs` among them shares ONE compile gate |
| **Edit text** | two entries of one `edits=` batch addressing occurrence 1 and 2 of the SAME anchor | `edit_text(path, edits: [...])` — ordinals resolve against the ORIGINAL text |
| **Edit text** | an anchor that deliberately repeats — a table of near-identical rows | `edit_text(path, oldText: "\| row \|", occurrence: 3)` |
| **Edit text** | `Edit`/`Write` a non-`.cs` file | `edit_text` · `write_text` |
| **Edit text** | re-reading a file because an anchor copied from `get_symbol_source` did not match | `edit_text` already handles it — dedented payloads still match |
| **Edit text** | `Write` a **new** `.cs` file | `write_text(path, content, force: true)` |
| **Edit text** | rewriting a whole `.cs` file | `write_text(path, content, force: true)` — compile-gated when a project compiles it |
| **Edit code** | `Edit` a `.cs` file | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` |
| **Edit code** | a new body that calls a private helper you have not written yet | `replace_symbol(symbolId, declaration, add: [...])` |
| **Edit code** | a signature change that breaks its callers | `replace_symbol(symbolIds: [...], declarations: [...])` — one compile-gated edit across files |
| **Edit code** | renaming a member and rewriting its body in one edit | `replace_symbol(symbolIds: [...], declarations: [...], rename: true)` |
| **Edit code** | adding an **enum member** | `add_member(typeSymbolId: "T:…MyEnum", declaration: "Retry")` |
| **Edit code** | adding a **sibling type** to an existing file | `add_member(path: "Foo.cs", declaration: "public sealed record Bar(int X);")` |
| **Edit code** | placing a member instead of letting it land last | `add_member(typeSymbolId, declaration, before: "Submit")` — also `after:`, and `position: "first"` / `"afterFields"` / `"last"`; the anchor takes any spelling of the parameter list |
| **Edit code** | an interface member and every implementation | `add_member(typeSymbolIds: [...], declarations: [...])` — ONE compile-gated edit |
| **Edit code** | find-and-replace a name | `rename_symbol(symbolId, newName)` — interfaces, overrides, doc crefs and XAML follow |
| **Edit code** | reverting an edit you regret | `undo_last_change` |
| **Refactor** | hand-writing an interface from a class | `extract_interface(symbolId)` |
| **Refactor** | cut-and-paste between files | `move_type_to_file` · `move_type_to_namespace` |
| **Refactor** | editing a signature and every call site by hand | `change_signature(symbolId, …)` |
| **Projects** | editing a `.csproj` by hand | `project_set_property` · `project_properties` · `project_add_reference` · `project_remove_reference` · `project_create` — untouched lines survive byte for byte |
| **Projects** | editing `PackageReference` by hand | `package_list` · `package_add` · `package_remove` |
| **Projects** | `Bash: dotnet list package --vulnerable` | `package_list(vulnerable: true)` · `package_list(outdated: true)` |
| **Projects** | "which properties does this project really have?" | `project_properties(project)` — MSBuild's evaluated set |
| **Projects** | editing a `.sln`/`.slnx` by hand | `solution_add_project` · `solution_remove_project` |
| **Projects** | "which projects does this solution contain?" for a solution that is **not** loaded | `solution_projects(path: …)` |
| **Git** | `Bash: git log` / `git show --stat` | `history` — `git blame` stays on the shell |
| **Git** | `Bash: git describe` | `history(describe: true)` |
| **Git** | `Bash: git tag --list` / `git tag -l "v*"` | `history(tags: true)` — creating or deleting a tag stays on the shell |
| **Git** | `Bash: git ls-remote --tags origin` | `history(tags: true, remote: true)` — every row tagged `local=yes\|no remote=yes\|no` |
| **Git** | `Bash: git diff --cached` for its hunk text or its declarations | `diff_symbols(staged: true)` · `diff_text(staged: true)` |
| **Git** | `Bash: git diff --cached --name-only` / `git status --untracked-files=no` | `changed_files(staged: true)` · `changed_files(untracked: false)` |
| **Git** | `Bash: git status` / `git diff --stat` / `--numstat` / `--name-only` / `--name-status` | `changed_files` — the counts family routes here, not to `diff_symbols`; a byte-identical repeat with nothing moved replays as `UNCHANGED` |
| **Git** | `Bash: git diff` to decide what to review | `diff_symbols` — hunks become symbol ids for `get_symbol_source` |
| **Git** | `Bash: git diff` for the hunk text itself | `diff_text(path: …)` — a clipped answer is `INCOMPLETE` and names the `skipLines=` that continues it |
| **Build and test** | `Bash: dotnet build` / `msbuild` | `build` — a byte-identical repeat with nothing written since answers `build UNCHANGED` |
| **Build and test** | `Bash: dotnet build -c Release` | `build(configuration: "Release")` |
| **Build and test** | `Bash: dotnet build -p:Name=Value` | `build(properties: ["Name=Value"])` |
| **Build and test** | `Bash: dotnet test` / `vstest` | `run_tests` |
| **Build and test** | one `run_tests` call per test project | `run_tests(projects: [...])` — concurrent, per-project timeout |
| **Build and test** | bounding parallelism **inside** one test assembly | `run_tests(runSettings: ["xUnit.MaxParallelThreads=1"])` |
| **Build and test** | re-running what broke | `rerun_failed` |
| **Build and test** | re-verifying SOME of what broke | `rerun_failed(tests: [...], exclude: [...])` |
| **Build and test** | `dotnet test --list-tests` | `list_tests(contains)` |
| **Build and test** | `Bash: dotnet clean` | `clean` |
| **Analyse** | one `analyze` call per touched file | `analyze(paths: [...])` |
| **Analyse** | `dotnet format whitespace` / an IDE inspection | `analyze` — compiler + analyzers + dead code, down to `info` |
| **Analyse** | running `analyze` → `format` → `cleanup` → `analyze` at the end of a task | `gate` — one verdict line |
| **Analyse** | `dotnet format style` / `dotnet format analyzers` | `cleanup fix=style\|analyzers\|all` |
| **Analyse** | `dotnet format --verify-no-changes` | `format verify=true` · `cleanup verify=true` |
| **Analyse** | one `cleanup` call per touched file | `cleanup(paths: [...])` |
| **Analyse** | one `format` call per touched file | `format(paths: [...])` |
| **Analyse** | formatting only what you touched | `format changed=true` · `cleanup changed=true` |
| **Analyse** | reading build output for a consumer you broke | `get_diagnostics` |
| **XAML** | `Read` a `.xaml` file | `xaml_outline(path)` |
| **XAML** | `Grep` a `.xaml` file | `xaml_find(query)` · `xaml_names()` · `xaml_resources()` |
| **XAML** | hunting a resource through `App.xaml` | `xaml_resolve(key)` |
| **XAML** | "why does this control look like that" | `xaml_styles(typeName)` |
| **XAML** | eyeballing a `{Binding}` | `xaml_bindings(path, validate: true)` |
| **XAML** | `Read` a `.xaml.cs` to see what the markup wires | `xaml_codebehind(path)` |
| **XAML** | "is this element translated" | `xaml_localization()` |
| **XAML** | guessing whether the markup is sound | `xaml_validate()` |
| **XAML** | `Edit` a `.xaml` file | `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` |
| **Localization** | `Read` a `.resx`/`.resw` | `resx_get(path, cultures)` |
| **Localization** | `Glob` for resource files | `resx_files()` |
| **Localization** | `Grep` a resource key | `resx_find(query)` |
| **Localization** | "is this key still used" | `resx_usages(key)` |
| **Localization** | "which strings are untranslated" | `resx_validate()` |
| **Localization** | one `resx_set` call per key | `resx_set(entries: "Key=Value\nOther=Second")` — `files: [{path, entries}, …]` writes up to 10 culture files, and needs no top-level `path` |
| **Localization** | `Edit` a `.resx`/`.resw` | `resx_set` · `resx_remove` · `resx_rename` |
| **Razor** | `Read` a `.razor` or `.cshtml` file | `razor_outline(path)` |
| **Razor** | "how do I use this component" | `razor_component(name)` |
| **Razor** | `Grep` a tag, directive or route in markup | `razor_find(query, kind)` |
| **Razor** | "is this `@bind` real" | `razor_bindings(path, validate: true)` |
| **Razor** | `Read` a `.razor.cs` | `razor_codebehind(path)` |
| **Razor** | "what breaks at render" | `razor_validate()` |
| **Razor** | `Edit` a `.razor` file | `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` |

## 🚫 HARD GATE — take the tool from the table; the built-ins are the last resort

**Take the tool the table above names, on every call.** That is the whole rule, and it holds for
`.cs`, `.razor`, `.cshtml`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`/`.slnf`, `.xaml`,
`.axaml`, `.paml`, `.resx` and `.resw`, and for every question about C# symbols, references,
diagnostics, builds, tests or the working tree.

**So a `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call on one of those is
forbidden.** Not "discouraged" — forbidden. There is a TerseSharp tool for it in the table above.

**And issue independent calls in ONE message.** Several `tool_use` blocks in one message run
concurrently; one call per message pays a **6 136 ms (p50)** model gap before its tool even starts.
Measured over a fortnight and 647 transcripts, grouping `tool_use` blocks by the API `message.id`
that carried them: **1.165 calls per assistant message, and only 14.3% of messages carry two or
more**. Measured A/B on the same eight
files: eight `get_file_outline` calls one-per-message cost **151.4 s wall**, of which **148.5 s was
model gap**; the identical work as one `paths=[...]` call cost **10.2 s** - **14.8x faster**, and
**98% of the saving was gap, not tool time**. What it is worth has been measured elsewhere too:
three tool calls per turn cut wall clock **40.6%** while accuracy *rose* (arXiv:2602.07359), and one
batched call rather than 38 separate ones measured **57% faster and 41% fewer tokens**
(arXiv:2511.19477). Two concrete shapes are most of it: a `search_text` beside a `read_text` of a **different** file, and a
`find_files` or `search_symbols` beside a read of a file you already know you need. Send those in one
message - but never guess an argument to make a call parallel. Inside
one tool the same lever is `paths=`, `symbolIds=`, `queries=`, `edits=`, `files=`, `projects=`.

**The shell does not launder it.** `grep`, `rg`, `find`, `fd`, `cat`, `head`, `tail`, `sed`, `awk`,
`ls`, `dir`, `tree`, `wc`, `nl`, `findstr`,
`type`, `dotnet build`, `dotnet test`, `dotnet watch build`, `dotnet watch test`, `dotnet msbuild` and
`msbuild` run through `Bash` are built-ins
too and are covered by the same gate — including later in a compound command
(`cd src && dotnet test`).

**In a .NET tree the shell text tools are denied even when the command names no `.cs` file** - `grep -rn TODO docs/`, `ls src`, `cat appsettings.json` all have a replacement there. A text command naming no .NET source whose every path operand is OUTSIDE the tree - `tail -5 /tmp/scan.out` - is allowed. A denied command that WRITES routes to `write_text`, not to an outline. A text tool reading STDIN is untouched, so `git branch -a | head -40` still runs. A `2>&1` no longer forces a whole-command refusal, a `$( )` no longer shadows the real command, and a denial names the replacing call **with your own arguments translated** - `git log --oneline -1` answers `history maxResults=1`.

**This is enforced, not advisory, when `terse install --guard` is in place.** The `PreToolUse` hook
denies the call, names the tool that replaces it, and tells you not to run it in `Bash` again. A
denial is not a reason to try a different spelling of the same shell command — call the tool.
**The denial also hands you the answer**: a system reminder beside the tool result reads
`Call this instead: <the complete call, with the arguments already filled in from what you tried>`.
Run that call verbatim; it is chosen from the file kind, so a `.xaml` read routes to `xaml_outline`
and a `.resx` read to `resx_get`, not to `get_file_outline`.

`dotnet format` and `dotnet clean` are covered too, with the **exact** replacement per sub-command:
`dotnet format analyzers` -> `cleanup fix=analyzers` (add `verify=true` for `--verify-no-changes`),
`dotnet format style` -> `cleanup fix=style`, a bare `dotnet format` -> `format` plus `cleanup fix=all`,
and `dotnet clean` -> `clean`. Those two verify modes check exactly the rule sets the two CI commands
check, so never shell out for them - and **`cleanup verify=true fix=ci` is both in ONE call**. `dotnet list package` routes to `package_list`
(`vulnerable=true`, `outdated=true`, same restored graph). `dotnet restore`, `pack`,
`publish`, `run` and `tool` are **not** covered: nothing here replaces them.

**A bare `sleep` is denied too, `powershell -Command "Start-Sleep ..."` included, and nothing replaces it.** A segment whose COMMAND WORD is `sleep`,
outside a `while`/`until`/`for` loop, is refused. `docker run … sleep 3600` and `python sleep.py` are
untouched. Background work
re-invokes you when it finishes, so when you need its result and have nothing else to do, **end the
turn** — stopping is free, sleeping is billed. The one allowed shape is the pause inside a loop that
also detects the process dying: `while :; do kill -0 "$PID" || break; sleep 1; done`.

**POLLING BY TOOL is the same breach, and the guard now says so at the call.** Reading `TaskOutput`/`TaskList`
for a result the harness delivers by itself cost **14.08 h/week**. A check *after* a notification is
fine; waiting on one is not.

**One replaced command no longer kills a batch.** The guard strips those commands, rewrites the
rest and lets them RUN, naming what it removed — call the tools for those, do NOT re-run the batch. It
rewrites only sound shapes: uniform `&&`/`;`/newline separators, a whole pipeline at a time, and a plain redirect (`>`, `>>`, `2>`, `<`) rides with the pipeline it follows - stripped with a replaced one, run with a kept one; a heredoc (`<<`), a target-less redirect and `>&-` still fence. `||`, a background `&`, a subshell, a substitution, a comment, a backslash escape, a mixed `;`/`&&` run or a shell keyword is **denied
whole** — `NO part of the command ran`, and `Call this instead:` names each denied segment's tool call
**and** every segment nothing replaces — chained with `&&` when re-issuing them together is sound,
listed one by one when it is not, because printing a segment executes nothing. That class cost **18.1 h — 51.5% of all `Bash` wall time** in
one week, at a 13.2% error rate. A whole-command
refusal also names the construct that forced it and its offset, so you re-issue that ONE segment rather
than re-deriving the command.

**A `maxResults=` you pass is taken as your bound.** `search_text`, `search_regex`, `find_files`,
`changed_files` and `history` still say the cap bit - `2/38 matches truncated` - but never advise
raising a number you chose; the steer returns as soon as you drop the argument.

**The working tree is covered as well.** `git status`, `git status --porcelain`, `git diff`,
`git diff <ref>` and the whole `git diff --cached` family are served by `changed_files`
(`staged=true` for the index, `untracked=false` for `--untracked-files=no`), `diff_symbols` and
`diff_text` — **all three take `staged=true`**, and all three take
`baseRef=`, so `main`, `HEAD~3` and a range work, and the paths come back workspace-relative and
re-usable as arguments. A bare `git ls-files` is served by `find_files tracked=true`. A diff of a path
that is not `.cs` routes to `diff_text`, which is what can answer it. Running them in
`Bash` is the same breach as `grep` — but only for the tree TerseSharp serves: the guard reads the
directory the command actually addresses (`-C` target, then a directory operand, then the working
directory), so `git -C ../some-other-repo status` is allowed, because no tool here answers it. Git **history** is served too now: `git log` and `git show --stat` are `history`, and
`git show <ref>:<path>` is `read_text ref=` / `get_file_outline ref=`, and a `git tag` **listing** —
bare, or any flag-only form such as `--list`, `-l` or `--sort=` — is `history tags=true`. A tag listing of
**origin** — `git ls-remote --tags` — is `history tags=true remote=true`, which merges both lists and
tags every row `local=yes|no remote=yes|no`, putting the rows only the remote has FIRST so the cap
cannot drop the ones a release check is looking for; `--heads` (even beside `--tags`), another remote
and a bare `git ls-remote` are left alone. Still on the shell: `git blame`
— measured at **one** call in 683 sessions — anything that mutates the index or history (`git add`,
`git commit`, `git push`, and every `git tag` that creates, annotates or deletes one), and a
**scripted extraction** such as
`$(git log -1 --format=%H)`, because `--format=`, `--pretty=`, `-s` and `--name-only` ask for a shape
`history` does not produce.

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
- You are about to run `git status` or `git diff` in `Bash` — `changed_files` and `diff_symbols`
  answer both, for a fraction of the tokens.
- You are about to open a `*_razor.g.cs` under `obj/` — that file is generated; edit the `.razor`.

## Behaviour the table cannot carry

**Four solutions stay loaded at once**, the least recently used being unloaded beyond that; a
workspace that vanished from `list_workspaces` was evicted, not lost, and the next call naming it
reloads it. The user can change the limit with `terse serve --max-workspaces N` or
`TERSE_MAX_WORKSPACES` — worth telling them when a big solution is making the server heavy, because a
loaded workspace costs roughly 3 GB on a 148-project tree.
**The advertised surface is derived from what the solution holds** — no `.xaml`/`.axaml` hides the 13
`xaml_*` tools, no `.razor`/`.cshtml` the 10 `razor_*` — and so does one whose Razor generator did not
run — no `.resx`/`.resw` the 8 `resx_*`: 57 tools
instead of 88 on a plain C# solution, because the full catalogue costs tokens on every request and
measurably lowers selection accuracy. Loading a second solution that does hold them re-advertises
those families through `notifications/tools/list_changed`; `--tools all` (or `TERSE_TOOLS=all`)
advertises everything regardless and `--tools core` narrows to about twenty. A hidden tool still
answers when called by name — but an agent can only call what its client lists, so treat a narrowed
surface as narrowing what you can reach, not merely what you can see. `workspace_status` prints
`tools=core - N advertised` under a profile and `tools=<families> hidden` when the workspace narrowed
it. `verbose=true` adds `surface=<n> tools <t> tokens` - what the WHOLE surface costs.
**A freshly loaded workspace has no compilations yet**, so `load_workspace` ends with
`compilations=cold - the first semantic call realizes them and pays for it once`, and the first
semantic call that realizes them appends `compilations=realized in Nms (once per load, not per call)`.
Read that as a one-off, not as the per-call cost of the tool that happened to pay it — measured at
about 7 s on a 300-document solution — and do not reload or restart over it.
**A workspace nobody has used for 15 minutes gives its compilations back** (`--idle-minutes`,
`TERSE_IDLE_MINUTES`, `0` to disable), and so does any idle workspace once the heap passes 2 GB.
`workspace_status` then says `idle=<n>m compilations=dropped`; the next semantic call re-realizes
what it needs, which costs a second or two once — that is the trade, and it is why the line is
printed rather than left silent. On a **multi-targeted** solution pass
`load_workspace(targetFramework: "net10.0")`: without it MSBuild picks, and an `#if NET6_0` branch can
be invisible to `find_usages` with every gate green. Whatever was chosen is printed as
`targetFramework=` by both `load_workspace` and `workspace_status`.
Unloading a workspace — by `unload_workspace` or by eviction — ends with a compacting collection, so
the memory really does come back; that costs about a second, which is why it happens only when a
workspace is genuinely dropped and why the unload-and-retry that `build`/`run_tests` perform on a
locked output skips it.

**The analyzers a solution builds from source
no longer block your own build**: every analyzer and source-generator assembly is loaded from a
shadow copy under a user-private `terse-analyzers/` cache, so the file in the project's `bin/` is never mapped and an
external `dotnet build` succeeds while the workspace is loaded. The response still carries a `WARNING`
listing any assembly that *did* end up mapped — that is a regression detector, and if you ever see it,
restarting the server is the only way to release those files. One consequence to know: an analyzer or
generator **rebuilt while the server is running is still served from the copy loaded first**, because
the .NET default load context cannot replace an assembly identity in place — restart the server after
rebuilding an analyzer whose behaviour you need to see.
`load_workspace`'s last line reports freshness —
`watch=active gen=c12/p1/x3/r0/rz2/f4 pending=0 lastSyncMs=8 gaps=0`: the
watcher state, the per-kind generation counters (Code / Project / Xaml / Resx / Razor / Files), how many paths are
waiting to be examined, and how many watcher events were lost. The line after it reports the workspace index —
`index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-) razor(hit=3 miss=1 files=10)
paths(hit=7 miss=1 files=31324) documents=9/128 parses=9`.

**`find_files`, `search_text` and `search_regex` answer from that `paths` index, not from a fresh
walk.** The tree is enumerated once and re-enumerated only when the watcher sees a file appear,
disappear or get renamed, so a repeat `find_files` on a 31 000-file solution costs a glob match over
an in-memory list rather than a full directory walk. Ask them as often as you like; a file you or the
user just created, deleted or renamed is in the answer without a reload — the writers say so directly,
so it does not wait on a watcher event. When the watcher is off or degraded the index is not trusted
and the tree is walked again — correct, just slower.

**`workspace_status` says when a document it holds no longer matches the file on disk.** After this
server has applied an edit it compares the files it wrote against their bytes and answers
`WARNING workspace=diverged - N document(s) differ from disk: <paths>`; that is the one case every
other read cannot detect, because they all answer from the same in-memory snapshot. Re-apply the edit
or `load_workspace reload=true`. `verbose=true` prints the clean verdict too - `disk=in-sync`, or
`disk=not probed` when this server has written nothing since the load.

**A `WARNING guard=absent` or `skill=absent` line on `workspace_status` or `load_workspace` is for the
user.** Without the `PreToolUse` guard nothing stops an agent answering with `Read`, `Grep`, `cat` or
`dotnet build` - measured at 884 such `Bash` calls in one week. Tell the user to run
`terse install --guard`; do not run it yourself, because it writes their settings file.

**`failures=` counts projects that did NOT load; `warnings=` counts everything else** — NuGet
advisories (NU1903), target framework notes (NU1701). Roslyn hands over every MSBuild design-time
message, warning and error alike, as one `Failure`-kind diagnostic, so the split is made on the one
observable fact: whether the project the message names is in the loaded solution — which means a
design-time **error** on a project that still loaded lands in `warnings=` too. **Neither is listed by
default**: the warnings are one `N MSBuild message(s) from project(s) that loaded, not load failures`
note, the failures one `FAILED <project>  messages=N` line per project under a `N load failure(s) in M
project(s)` header. `verbose=true` prints every message of both — read them before trusting an odd
project. A big solution routinely reports `failures=0 warnings=20`, is fully usable, and is never a
reason to fall back to the built-ins.

**Success is quiet.** `build`, `run_tests`, `rerun_failed`, `format`, `cleanup` and `clean` answer a
result that has nothing to say in one line, or one line per changed file. `verbose=true` restores the
full report on any of them. The short form is **only** emitted when there is nothing else to report —
a failure, a rolled-back edit, a timeout, a zero-result run and a locked file all keep the full
output — so do not pass `verbose=true` defensively.

**A warning is never something to report.** A build that **succeeds** answers in one line however
many warnings it produced — `build ok  errors=0 warnings=37  elapsedMs=4235` — and a build that
**fails** lists its error-severity diagnostics only, followed by `warnings=37 hidden`. The count is
there so you know `verbose=true` has something to show; ask for it when you intend to act on the
warnings, and use `analyze` when the warnings *are* the question. A failed build with no
error-severity line falls back to listing what it does have, so a failure never answers with nothing.

**`warnings=N` counts what that build emitted, not what the solution contains.** MSBuild re-reports
nothing for a project it did not recompile, so a second `build` on an unchanged tree answers
`warnings=0` however many the first one found. Read it as "warnings from the work this build did";
when you need the solution-wide truth, ask `analyze`.

The same holds where `run_tests`, `rerun_failed` and `list_tests` report a build that failed under
them: `no test results were produced` is followed by the **errors**, not by fifteen lines of raw
MSBuild output. Those three have no "list the warnings when there is no error" fallback — a failure
carrying only warnings answers with the bounded
`FAILED with no error-severity diagnostic; last output lines:` tail, which is where a crashed test
host says why. That tail is appended whenever no **error** was found, in either mode, so
`verbose=true` is always a superset: it adds the warnings, it never replaces the failure reason. A
`list_tests` that succeeded is untouched, whether or not it matched a name.

**The verification ladder — climb it, never start at the top.** `run_tests` is **37% of all tool wall
time**, and **6.1% of its identical repeats were provably redundant - nothing was written between
them** (`build`: 10.0%). Per
edit, climb only as high as the edit reaches:

| Rung | Call | Measured mean | When |
|---|---|---|---|
| 1 | `analyze` on the touched file, down to `info` | **7.0 s** | after EVERY edit |
| 2 | `build` scoped to the project | **13.0 s** | when the edit crosses a signature or a consumer |
| 3 | `run_tests` scoped to the affected project | **85 s** | once the slice compiles |
| 4 | `run_tests` over the whole solution | 85 s+, p99 **16 min** | ONCE, at the end of the task |
| — | `rerun_failed` | 20 s | after a red run — never re-run a whole suite to watch the same test fail twice |

A tier is never dropped; only how often it is re-run. A byte-identical `build`, `run_tests`, `rerun_failed`, `list_tests` or `clean` call inside one session answers with `repeat #N of this exact call Ns ago - previous verdict: ...; nothing was written in between` - read that as the answer you already have. Banned: a full-suite run between two edits of one
slice · re-issuing `build` or `run_tests` with identical arguments when nothing was written in between - neither lets you any more: a repeat of a call that already answered GREEN (`run_tests PASSED`, `build ok`), with no edit and no watcher event on any loaded workspace since, answers `run_tests UNCHANGED` / `build UNCHANGED` naming the previous verdict and its age instead of running, and `force=true` opts out. `rerun_failed` is never memoized and always runs, because the failure list it replays is not named by any of its arguments · a
run to "confirm" one that already passed · reading a test result before the build result.

**Analyse — at the end of a task, call `gate` and stop there.** An UNSCOPED `analyze`, `format` or
`cleanup` now ends with `next: gate` (or `next: gate dryRun=true` when you were only verifying) —
take it; the whole-solution sweep you just started is the composite's one call.
**Analyse — the detail:** It runs `analyze` at `info`,
`format`, `cleanup fix=all` and `analyze` again, in the order this project mandates, over the files
changed since the workspace loaded, and answers **one verdict line**. That is the whole end-of-task
sweep in one call instead of four, and it is the first thing to reach for — a measured week of this
server's own sessions made 356 `analyze` calls and **zero** `gate` calls. Reach for the individual
tools only when you need one of them on its own, or when `gate` reports `FAILED` and you are
fixing what it named.

**`analyze` and `get_diagnostics` fold findings sharing an id, a severity and a message onto one line
carrying every position**, because the positions are the fix list and the message is not. **Each
position names the declaration containing it** - `OrderService.cs:15:16 OrderService.Unused` - so the
fix list is ids for `get_symbol_source`, not coordinates. A finding with no source tree keeps the bare
position; `build` carries no tag, having released the workspace before it shells out. **An id you
pass to `ids=` that no referenced analyzer declares comes back as `NOT_ENABLED <id>`**, so a sweep
answering `0 diagnostics` can no longer mean "the rule never ran".

`analyze`'s `changed=true` set is carried across the unload-and-reload `build`/`run_tests` perform on
a locked output, so an analyze after a build no longer answers `no document under that scope was
modified`; the end-of-task gate over a task's touched files is **one** call, not one per file.
`sinceLast=true` reports only what appeared since the previous run of the same scope, plus what was
fixed. `cleanup` never rewrites generated code, and `clean` is not covered by `undo_last_change`.
`gate` answers **one verdict line** - `clean` or `FAILED` - and, when it is not clean, each step's
own line plus the diagnostics that are still unfixed; never a diff. **`analyzed=N` on that line counts
the documents the gate had in scope, not the diagnostics it found**, so `analyzed=0` cannot happen and a
clean verdict is never a gate that ran over nothing; a scope matching no document answers an `ERROR`
naming it instead of a verdict. It condenses to that single line
only when every step was genuinely quiet, so a `VERIFY_FAILED`, an `UNFIXED`, a rolled-back step or a
file the run rewrote is always shown. Under `dryRun=true` a tree that **would** change answers
`FAILED`, which is what a pre-push check is for. `dryRun=true` makes both write steps verify instead of write, so
nothing is modified; `verbose=true` adds each step's own report. It never replaces reading `build`
before `run_tests`: those two stay separate on purpose, because a test result read before its build is
the previous binary's.

**`format` also collapses a run of blank lines between members down to one**, so the double blanks a
multi-member `add_member` leaves behind never need a shell rewrite. It edits trivia, never token text,
so a raw string literal is safe. `cleanup fix=all` and `fix=usings` fold too; `fix=style` and
`fix=analyzers` do not reformat at all.

**`cleanup` removes unused `using` directives; it never reorders the ones already there** - sorting the
block System-first is a rewrite neither CI command makes. A directive `usings=` adds still lands
sorted. **And a run that REFORMATTED text where no `.editorconfig` at or above those files sets
`indent_style` says so**: whitespace then follows Roslyn's own defaults, which may not be the repo's
convention, and a ReSharper `*.sln.DotSettings` is **not** read. `fix=style`, `fix=analyzers` and
`fix=ci` never reformat, so they never say it, and a run that changed nothing says nothing.

**`fix=all` withholds a fix that would rewrite the shape of an externally visible member** - today
`CA1822`, whose instance-to-static flip a Razor template, a binding, a serializer or reflection
breaks on while every compiler gate stays green. Withheld occurrences answer `UNFIXED CA1822 xN`
with the reason; a `private` or `internal` member - or one inside such a type - is still flipped, and
`gate` inherits this in BOTH its modes, dry run included, so its preview matches what it writes.
**`cleanup verify=true` never withholds**, so a verify can never hide a red CI leg, and
`fix=analyzers` and `fix=ci` never withhold at all.

**`format verify` and `cleanup verify` are not the same gate.** `format` compares against the Roslyn
whitespace formatter, which `dotnet format style` and `dotnet format analyzers` do not run — a
`VERIFY_FAILED` there can still be a green CI leg. `cleanup verify=true fix=style` and
`fix=analyzers` are exactly those two CI commands — they apply code fixes only and never reformat —
while `fix=all` and the default `fix=usings` do reformat, so those two are supersets that may name
files CI accepts. **You no longer have to work that out**: every file a verify names carries the step
that would change it — `whitespace`, `fixers` or `fixers+whitespace` — and a mode that also reformats
names the byte-equivalent CI pair. Every file `whitespace` is a green CI leg; any `fixers` is a red one.

**`cleanup verify=true fix=ci` is both CI commands in ONE call** - the same union, no whitespace
formatter, each named file tagged `style`, `analyzers` or `style+analyzers`.

**`find_usages` and `resx_usages` report one record per SOURCE POSITION.** A multi-targeted project
gives Roslyn one symbol per target framework, each finding the same call site, so one usage used to
print two to eight times. They deduplicate by position now, so the count IS the blast radius.

**Refusals the tool descriptions do not spell out:** `cellChars=` without `columns=`; `bytes=` and
`stamp=` answer `UNRESOLVED` under `ref=`; `edit_text` refuses `toPath=` naming the same file twice
and `row=`/`rows=` beside `section=`; `replace_symbol` refuses two `symbolIds=` entries where one
declaration contains the other, and takes enum member declarations on an enum member id; `fix=`
replays every held entry it does not name and refuses an index the batch does not carry;
`ifUnchangedSince=` carries ONE file's stamp, so a call writing more than one file is refused, and a
value that is not a round-trip UTC timestamp is refused by name. A `retryWith` token is bound to the
workspace AND the tool that issued it, so a `replace_symbol` token replayed through `add_member` is
not recognised. With `toPath=`, `place=prepend` puts the moved section at the top of the target.

**A write is visible to THIS workspace's next call with no reload** - but another loaded workspace
over the same root, and another terse process, pick it up through their own watcher, so their next
call may still answer from the pre-write snapshot. The compile gate reads the workspace as it is NOW,
so two new interdependent `.cs` files land in either order.

**A batched symbol read counts what it ANSWERED, not what it was asked for.** `get_symbol`,
`get_symbol_source` and `get_type_outline` answer `1/2 symbols` when one id did not resolve - a
partial batch, not a truncation - and the `NOT_RESOLVED` line names which one.

**A question with a defensible default is answered by taking the default, not by asking.** Stopping
the loop to ask cost **12.91 h over 90 calls** in one fortnight - p90 **1 011 s**, max **4.23 h on one
question** - more than every `analyze`, `cleanup`, `format`, `get_diagnostics`, `gate` and
`list_tests` call combined. Take the default and record it as an ASSUMPTION.

**A missing path is answered, not just refused.** `get_file_outline` and `read_text` on a path named
after a type the workspace declares elsewhere name the file that declares it, and `add_member path=`
on a `.cs` file nobody has written yet names `write_text path=… force=true` — neither sends you to
`find_files`, which cannot find a type that does not name its file.

**`replace_symbol` replaces the whole declaration, attributes included, and says when yours dropped
them** — `WARNING attributes dropped: McpServerTool, Description`. The edit still applies, because
dropping an attribute is sometimes the intent, but an un-advertised tool is exactly what a clean
build, `analyze` and `get_diagnostics` cannot show you. Copy the attributes in, or use
`replace_symbol_body`.

**`add_member` formats only what it inserted** - no collateral hunks, and an anchored insert leaves the
close brace alone.

**`add_member` refuses a duplicate member from syntax, before anything is compiled.** A declaration
whose name and parameter list the type already declares answers `ERROR NameTaken` naming that member
and its line - it used to cost a full compile round trip and the whole rejected declaration. An
overload whose parameter list differs still lands.

**`add_member` places the member; it no longer only appends it.** `before="Submit"` lands the new
members above that member and `after="Submit"` below it, by short name or documentation id, resolved
**inside the target type**; a name it does not declare is refused naming the members it does.
`position="first"`, `"afterFields"` (after the last field) or `"last"` picks a coarse slot instead;
two anchors together, an anchor beside `position=`, an anchor naming two overloads - the refusal
names each overload's signature, and passing one of those verbatim places it - and any placement
beside `path=` are refused rather than one being dropped - an indexer, operator, destructor or
explicit interface implementation cannot be anchored on. The placement is **not** held by a
`retryWith` token, as `add=` and `rename=` are not.
**The default `last` is region-aware**: a trailing `#endregion` lives in the close brace's leading
trivia, so an append lands above the region the type closes rather than inside it - where a new
constant used to be filed silently under "Nested types". `replace_symbol add=` shares that default
but takes no placement arguments.

**A mutation names the warnings it introduced** as `WARNING introduced  <diagnostic>`, up to five and
saying `5 of 12 shown` when there are more, so learning *which* three no longer costs an `analyze`. **A NESTED TYPE's container is its declaring type**, so `symbolIds=["Outer.Nested", "Outer.Sibling"]
with `add=` lands the members in `Outer` instead of being refused.
**`replace_symbol add=` takes `addTo=`** when the targets do not share one containing
type; it must name one of the targets' own containers, and a bare leaf name that matches two of them
is refused naming both qualified names rather than resolved to the first. **`addTo=` is comma-separated**,
paired with `add=`: `add=[a, b] addTo="Alpha,Beta"` puts `a` in `Alpha` and `b` in `Beta`. One name
takes every entry; any other count is refused.

**`add_member` and `replace_symbol` accept several declarations in one call**, applied as a single
compile-gated edit — so a set of members that reference each other needs no dependency ordering, and
`replace_symbol` can split a member into overloads. `add_member` also takes `declarations=[...]`. On a member that is already expression-bodied,
`replace_symbol_body` accepts a bare expression as well as `=> expr` and a statement block.

**`usings=` lands the import in the same edit, and is the first thing all three descriptions name.**
`replace_symbol_body`, `replace_symbol` and
`add_member` take `usings: ["System.Collections.Immutable"]`, added to the file's using block —
sorted System-first, one already present ignored — inside the **same** compile-gated write as the
declaration. That is the answer to a `CS0246` rollback: pass the namespace instead of paying a
rejected edit, an `edit_text force=true` on the file header and a `retryWith`.

**`replace_symbol` also edits several files as one compile-gated edit.** Pass `symbolIds` and
`declarations` — one declaration per symbol, paired positionally, at most 20, and more than one entry
per file is allowed. That is how a signature change lands **together with the callers it breaks**:
sent one at a time it is rolled back as a `CompileRegression`, and callee-first ordering does not help
because the callee is what is changing. Unpaired arrays are refused naming both counts, a declaration
whose own name does not match the symbol its position pairs it with is refused from syntax before
anything is compiled (`declarations[3]: declares 'PerEntryOnly', but the paired symbolId addresses
'Threshold'`), and two edits
where one declaration **contains** the other are refused whichever order you send them in, rather than
silently dropping the inner one.

**`list_projects` is the loaded-workspace answer** and carries the language and document counts a
solution file cannot know; `solution_projects` is the one to reach for when the solution is not loaded.

**A `typeSymbolId` resolves against types only.** `add_member`, `extract_interface`,
`move_type_to_file` and `move_type_to_namespace` take a *containing type*, so a short domain name that
is also a property name — `Errors`, `Report`, `Tally` — resolves to the type instead of answering
`AmbiguousSymbol`. A name matching no type at all says so and counts the non-type matches rather than
hiding them.

**A symbol asked about by a second navigation tool steers to the composite.** When `get_symbol_source`
or `find_usages` answers about a symbol id another of `get_symbol` / `get_symbol_source` /
`get_type_outline` / `find_usages` / `find_implementations` already asked about this session, the
response ends with `explore_symbol symbolId="<that id>" answers signature, usages and implementations
in ONE call` — once per id. Take it: the chain you are walking is the composite's payload.

**When the second consecutive call of one tool lands, the response gains one line - once per run, not
on every call after it** —
`2 read_text calls in a row - these are ONE call: paths=["src/A.cs", "src/B.cs"]` — the run's own
DISTINCT arguments, already filled in, whenever every call of the run carried a short identifier one;
otherwise `pass paths=[...]`, naming the plural parameter that tool declares. It is framing, never payload, it says nothing when the call already
used the plural parameter, and the counter resets on any different tool - and on a `read_text` that
carried `startLine`, `endLine`, `tail` or `section`, because `paths=` cannot express a per-entry
range and a steer that asks for the wrong lines is worse than none. Obey it literally: 571 runs
of exactly **two** consecutive calls stay unreachable, because a steer can only ride on a response, and
firing it on the first call was measured to break the one-line success contract on six tools. So batch
on your own judgement: whenever the next two calls are the same tool and independent, send them as one.

**A whole markdown read ends with its section map** - `sections=N - address one with read_text or
edit_text section="..."`, naming up to six of them - so the anchor a `read_text` was paid for is
replaced by an address. It rides only on a read that carried no `headings=`, `section=`, `columns=`
or line range.

**A `changed_files` listing carrying both kinds says how many of each** (`tracked=N untracked=N`), so
a capped listing can never read as though the tracked half was all of it, and one carrying tracked
changes ends with the exact `next: diff_symbols ...` call for them - take that before `diff_text`.

**Git is the other deliberate shell-out beside `build`/`run_tests`**, and the answer to the
end-of-task review, which is defined over the diff. Start
with `changed_files`, then `diff_symbols` to turn the hunks into declaration ids, then
`get_symbol_source` on the two or three
bodies you actually intend to read. `diff_text` returns the raw unified diff and is the last resort —
scope it with `path=`. **`changed_files` and `diff_text` also take `root=`** - any absolute directory, answered without
loading it and tagged `outside-workspace` - so a sibling worktree or another repository needs no
second `load_workspace` and no `git -C` in `Bash`. `diff_symbols` deliberately does **not**: mapping a
hunk onto a declaration needs that directory's Roslyn compilation, so it refuses and names the two
tools that can answer. All three take `baseRef=` (empty compares the working tree against `HEAD`) and
`path=`, and are scoped to the workspace root with git's own `--relative`, so a workspace nested
inside a larger repository never reports a file outside it. On a tree shared with other sessions,
a directory contributing more than five **untracked** files folds into one
`.research/**  +? -?  ?  x40 untracked` row - tracked files stay one per line and the count still counts
every file. **A folded row opens**: the fold key is the first segment BELOW the `path=` scope, so
`changed_files(path: ".research")` descends into it until the files are listed. And `changed_files(path: "src")` is the difference between reading your own change set and reading
everybody's, and `changed_files(exclude: ".research/**")` drops the folders a positive pathspec
cannot leave out. `diff_symbols` tags a hunk `EXACT` only when it sits
inside exactly one declaration; anything else is `HEURISTIC` with the raw line range and the reason.

**Searching.** `query` and `queries` combine, `query` first; an 11th entry is refused naming the cap
rather than truncated, and a blank entry is refused rather than matching everything. An entry that
matches across a line break — a literal containing a newline, or `[\s\S]` / `(?s).` in a regex — is
reported **once, at the line its text starts on**, and the scan resumes on the next line, so every
other entry still sees the lines that match spanned; `search_regex` anchors `^` and `$` to each line.
`search_text`, `search_regex` and `find_files` each accept `pattern` as an alias for their query or
glob — `find_files` accepts `query` too — so the wrong name of the three is never a failed call, while
a parameter name **no** tool declares is refused before the call runs, naming every accepted spelling:
an argument the server does not understand is never silently dropped, because a listing that ignored
your `maxResults` is a confidently wrong answer you cannot detect. **A glob expands `{a,b}`**,
nested and across separators - `**/*.{md,yml}`, `{src,tests}/**/*.cs`, `{src/**/*.cs,notes.md}` -
everywhere a glob is taken, `exclude=` and every `path=` scope included; an unclosed brace is a
literal rather than a swallowed glob. All three skip `bin`, `obj`,
`.git`, `.vs`, `.idea`, `artifacts`, `TestResults`, `node_modules`, directory symlinks and `.claude`
SESSION STATE — the same set every index uses, so a nested agent worktree never doubles a result.
**`.claude/commands`, `agents`, `skills` and `hooks` ARE listed** - project source. The files
directly in `.claude` are not: a session rewrites `settings.local.json` constantly.

**A `.cs` file returned verbatim ends with `symbolIds=[...]`** when the read covered the whole file and
it has at most ten members, so the *next* read is member-scoped. A line-ranged read gets nothing.

**A markdown file over 8 000 characters asked for whole answers its SECTION MAP plus a steer**, not its text - a whole `.md` read averages 5 699 characters against 3 278 for a `.cs` path - and `verbose=true`, a line range, `ranges=`, `tail=`, `section=` or `columns=` opt back into the text.

**`read_text` on a `.cs` path asked for whole answers the outline, not the text** — no `startLine`,
`endLine`, `tail`, `section` or `verbose`. Whole-file `.cs` reads were 71 % of everything this tool
has ever returned and an outline is a third of the tokens. A `.cs` file that is not a document of this
workspace is read as text unchanged. `read_text` also accepts an **absolute path outside every
workspace root**, tagged `outside-workspace`, so comparing a file against another repo needs no second
`load_workspace` and no `workspace=` even with several loaded; every writer still refuses to leave the
workspace. It clips at **40 960** characters unless `maxChars` says otherwise (ceiling 131 072): the
default is set so a whole-file read stays inline in your client rather than being spilled to a file
that answers nothing, and the clip always names `next: startLine=`. A file whose bytes open with a
Unicode byte order mark - UTF-16 LE or BE, UTF-32, UTF-8 - is decoded and served, not refused as
binary, and a write back to it keeps that encoding; only a file carrying a real NUL code unit is
refused.

## Working rules

0. **A response carries no ceremony.** There is **no header echoing the tool name or your arguments**
   — you know what you called. The first line is the count (`4 usages in 2 files`), and when a result
   was clipped it reads `4/17 usages truncated - narrow with <parameter>`. Nothing else is added:
   no "pass verbose=true" hint, no counter that reports a non-event. `verbose=true` restores the old
   shape verbatim — header and `(truncated=…, total=…)` — on every tool that takes it.
   **A `truncated` count is always real.** When the total lands within 10 % of the cap the whole list
   is returned instead — `108 files`, never `100/108 files truncated` — so a listing that says it
   truncated is worth a second, narrower call, and one that does not never is.
1. **Address a symbol by the name a response printed.** An outline prints `OrderService.Submit`, and
   adds the parameter list (`Reconcile(Order, decimal)`) only where the type overloads that name;
   every tool taking a `symbolId` accepts that, the full documentation id
   (`M:Trading.OrderService.Submit(Trading.Order)`), a bare `Submit`, or any qualifier in between.
   A name matching several symbols returns `AmbiguousSymbol` listing their ids — **pick one, never
   guess**. Constructors, operators, indexers, generics and explicit interface implementations keep
   their documentation id in outlines, because a name cannot address them. Every one of those tools
   also accepts `symbol:` as an alias for `symbolId:`, and none of them declares the parameter
   required — a call with neither answers `ERROR InvalidArgument` naming `symbolId`.
   `add_member` addresses a *containing type*, so its canonical name is `typeSymbolId:` — but it
   takes `symbolId:` as well, because that is the name every other symbol-addressed tool uses and
   guessing it was the single most frequent rejected argument in a measured fortnight.
   **Need several members?** `get_symbol_source(symbolIds: [...])` returns them in one response, and
   an id that does not resolve is reported inline as `NOT_RESOLVED <id>` plus its nearest ids, instead of
    failing the call.
   Use it instead of one call per member.
   **When a name an outline just printed still answers `AmbiguousSymbol` or `SaturatedName`, pass the
   file it came from**: `get_symbol_source`, `get_symbol` and `get_type_outline` take `path=`, resolve
   the name inside that document first, and fall back to the solution only when the file holds no
   match — so the answer never needs the full documentation id. A `path=` naming no document of the
   workspace answers `DocumentNotFound` rather than being ignored.
2. **Read the confidence tag.** `EXACT` came from the Roslyn semantic model. `HEURISTIC` came from a
   text or index match — verify before acting on it.
3. **`dryRun: true` first on any edit you are unsure about.** You get the unified diff, the diagnostic
   counts, and nothing is written; the response says `dryRun` so it can never be mistaken for a write.
4. **A successful edit answers in one line per changed file, not a diff.**
   `<workspace-relative path>  changedLines=N` - and that count is the lines that actually changed,
   summed over each separate change, not the span between the first and the last one; a diff is one
   `@@` hunk per change. You already know what you wrote, so the diff is not
   repeated back to you, and there is no `N files changed` line above it, because the lines are the
   count. `edit_text` and `write_text` print the **file name alone**, because you
   passed the path in. A clean gate prints no counters at all; `errors=`/`warnings=` appear only when
   there is a non-zero count or delta to report. Pass `verbose=true` on any edit, refactor,
   `write_text`, `edit_text`, `xaml_*`, `razor_*`, `resx_*`, `project_*`, `package_*` or `solution_*`
   write to get the full unified diff. **`dryRun: true` is never condensed** — there the diff *is* the
   answer.
   **Every caveat still prints in full**, condensed or not: the `errors=/warnings=` deltas, a rollback,
   a new compile error, `0 files changed` — which now also carries
   `NOTE no change - the result is identical to what is already there`, so a no-op is never
   byte-identical to a silent drop — `compileGate=unavailable`, `workspace=stale`, `UNFIXED`,
   `designerStale`, and the `NOT rewritten` list a XAML-aware rename leaves — so a short answer never
   hides something you must act on. A rename of a **Razor component** and a Razor edit whose compile
   gate could not run keep the whole diff, because the result itself carries a caveat. Do not pass
   `verbose=true` defensively; ask for it when you actually intend to read the diff.

5. **Every edit reports its diagnostics.** Each mutation and each `dryRun` carries
   `errors=N (+D) warnings=N (+D)` for the changed projects and their dependents, and
   `info=N introduced` for an info-severity **compiler** diagnostic, named under `verbose=true`.
   Analyzers are NOT run on an edit, so a `CA`/`IDE` rule still needs `analyze`. A `dryRun` that *would* be rolled back says
   `WARNING … would be rolled back` and names the errors; a `(+0)` delta alone is **not** proof the
   edit is safe.
5. **Edits are compile-gated — but the gate is the semantic model, not an emit.** `errors=0 (+0)`
   does **not** cover emit-time or source-generator errors, so `build` is worth one call **before you
   push, not after every edit**; the first *applied* gated edit of a process says so once as
   `gate=semantic …`.
   An edit introducing a new compile error is rolled back and the error returned. `allowErrors: true` opts out — use it only mid-refactor on purpose.
   **A rollback keeps your text**: the error ends `retryWith=r3`, and `replace_symbol`,
   `replace_symbol_body` and `add_member` take `retryWith: "r3"` to replay exactly what was rejected —
   after you add the missing callee, or together with `allowErrors: true`. Never re-send the whole
   declaration to retry; the server holds the last 8 rejections and says so if a token has expired.
   **When only ONE declaration of a batch was wrong, correct that one and replay the rest**:
   `replace_symbol retryWith="r3" fix=["2=<corrected>"]` replaces the held entry at the 0-based index
   the rejection printed and replays the others unchanged — measured at ~4 700 characters saved per
   retry on a 5-entry batch. `fix=["add:1=..."]` corrects a held `add=` helper the same way.
   **`append: true` beside a token ADDS the `symbolIds=`/`declarations=` pairs you pass to the held
   batch** - how a `CS7036` rollback's callers land with the member; a bare `symbolIds=` still
   OVERRIDES, which is how a mis-typed id is corrected.
   Better still, do not earn the rollback: `replace_symbol add=[…]` appends the helper in the same
   edit. **`add=`, `addTo=` and `usings=` are held with the token too**, so a retry names the token
   and nothing else; pass any of them again only to override what is held, and **pass `usings: []` to
   DROP the imports it holds** - an empty list means "none", not "keep what you sent".
   **The token is the last line of the rejection and is alone on it**, so reading it to the end of the
   line is safe; the sentence explaining what is held sits on the line above it.
   **When every new error is a `CS0104` ambiguity caused by an import this edit added**, the remedy
   names that `usings=` entry - `the ambiguity was introduced by usings=["ModelContextProtocol.Protocol"]
   which this edit added - retry with usings=[] and the retryWith token below to drop it` - rather than
   telling you to fix an edit whose text was fine.
   **A remedy never names a parameter the rejecting tool does not declare**: `write_text` and
   `edit_text` take no `usings=` and no `retryWith=`, so their rollback says to put the directive in
   the content you send instead.
   **When every new error is just a missing import, the remedy names the one-call fix**: a rollback
   whose errors are all `CS0246`/`CS0103` for names the project resolves in exactly one namespace each
   answers `remedy: retry with usings=["System.Collections.Immutable"] and the retryWith token below`.
   Do exactly that: `usings=` lands the directive inside the *same* compile-gated edit, so the whole
   recovery is one call rather than an `edit_text force=true` on the header plus a `retryWith`. The
   directive is never added behind your back. A `dryRun` names the same parameter without a token.
   **When every new error is a broken *call* (`CS7036`/`CS1501`/`CS1503`/`CS1729`) the remedy names
   the callers instead** — `send these callers in the same replace_symbol symbolIds/declarations
   batch: OrderRouter.Route(Order)`. Paste them into `replace_symbol symbolIds=` beside the member you
   changed: that is the only ordering that works when the callee is what moved. `dryRun` prints it
   too, and nothing is named when a caller cannot be proven.
   **When every new error is a missing implementation (`CS0535`/`CS0534`) the remedy names the types
   that owe it** — `the new member is declared in no implementation: Fixture.Trading.NullOrderRepository`
   — and the sanctioned sequence, which is the one shape that works when the member cannot exist on
   both sides at once: retry with `allowErrors=true` and the token to land the interface or abstract
   declaration, then one `add_member` per named type. The tree does not compile between those calls,
   so make them the next ones.
   **A token belongs to the workspace it was rejected in, and to the tool that issued it**: replaying
   it against another workspace - a sibling worktree where the same symbol id resolves - is refused
   naming both roots, instead of landing the held declaration in the wrong tree, and replaying it with
   the wrong edit tool is refused naming the tool that can apply what it holds, so learning that costs
   no second call. Every diagnostic a rollback lists names its file
   **workspace-relative**, like every other record.
6. **Truncation tells you what to do.** `<shown>/<total> <unit> truncated` is followed by
   `- narrow with <parameter>`. Follow that, rather than re-running with a bigger `maxResults` and
   paying for the whole list. A **complete** listing of 25 records or more names the same parameter,
   so an uncapped tool like `list_projects` still tells you `filter=` exists — that is an offer, not a
   truncation. `read_text` is the line-ranged equivalent: a read clipped by the tool's own cap ends
   with `next: startLine=<first line not returned> (total=<lines>)`, and on a `.cs` file an
   `outline: get_file_outline path=…` steer, on a `.md` file a `headings=true` then `section=` steer —
   follow it instead of paging, because the heading map is one call and paging is one call per page.
   A read your own `startLine`/`endLine` ended says nothing —
   you already know where it stopped. When a **character** budget runs out inside a line you also get
   `line N was cut mid-way`; that is not a `startLine` you can follow, because a line range cannot
   resume at a character offset — raise `maxChars` and re-read that line. A `startLine` beyond the
   end of the file answers `startLine=N is past the last line (total=T)` rather than an empty
   payload, so an out-of-range read is never mistaken for an empty file — and `total=T` is the
   one-call answer to "how long is this file?".
   `list_projects` prints each project's workspace-relative path, so the name it lists and the
   `project=` argument you feed to `build`/`run_tests` come from the same line.
   `workspace_status` prints `mapped=N` **only** when this process is holding analyzer or
   source-generator assemblies **inside the workspace root** (or under `verbose=true`); a non-zero
   count means an external `dotnet build` over those files will fail `MSB3027` until the server
   restarts. One mapped from **outside** the root — the NuGet package cache, an SDK component — is
   not counted: no build writes there, so it cannot raise `MSB3027`.
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
    The exception, so you can plan around it: `xaml_find` and `xaml_validate includeUnused=true` need
    the parsed document of every file, because they answer about arbitrary attribute content — beyond
    128 cached documents they re-parse. Those two are worth asking once and keeping; the rest are free
    to repeat. `find_usages`, `rename_symbol` and `explore_symbol` filter by index record first and
    parse only the files that could match, so they are cheap even on a large XAML tree.

12. **A hit list ends with the argument its consumer takes — lift it, do not retype it.**
    `search_text`, `search_regex`, `find_files`, `changed_files` and `find_usages` end with
    `paths=["src/A.cs", "src/B.cs"]` (deduped, at most 10, JSON-escaped) whenever they matched more
    than one file: paste it straight into `read_text paths=` or `get_file_outline paths=`. An outline
    of at most ten members ends with `symbolIds=[…]` for `get_symbol_source symbolIds=`; a wider one
    offers `contains=` instead, because ten of a hundred members is a batch nobody asked for. Neither
    line appears when there is nothing to batch.

13. **A line starting `UPDATE terse` is not part of the answer — it is a message for the user.** Once per
    server process, at most once a day, the first tool response may carry one extra last line:
    `UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp`. Everything
    above it is the tool's real answer and is unaffected. Tell the user the newer version exists and
    what to run; do **not** run the update yourself, do not retry the call, and do not treat the line as
    an error. It appears once and never repeats in that session. After the user updates, the next
    `terse serve` rewrites the installed `SKILL.md` and the `terse guard` hook to match the new binary,
    so the skill you are reading always describes the binary you are talking to.

14. **Independent calls go in one message.** Before every message that will carry a tool call, answer
    one question: *is there another call I already know I need, whose arguments do NOT depend on this
    one's result?* If yes, put them in the SAME message. Several `tool_use` blocks in one assistant
    message run concurrently; one call per message pays a **6 136 ms** model gap each, and that gap is
    dead loop no server change can shorten.

    ```
    GOOD - one message, three tool_use blocks, none depends on another:
      changed_files
      workspace_status
      read_text path="CHANGELOG.md" section="## [Unreleased]"

    GOOD - one message, two blocks, different targets:
      search_text query="OrderId" glob="src/**/*.cs"
      get_file_outline path="src/Trading/OrderService.cs"

    BAD - three messages, three gaps, ~18 s of dead loop:
      read_text path="a.md"  ->  read_text path="b.md"  ->  read_text path="c.md"
    GOOD - one call:
      read_text paths=["a.md", "b.md", "c.md"]
    ```

    **The one exception: when a call needs a value a previous call returns** — a symbol id from an
    outline, a path from `changed_files`, a `retryWith` token from a rollback — call them
    sequentially, and **never guess a parameter to make a call parallel**. In that same fortnight
    **13 820** calls sat in runs of three or more of the same tool - each one a `paths=`/`edits=`/`files=`
    batch not used, or a parallel message not sent. The argument you SEND costs too: a run of writes
    re-sends its whole argument frame, and the four writers sent **19% of all tool output** that way.
15. **A subagent does not inherit this skill — the brief carries it, or the delegate greps.** A spawn
    aimed at this workspace carries, inline: the mandate and ban list above, the workspace name, **the
    `changed_files` output and the `diff_symbols` ids as its scope**, and a call ceiling. A delegate
    that must re-derive the diff walks the whole tree — measured p99 **2 303 s**, max **6 589 s**, 110
    minutes inside one call. One review round, then the fixes, then a re-review of the fixes only. And
    spawn only when the work does not fit one context or genuinely runs beside yours; otherwise inline
    is cheaper, because a spawn pays a full context prime plus its own serial round trips.

### The advertised surface can be narrower than the whole surface

`workspace_status` prints a `tools=` note when it is: the workspace holds no `.xaml`, `.razor` or
`.resx`, the project checked in a `.terse.json`, or the server was started with `--tools core`. A
hidden tool is **unadvertised, not removed** - it still answers when called by name, so that note is
never a reason to fall back to `Read` or `Grep`.

To narrow it deliberately, `write_text` a `.terse.json` at the repo root. Every one from the user's
home (`$TERSE_HOME`, else the user profile) down to the server's directory is read, NEARER wins per
setting like `.editorconfig`, and the walk never climbs above the repository root:

```json
{
  "tools": {
    "groups": { "xaml": false, "razor": false },
    "names": { "search_regex": false }
  }
}
```

`groups` takes `analysis`, `build`, `edit`, `file`, `git`, `navigation`, `project`, `razor`,
`refactor`, `resx`, `workspace` or `xaml`; `names` takes a tool name and outranks its group; an
explicit `true` outranks the markup narrowing and `--tools core`. An unknown or non-boolean key is
named back rather than dropped, an unreadable file advertises everything, and the `PreToolUse` guard
reads the same file, so a built-in whose every replacement the project disabled stops being denied.
An undeclared setting keeps the value the file above gave it, so a home file hides a group everywhere
while one project re-advertises it with an explicit `true`.
## Code policy - when an edit is refused for style, not for compiling

A project can make this server **reject an edit that violates its standards**, through a `policy`
section in the same `.terse.json`, cascading exactly as the `tools` half does. `terse install` writes
the home file with every rule at its default, and each later `terse serve` adds the rules a new version
introduced without changing a value the user set. It is **off unless that section
exists - except `TERSE112 comments` and `TERSE113 xmlDocs`**, both enforced at `warn` with no
`.terse.json` at all: an edit introducing a `//` or `/* */` answers `WARNING policy  TERSE112 ...` and
one introducing a `///` block answers `WARNING policy  TERSE113 ...`, and both still land. Make the
code say it instead. `{"policy":{"enabled":false}}` turns them off - NOT
`{"rules":{"comments":false}}`, because declaring a `policy` section turns the other twelve rules ON -
all at `warn`, `chainedReferences` off, so **nothing rejects until you ask**:
`"rules":{"comments":{"action":"reject"}}` refuses. When on, an edit answers
`ERROR PolicyViolation` naming each rule, the declaration, measured against allowed, and a `fix:` line.

**Only what the edit INTRODUCES counts** - a violation already in the file does not block you, so never
"fix" unrelated members to get an edit through. A finding is keyed by rule, path and declaration, not
by its measured value, so neither improving nor worsening an already-violating member registers.

Fourteen rules, `TERSE100`-`TERSE113`: cognitive complexity, method statements, methods per type,
constructor dependencies, parameter count, method-name length, meaningless type suffixes, naming per
declaration kind, `async void`, condition operands, chained references (off by default), nesting depth,
and **comments (`TERSE112`) and XML doc comments (`TERSE113`), the two rules that are ON at `warn` with
no `.terse.json` at all**. `TERSE112` never flags a `///` block and `TERSE113` never flags a `//` one.
Each is `reject`, `warn` or `off` and every one DEFAULTS to `warn`, so a rule only refuses an edit
where a `.terse.json` asked it to; a `warn` rule lets the edit land and answers `WARNING policy  ...`.
Cognitive complexity is a **percentage of a threshold** - default `150`% of `10`, so a score above 15
fails: `cognitive complexity 21 (210% of threshold 10) exceeds 150% (15)`.

**`allowPolicy=true` is the escape hatch and is never silent.** Every tool whose edit reaches the gate
takes it - `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`,
`write_text`, `extract_interface`, `move_type_to_file`, `move_type_to_namespace` and
`change_signature`; the edit lands and the
response carries `WARNING policy overridden` naming every rule bypassed. A project setting
`"allowOverride": false` refuses it. A rejection also names a `retryWith` token holding your
declaration, so the corrected retry costs a token, not the payload.

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

A top-level `"action"` sets every rule at once - the one switch between declining an edit and warning.
An unknown rule key, a bad regex or an unrecognised action is **named back on the next edit** as a
`WARNING`, never silently dropped. The policy half of the file is re-read whenever it changes; the
`tools` half below is read once at startup.


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
passed through — `resx_set` on one is refused rather than corrupting it. `resx_set(entries: ...)` writes
every `Key=Value` line in one pass, and a line with no separator is named by number and refuses the
batch rather than vanishing from it. **`comment=` reaches the whole batch** - the single key, every
line of `entries=` and every file of `files=` - so one key across 23 locales is 3 calls, not 23, and a
line written `Key=Value\tComment` overrides it for that key - so a value that must itself CONTAIN a
tab is written with `key=`/`value=`, which never splits. With `files=`, `path=` is only the default
target of an entry that declares none, so a batch where every entry names its own file needs no
`path=`. A repeated `designerStale=` note is printed once per call, not once per file. `resx_remove` covers every file of
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
`find_usages` shows the same XAML sites, so check the blast radius before renaming. A C# comment or string literal the old name survives in is
reported the same way and never rewritten, because no compiler checks that text.

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
`RZR009` an `@inject` nothing registers (`HEURISTIC`; services the Blazor host provides —
`NavigationManager`, `HttpClient`, `IJSRuntime`, `IStringLocalizer` and friends — are never reported,
and when the scan meets `Add*` extension calls whose registered types it cannot read the finding says
the service may live inside one of them rather than asserting a runtime failure) · `RZR010` markup that will not parse. Razor's
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

**A green run answers in one line** —
`run_tests PASSED  passed=478 skipped=0 total=478 durationMs=122371 elapsedMs=476900` — where
`durationMs` is summed test time and `elapsedMs` is wall clock — so running the suite after every
change is nearly free. **A suite pathologically slow for its own size names itself**: past 5 000 ms per test the verdict gains
` slowAssembly=<name> <n>ms/test`. That is a 31x regression announcing itself, not a big-suite warning.
It needs **at least five executed tests** in that project, below which the mean is all fixed cost.
A run that spanned **more than one project** appends `concurrency=<summed/wall>x` plus
`Name:total/durationMs`
per project to that same line - and a run that already prints its counters in full adds the slowest
test when concurrency is under 2x
(`… durationMs=122371  TerseSharp.UnitTests:310/12043ms  TerseSharp.E2ETests:168/110328ms`). A
single-project run is unchanged. A run that **built** also carries that build's own verdict on the
same line - `build=ok errors=0 warnings=0` - so reading the build result before the test
result costs no second call; `noBuild=true` carries nothing. `build` behaves the same way
(`build ok  errors=0 warnings=0  elapsedMs=4235`), warnings included: a build that succeeds is one
line however many warnings it produced, and a build that fails lists errors only. `warnings=` counts
what that build emitted, so a build that recompiled nothing reports `0`.
The short form is only ever emitted when there is nothing else to report, so do not pass
`verbose=true` "to be sure". Anything that is not a clean pass returns the full report —
`passed= failed= skipped= total=`, then one block per failure with the message, expected and actual
values, and one `file:line` frame. Fix the test from that block, never `dotnet test`.

| Goal | Call |
|---|---|
| whole solution | `run_tests` — built **once**, then each test assembly run directly where its runner allows, **concurrently**, one process each; `timeoutSeconds` bounds **each**, `parallel: 1` restores the single `dotnet test` |
| one project | `run_tests(project)` — a project **name** or a path to the `.csproj` |
| one test, or a class/namespace prefix | `run_tests(test)` — not combined with `filter` |
| several tests or classes of the same project | `run_tests(tests: [...])` — at most 10, combined into ONE filter expression and ONE verdict line, which replaces one targeted run per class |
| a raw VSTest expression | `run_tests(filter)` |
| only the test projects your change can reach | `run_tests(changed: true)` — the test projects that transitively reference a project you changed since the workspace loaded, at **assembly** granularity, naming both what it ran and what it skipped. Falls back to one whole-solution run, saying why, whenever it cannot reason (nothing changed, a changed file belongs to no project, no test project depends on it) or the change reaches more than 10 test projects. It never silently runs less than it should. Ignored when `project=` is passed |
| several projects at once | `run_tests(projects: [...], parallel: N)` — concurrent; `1` is serial |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` — a red `run_tests` verdict ends with that exact call |
| only some of what just failed | `rerun_failed(tests: [...], exclude: [...])` - the answer names how many remembered failures it skipped |
| the slowest N | `run_tests(slowest: 10)` |
| names without running | `list_tests(contains)` |
| the full report on a green run | `run_tests(verbose: true)` |

`test=` is a **substring** match, so a name that is a prefix of another (`…Submits` vs
`…SubmitsTwice`) runs both — check `total=`, and use `filter="FullyQualifiedName=<name>"` for exactly
one.

`total=0` with a `WARNING` means **nothing ran** — a filter typo, not a green suite. A run that
produced no results says so, and never `0 failures`.

**A run that produced no results, and a build that failed, echo the command line they ran** -
`command: dotnet test ...` - because that is exactly the case where the arguments are the answer and
the payload is otherwise empty. `verbose=true` echoes it on any run.

**`STALE n document(s) changed after this run started`** ends a `build`/`run_tests` verdict when an
edit landed mid-run: the answer is about the tree as it WAS - re-run it. It counts only documents a
build or a test run can read, so a markdown working note written beside the run never makes a verdict
stale; a `.cs`, `.razor`, `.resx`, `.csproj`, `.props`, `.targets` or `.txt` write does. It **names
up to three of them**, workspace-relative, in parentheses after the count.

**A stopped run says why.** Above 30 s, `timeoutSeconds` arms VSTest's blame collector 15 s below it,
so a *hung* test is named in
`WARNING the run was stopped while these test(s) were still running: <name>`; a merely *slow* one
answers `FAILED timed out after <n> ms`, a `remedy:` and the lines it printed.
`WARNING … output stream stayed open` means the capture is partial.

**A batch is concurrent by default**, `parallel` at a time (default per-core); each is built before
the fan-out then run `--no-build`, and a build that fails runs nothing. `parallel=1` is serial and the
only mode that stops at the first timeout. **A single project ignores `parallel`.**

**Microsoft.Testing.Platform needs nothing extra from you.** When `global.json` selects it
(`"test": { "runner": "Microsoft.Testing.Platform" }`), the whole `dotnet test` invocation is rebuilt
for that host — it refuses the **entire session** over one VSTest-shaped argument. `list_tests`
answers there too: the SDK hosts the test application in server mode and discards its `--list-tests`
output (dotnet/sdk#49754), so terse builds the target, resolves each test project's `TargetPath`, and
runs the test module itself with `--list-tests`. There, `timeoutSeconds` bounds **each** child rather
than the call, and a multi-targeted project needs `targetFramework=`. `runSettings=` stays VSTest-only.

**A suite can hand you a run-level note.** `run_tests` sets `TERSE_RESULTS_DIRECTORY` on the
`dotnet test` process — per project in a batch, so `.trx` names cannot collide — and whatever it writes to `$TERSE_RESULTS_DIRECTORY/terse-notes*.txt`
comes back under `run notes:` when `verbose=true`, bounded to 20 lines. It is the only channel that
survives a **green** run — a test host's own console output never reaches `run_tests` at any
verbosity, because the runner captures it per test.

`project=` takes the name `list_projects` prints as readily as a path — `run_tests(project: "Trading.Tests")`
resolves against the solution's projects first and then against the `*.csproj` under the workspace
root, so a test project outside the solution still runs. An unknown name answers `ERROR ProjectNotFound`
naming the closest projects and a name two projects share answers `ERROR AmbiguousProject` listing
both; neither is ever handed to MSBuild as a path.

When a locked output file blocks the build that `build`, `run_tests`, `rerun_failed`, `list_tests` or
`clean` runs, the response says so (`WARNING a locked output file blocked the operation`) and the server unloads the
workspace THIS CALL RESOLVED TO, retries and reloads, reporting it in a `NOTE` — so no
`unload_workspace` by hand, even with a second workspace loaded, which keeps its own compilations. When the output is **still** locked it lists every process the build
named, one
`holder pid=… <name> startedUtc=… age=… exe=… cmd=…` line each — the age says how long it has run and `cmd=` is the command line from its assembly name on (Windows and Linux; absent where the platform cannot answer it), which is what telling a live run from a stranded one needs — the executable workspace-relative when it lives
under the root, which tells a test host running out of *this* tree's `bin/` from another session's —
classified as this terse server, an MSBuild or BuildHost (including one an earlier terse load spawned
out of this tree's `bin/`), a live `testhost` to wait for rather than stop, a bare `dotnet` host, or a
pid already gone, and when MSBuild names none they are scanned for in-server. **A bare `dotnet` holder is classified against the process table**: when a test host
of *this* tree is running, the line says so by name and pid and tells you to wait rather than stop it -
tagged `HEURISTIC`, because the association is by tree and not by parentage - and when none is, it says
that too, which is what makes "another session's live E2E run" distinguishable from "a stranded fixture
host" without a shell-out. The one holder it rules out is the analyzer set, mapped from a shadow copy and never
from a project's own output; read the `holder` lines before stopping anything.

**One lock is refused before it happens**: when the loaded solution builds the assembly this server
runs from — a `terse call` probe out of a repo's own `bin/` — the build and test tools refuse up front
naming `MSB3026`. Run the probe from a copy outside the solution.

## When a tool refuses

Errors are `ERROR <Code>` plus a `remedy:` line. `SymbolNotFound` suggests the nearest names;
`AmbiguousSymbol` lists the candidates and says how many of the total it shows; `SaturatedName` means
too many symbols carry that name **exactly** - a unique exact match resolves however many fuzzy
candidates share its letters, and an already-dotted name is told to pass `symbolId="T:<fqn>"`. It is
otherwise reached only by a **bare** name:
a `Type.Member` whose member name saturates is resolved through the members of the types called
`Type`, so qualifying the name really is the fix the remedy names, and a type declaring no such member
answers `SymbolNotFound` listing its members instead of a saturation count; `OutOfWorkspace` means the path
escaped the workspace root; `ProjectNotFound` and `AmbiguousProject` come from a `project=` that names
no project or two, and list the candidates; `InvalidArgument` naming a **missing** or **unrecognized**
parameter means the argument names were wrong, and the remedy lists the ones the tool declares; an
`InvalidArgument` carrying a `JsonException` also names the **array** parameter it could not convert
and quotes the ~80 characters around the offending byte, so a 9 000-character `declarations=` is
located without re-sending it - and a declaration that reaches the parser and fails there is answered
the same way, `at offset 27 of 28: public int Unused() => 7 + ;`, prefixed with `declarations[1]:`
when the call was batched;
`ReadOnly` means the server runs with `--read-only`; `Transient` means MSBuild's out-of-process build
host dropped the call - the project file was restored, a file the edit was adding may already be on
disk, and the answer is to retry the same call rather than to report a defect.

Read the `remedy:` and fix the call. Falling back to `Read`/`Grep` is the one outcome this server
exists to prevent.

**Need a call of a tool that actually works?** For the tools whose valid arguments are not derivable
from the schema — the ten `razor_*` tools, `package_add`/`package_remove` and the three glob-taking
search and file tools — the `remedy:` of a
rejected call ends with `example: <a complete, working call>` — and `find_files`, `search_text`,
`search_regex`, `package_add` and `package_remove` carry theirs in the **advertised description**, so
you never earn those the hard way. For the ten `razor_*` tools calling one with no arguments on purpose
is the one-call way to get that shape; do not go read a test file for it.

**A claim about tool *behaviour* is proven against a freshly built binary, never against this
server.** The server answering you is whatever `dotnet tool install`/`update` last put on PATH — it is
not your working tree and it does not pick up a build you just ran. When you have edited the server
and need to know what it now does, run the one-shot probe: it starts a separate process, answers one
call and exits.

```
dotnet "<path to terse.dll>" call <tool> --workspace <path to the solution> --json '{"path":"src/Foo.cs"}'
```

`--workspace` is mandatory in practice: without it the probe answers about an auto-discovered
solution rather than the one under test. `doctor` prints the running server's assembly path and this
exact command shape on its `version` line, so the path never has to be guessed, and
`workspace_status` prints `terse=<version>` — read one of them before saying what a tool does or does
not do. The probe costs about 3 s against 13 s for the narrowest filtered E2E run.

The probe binds `--json` through the **same argument filter the stdio client goes through**, so an
argument the server would refuse is refused there too, instead of being dropped silently and proving
a call no client can make.
