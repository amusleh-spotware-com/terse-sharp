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
call what is in **Use**. Every tool the server advertises is in this table exactly once.

| Job | Instead of | Use | Why |
|---|---|---|---|
| **Workspace** | — | `workspace_status` | solution, worktree, branch, project and document counts, plus `advertised=<n> tools <t> tokens` for what this session's `tools/list` really costs - `verbose=true` splits that total into `toolDescriptions`, `parameterDescriptions`, `schemaFrame` and `names`; its last line is `terse=<version>`, the one place the running binary names itself — read it before claiming what a tool does or does not do. `verbose=true` adds the whole surface beside the narrowed one, `advertised=20 tools 6000 tokens of 88 tools 25857` |
| **Workspace** | `Bash: terse doctor` | `workspace_status(verbose: true)` | the six self-check lines an agent acts on, in-server and without the ~40 s shell-out: `roslyn` (the SDK's Roslyn against the one terse carries — the check that explains a dead Razor generator), `assets`, `guard coverage`, `memory` (what every live terse server holds), `shadow` (whether an analyzer was mapped IN PLACE rather than from the shadow cache) and `phases` |
| **Workspace** | globbing for `*.sln` | `load_workspace(path, discover: true)` | lists every solution and project under a directory without loading one; auto-discovery only walks *up* from the working directory |
| **Workspace** | — | `load_workspace` | one call per solution; `targetFramework:` picks the framework every semantic tool answers from, `reload: true` forces a re-read you should almost never need |
| **Workspace** | — | `list_workspaces` | every loaded solution with its git branch and worktree, and the absolute path `unload_workspace` takes |
| **Workspace** | — | `unload_workspace(path)` | releases the MSBuild file locks; addressed by the solution **path**, not a worktree name (`workspace=` is an alias for `path=`) |
| **Workspace** | — | `list_projects(filter)` | name, language, document count; the name it prints is exactly what `build`, `run_tests`, `list_tests` and `clean` accept as `project=` |
| **Workspace** | one `project_properties` call per project for "which projects set X" | `list_projects(properties: "IsTestProject,TargetFramework")` | each line gains `name=value` from MSBuild's **evaluated** set, so a `Directory.Build.props` value is answered rather than missed, and an undefined one reads `(unset)`; refused beside `path=` |
| **Workspace** | reading a `.csproj` to learn whether an edit is gated | `list_projects(path: "src/Foo.cs")` | which project compiles that file, from the evaluated `EnableDefaultItems` the edit path reads; no project compiling it is exactly when a write is *not* gated |
| **Navigate** | `Read` a `.cs` file | `get_file_outline(path)` | every type and member with signatures and line ranges, no bodies; `usings: true` adds the file's own using directives, `parameterNames: false` prints parameter types without their names for about an eighth fewer tokens |
| **Navigate** | `read_text` a `.cs` file no project compiles | `get_file_outline(path)` | a path inside the workspace root that belongs to no project — a fixture tree kept outside the solution — is **parsed from its own text**, not refused, and the answer ends `HEURISTIC parsed from the file's own text`. Outside the root it is still refused |
| **Navigate** | `Read` **several** `.cs` files | `get_file_outline(paths: [...])` | up to 10 in one response, each under its own path line; an unresolved path is reported inline as `NOT_FOUND`, never a failed call |
| **Navigate** | outlining a 45-member file to find five members | `get_file_outline(path, contains: "Total")` | keeps only the matching members, under their declaring type, with an `N of M members` line so the omission is never silent; `get_type_outline` takes it too. An **unfiltered** outline lists at most **40 members per type** and counts the rest as `40 of 104 members - contains= or all=true`, so a wide type costs a steer instead of a payload; `all: true` lists every one, and the omission is always counted, never silent |
| **Navigate** | `read_text` a whole `.cs` file | it already answers the outline | a `.cs` path with no `startLine`, `endLine`, `tail`, `section` or `verbose` returns `get_file_outline`'s answer plus a steer, because the text is ~3x the tokens; pass `verbose: true` or a line range for the text |
| **Navigate** | `Read` a whole class's source | `get_symbol_source(symbolId)` on a **type** id | answers `get_type_outline`'s member list plus a steer to one member, not the whole file's text; `verbose: true` opts back into the source. A type with ONE declaring reference whose **rendered source** - the declaration *plus* its doc comment, which is what the response carries - is at most 4 lines and 200 characters answers that source instead, because withholding something shorter than the steer saves nothing |
| **Navigate** | `Read` to see one method | `get_symbol_source(symbolId)` | that member only, dedented; `verbose: true` for it verbatim, `comments: false` to drop doc and inline comments when you are orienting rather than editing |
| **Navigate** | `Read` to see **several** methods | `get_symbol_source(symbolIds: [...])` | all of them in one response; an id that does not resolve is reported `NOT_RESOLVED <id>` carrying the nearest ids when the miss is close, never a failed call |
| **Navigate** | `Read` to learn a class's API | `get_type_outline(symbolId)` | member list, no bodies; `parameterNames: false` there too |
| **Navigate** | — | `get_symbol(symbolId)` | signature, kind, accessibility, location and XML doc of one symbol |
| **Navigate** | a name an outline printed that answers `SaturatedName` or `AmbiguousSymbol` | `get_symbol_source(symbolId, path: "src/Trading/OrderService.cs")` | `path=` resolves the name inside that file first and only falls back to the solution when the file holds no match — `get_symbol` and `get_type_outline` take it too, `symbolIds=` scopes every id in the batch, and a `path=` naming no document answers `DocumentNotFound` instead of being ignored |
| **Navigate** | `Grep` for a type or member name | `search_symbols(query)` | declarations only; CamelHump (`OSvc` finds `OrderService`); production declarations first, and when the test half also matches it is folded to one `N more in test projects - scope=test` line |
| **Navigate** | asking the model whether a framework or NuGet member exists | `search_symbols` · `get_type_outline` · `get_symbol` · `get_symbol_source` | a name **no source declaration** matches falls back to the **referenced assemblies**: `JsonSerializer` and `System.Threading.Lock` answer real signatures tagged `System.Runtime 10.0.0.0` instead of `0 symbols`/`NOT_RESOLVED`. Exact type name only - no CamelHump, no substring - and no source, so members come with no line ranges. A `kind=` that is not a type kind, or any `scope=`, declines the fallback and says so rather than answering off-filter |
| **Navigate** | a name the tests declare dozens of times | `search_symbols(query, scope: "src")` | keeps one half of the solution - `src` for the production projects, `test` for the ones referencing a test framework; an unknown value is refused rather than searching everything |
| **Navigate** | a common name that buries the one declaration you meant | `search_symbols(query, path: "src/Trading/OrderBook.cs")` | matches inside that file are answered first and the whole solution is searched only when it declares none - the `path=` `get_symbol`, `get_symbol_source` and `get_type_outline` already take; a path naming no document answers `DocumentNotFound`, and a fallback says so as `NOTE path= declared no match` |
| **Navigate** | `Grep` to find callers | `find_usages(symbolId)` | real references, one line per file, each marked `src` or `test`; a usage inside generated code is tagged `gen` — real, but never edit it |
| **Navigate** | `Grep` for implementers | `find_implementations(symbolId)` | resolved through the interface |
| **Navigate** | the `get_file_outline` → `get_symbol_source` pair, when learning what a symbol IS | `explore_symbol(symbolId)` | signature, doc, location, usages split src/test, implementations and XAML sites, in **one** call |
| **Navigate** | judging a rename before doing it | `impact_of(symbolId)` | every affected file, XAML site and recompiling project |
| **Navigate** | searching for the tests a change can break | `impact_of(symbolId, tests: true)` | the test classes referencing it, each a ready `run_tests test=` argument; DIRECT references only, so it narrows a run and never replaces one |
| **What grep cannot reach** | "where is `IFoo` registered?" | `find_registrations(query)` | open generics, factories and `Add*` extensions defeat grep; a registration inside an `Add*` helper is also reported at the call site as `via AddTrading()` |
| **What grep cannot reach** | "what endpoints exist?" | `list_endpoints()` | every ASP.NET Core `Map*` with the member it sits in |
| **Files** | "find the file called X" | `find_files(name: "orderrouter")` | a plain file-name substring, case-insensitive, no glob to get right; combines with `glob=`, which selects first, and a glob that matched nothing names it |
| **Files** | `ls` in a directory outside the workspace | `find_files(glob, root: "C:/Users/me/AppData/Local/terse-analyzers")` | any absolute directory, tagged `outside-workspace`, with full paths on its `paths=[...]` line; refused beside `tracked=true`, which needs a repository this tool did not load |
| **Files** | `Glob` / `ls` | `find_files(glob)` | `bin`, `obj`, `.git`, `.claude`, `.vs`, `.idea`, `artifacts`, `TestResults`, `node_modules` and directory symlinks excluded |
| **Files** | globbing a whole tree to learn its shape | `find_files(glob, depth: 2)` | everything below the 2nd path segment folds into one `src/TerseSharp.Core/**  x94 files` row - 11 rows here against 367; the count line still counts every file, and a single-match directory stays its file |
| **Files** | `ls -l` / `Get-Item` for a size or a timestamp | `find_files(glob, stamps: true)` | each record gains the file's UTC last-write time and byte length, so "when was this written, and how big is it?" needs no shell; `glob` takes a **concrete path** as readily as a pattern, so one file's size is one call |
| **Files** | `Bash: git ls-files` to tell a checked-in file from a scratch one | `find_files(glob, tracked: true)` | only the files git tracks, so build output and another session's untracked notes drop out; the bare `git ls-files` is denied by the guard, every flagged form is not |
| **Files** | `Grep` in non-code files | `search_text(query)` / `search_regex(query)` | tagged `HEURISTIC` once for the whole response, not per record - these two answer nothing else; the count line counts matching **lines**, at most one per line, and a zero result proves absence only in the files it searched |
| **Files** | `grep -n -e A -e B -e C` / one search per literal | `search_text(queries: ["I175", "I176", "I177"])` | up to 10 literals in **one** pass over the same file set; every record carries `q1`..`qN` for the position of its literal in `queries=`, which a regex alternation cannot tell you. A line matching several is **one** record tagged `q1,q3` in query order, so a tag absent from a record means that literal is absent from that line. No legend is echoed back — you passed the array |
| **Files** | a search that keeps hitting a folder you do not want | `search_text(query, exclude: ".research/**")` | dropped after `glob=` has selected, so one call answers what two used to |
| **Files** | `Grep -C3` / a search then a read | `search_text(query, context: 3)` | the surrounding lines arrive on the hit's own record, indented — no follow-up `read_text` |
| **Files** | a text hit, then "which declaration is that line in?" | `search_text(query, containers: true)` / `search_regex(query, containers: true)` | names the C# declaration each hit sits in — `Type.Member`, from syntax — between the position and the matched line, so the record is an id `get_symbol_source` takes and no outline plus ranged read follows. Only a `.cs` file carries one; refused beside `countOnly:` |
| **Files** | `grep -w` / a short literal that drags in every longer identifier | `search_text(query, word: true)` | keeps a literal only where the characters either side are neither a letter, a digit nor `_`; `search_regex` answers it with `\b` and does not declare it |
| **Edits** | change a declaration's **attributes** — a tool `[Description]`, an `[Obsolete]` — without re-sending it | `edit_text(path, force: true, oldText: "<short unique fragment>")` | the sanctioned attribute edit: an anchor costs ~30 tokens where `replace_symbol` re-sends the whole declaration (~1 175 tokens for one `[Description]`). **Not compile-gated** - `edit_text` writes straight through, so `analyze` the file after |
| **Files** | `grep -o` | `search_regex(query, matchesOnly: true)` | prints the matched span instead of the whole line; compose with `unique: true` for "which distinct values of this shape exist". **`search_text` refuses it** — a literal's matched span is the literal you passed |
| **Files** | `grep -c` / "is X in these files at all?" | `search_text(query, countOnly: true)` | ONE line per file — path and match count, plus `q1=N` per `queries=` entry — and no matched text. Refused beside `matchesOnly=`, `unique=`, `context=` |
| **Files** | `grep -r` in a log folder outside the repo | `search_text(query, root: "C:/logs")` | an absolute directory outside every workspace, tagged `outside-workspace` |
| **Files** | `sort \| uniq -c` over repeated log lines | `search_text(query, unique: true)` | identical matching lines collapse to the first record plus `x<count>` |
| **Files** | `Bash: git show <ref>:<path>` | `read_text(path, ref: "main")` · `get_file_outline(path, ref: "main")` | the file as it was at that ref, with the same gutter, line ranges, `tail=`, `section=` and `maxChars` as the working tree, and a whole `.cs` answering its outline; one path, and a ref that does not exist is refused rather than answered from the working tree |
| **Files** | `Read` a non-`.cs` file | `read_text(path)` | line ranges, bounded response; a line number is printed only where the numbering jumps, so a contiguous read carries one — `verbose: true` numbers every line; a clipped read ends with `next: startLine=…` |
| **Files** | `Read` **several** files | `read_text(paths: [...])` | up to 10 in one response, each under its own path line with its own count and `next:` note; an unresolved path is `NOT_FOUND` inline, and `maxChars` is one budget shared across the batch that names the entry it clipped |
| **Files** | `tail -n 200 log.txt` | `read_text(path, tail: 200)` | the last N lines, so the end of a huge log is addressable |
| **Files** | `wc -c file` for a size you want *while* reading | `read_text(path, bytes: true)` | ends the answer with `bytes=N`, on every shape it returns and once per `paths=` entry |
| **Files** | guessing what a budgeted document costs before its test runs | `read_text(path, tokens: true)` | ends the answer with `tokens=N` for the **whole** file whatever range was read - the count the shipped-doc budgets assert - on every shape it returns and once per `paths=` entry |
| **Files** | a file whose lines are enormous | `read_text(path, maxChars: 20000)` | `maxLines` cannot bound those; the clip still names the line to continue from, and says `line N was cut mid-way` when the budget ran out **inside** a line — raise `maxChars` for that line, because a line range cannot resume at a character offset |
| **Files** | `Read` a whole `.md` to find a section | `read_text(path, headings: true)` then `read_text(path, section: "## Commands")` | the heading map with line ranges and each heading's GitHub anchor slug, then only that section. `maxLines=` bounds the map and `maxLevel: 2` drops every `###`, so a 179-section changelog answers its shape in a dozen lines; anchors stay the ones GitHub assigns over the whole document, and `maxLevel=` without `headings=true` is refused |
| **Files** | reading a whole `.md` whose content is one long table | `read_text(path, columns: "Finding,Tool")` | one line per table row, those columns only - what `headings=true` cannot give a file with nothing to narrow by. `section=` scopes it to that section's tables and is named in a refusal, `maxLines=` and `maxChars=` bound the rows - a projection that runs out of its CHARACTER budget truncates and ends with `next: search_regex matchesOnly=true unique=true`, the one bounded call that answers the same question, instead of spilling a whole large table; a column no table under the read declares is refused naming the real ones even when the others matched, and `headings=`/`startLine=`/`endLine=`/`tail=` beside it are refused rather than silently winning |
| **Files** | `Bash: git checkout -- <path>` after a bad write | `write_text(path, ref: "HEAD")` | restores the file from that ref through the same compile gate as any write - the way back for a `.csproj` or `.md` that `undo_last_change` cannot cover. A write of markup carrying `&lt;` and no raw `<` warns and names this call, because HTML-escaped markup is not markup |
| **Files** | a scratch `.cs` probe outside every workspace root | `write_text(path, content, force: true)` | an absolute path under no loaded root is written with `force=true`, tagged `outside-workspace` and never compile-gated, because no project of this workspace compiles it - the read half already worked |
| **Files** | `Bash: rm file` · `Bash: rmdir` | `write_text(path, delete: true)` | containment-checked; a `.cs` document goes through the compile gate and is covered by `undo_last_change`. The same call on a DIRECTORY removes it when it is **empty**, and refuses a non-empty one naming what it still holds |
| **Edit text** | `Edit` a `.md` section | `edit_text(path, section: "## Commands", newText: …)` | no `oldText`, so no read-then-match round trip |
| **Edit text** | reading a section out of one file and writing it into another | `edit_text(path, section: "## Open", toPath: "other.md")` | cuts the section and lands it in the other file as **one** write, answered as one changed-line count per file - the whole section text never crosses the wire. `place=prepend` puts it at the top of the target, anything else appends; `occurrence=` picks the source section; both paths must be markdown, **both must already exist**, and naming the same file twice is refused |
| **Edit text** | anchoring on `### Added` to add a changelog entry | `edit_text(path, section: "### Added", occurrence: 1, place: "prepend", newText: …)` | writes **inside** the section — `prepend` under its heading, `append` after its last non-blank line. A heading that repeats needs `occurrence=`: the refusal names `occurrence=1..N` and each candidate's start line, so the index is picked with no re-read. `read_text` takes it too, and refuses it without a `section=`. Only with `section=`; supply your own blank lines |
| **Edit text** | one `edit_text row=` call per row when closing a whole backlog | `edit_text(path, rows: [{row, newText}, ...], toPath: "IMPROVEMENTS-ARCHIVE.md")` | up to 25 rows cut and landed in order as ONE write per file; an identifier matching nothing or several refuses the batch, so a partial move cannot happen |
| **Edit text** | closing a backlog row: cutting one table row out of one markdown file and appending it to another | `edit_text(path, row: "I286", toPath: "IMPROVEMENTS-ARCHIVE.md", newText: "\| … \|")` | the row is matched by its **first cell**, so its old text never crosses the wire; `newText=` is what lands in the target - omit it to move the row verbatim. An identifier matching no row is refused saying so; one matching several names each candidate's LINE NUMBER and, when one exists, the longer identifier that resolves, so the retry needs no re-read; and `row=` without `toPath=` is refused rather than dropped |
| **Edit text** | three or more `edit_text` calls on the **same** file | `edit_text(path, edits: [{oldText, newText}, …])` | applied in order as one write, at most 10; an entry whose anchor fails is reported with its own code and remedy and the others still land, so one bad anchor never costs the batch. A **partly** refused batch leads with what changed and lists each refusal as `REFUSED <path>: <code> - <message>; remedy: …`, so only a leading `ERROR` means re-send; a malformed entry is named — `edits[1] is the entry that failed to bind` |
| **Edit text** | one `edit_text` call per file across **several** files | `edit_text(edits: [{oldText, newText, path}, …])` | an entry may name its own `path`, and the top-level `path` may then be omitted entirely; entries are grouped by file, applied as one write each, and answered one line per changed file. A path-less entry with no top-level `path` is refused by index. At most 10 per file and 25 in total |
| **Edit text** | one `write_text` call per new file | `write_text(files: [{path, content}, …])` | up to 10 in one call, and every `.cs` document among them shares **one** compile gate — so a type and the consumer it breaks land together instead of the first write being rolled back alone |
| **Edit text** | an anchor that deliberately repeats — a table of near-identical rows | `edit_text(path, oldText: "\| row \|", occurrence: 3)` | picks the Nth match instead of forcing you to lengthen the anchor; a multi-match refusal lists the candidate lines with their numbers, so `occurrence=` is picked from the refusal and needs no re-read, and an out-of-range value names the range it could have picked |
| **Edit text** | `Edit`/`Write` a non-`.cs` file | `edit_text` · `write_text` | line endings normalized before matching; an ambiguous match is refused and a miss names the file's closest lines |
| **Edit text** | re-reading a file because an anchor copied from `get_symbol_source` did not match | `edit_text` already handles it | that payload is **dedented**, and it still matches: the anchor is compared line by line allowing one uniform whitespace prefix, `newText` is re-indented by it, and a `NOTE` says so. A multi-line anchor matching nothing gets the closest REGION and its range, so the retry is a corrected anchor, not a re-read |
| **Edit text** | `Write` a **new** `.cs` file | `write_text(path, content, force: true)` | no symbol tool creates a file; the write is compile-gated whenever a project globs it, the new type is resolvable on the very next call, and two interdependent new files land in either order |
| **Edit text** | rewriting a whole `.cs` file | `write_text(path, content, force: true)` | compile-gated like `replace_symbol` when the file is already a document: rolled back on a new error unless `allowErrors: true` |
| **Edit code** | `Edit` a `.cs` file | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` | addressed by symbol, immune to line drift, compile-gated; `add_member` and `replace_symbol` take several declarations in one edit |
| **Edit code** | a new body that calls a private helper you have not written yet | `replace_symbol(symbolId, declaration, add: [...])` | the new members land in the **containing type** inside the same compile-gated edit, so the callee-after-caller `CompileRegression` never happens; targets must share one containing type, and an enum container is refused, never walked past |
| **Edit code** | a signature change that breaks its callers | `replace_symbol(symbolIds: [...], declarations: [...])` | one declaration per symbol, paired positionally, applied as **one** compile-gated edit across every file they live in — the way to land a signature change together with the callers it breaks instead of paying a `CompileRegression` and a retry |
| **Edit code** | renaming a member and rewriting its body in one edit | `replace_symbol(symbolIds: [...], declarations: [...], rename: true)` | accepts a declaration whose **name** differs from the symbol it is paired with instead of refusing the batch; references are not rewritten, so the gate rolls it back when a caller breaks - `rename_symbol` is what makes them follow; every rename it applies is reported as `NOTE renamed: Add -> Append` |
| **Edit code** | adding an **enum member** | `add_member(typeSymbolId: "T:…MyEnum", declaration: "Retry")` | an enum id takes enum members; `replace_symbol` and `delete_symbol` work on one too |
| **Edit code** | adding a **sibling type** to an existing file | `add_member(path: "Foo.cs", declaration: "public sealed record Bar(int X);")` | appended to that file's namespace as one compile-gated edit — no whole-file rewrite, no forced text edit |
| **Edit code** | find-and-replace a name | `rename_symbol(symbolId, newName)` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| **Edit code** | reverting an edit you regret | `undo_last_change` | up to ten solution snapshots per workspace; a snapshot dropped by an external change is reported rather than overwritten |
| **Refactor** | hand-writing an interface from a class | `extract_interface(symbolId)` | the members you name, with their doc comments, plus the `: IFoo` on the type |
| **Refactor** | cut-and-paste between files | `move_type_to_file` · `move_type_to_namespace` | the type, its usings and every reference, as one compile-gated edit |
| **Refactor** | editing a signature and every call site by hand | `change_signature(symbolId, …)` | reorders, adds and removes parameters and updates the callers |
| **Projects** | editing a `.csproj` by hand | `project_set_property` · `project_properties` · `project_add_reference` · `project_remove_reference` · `project_create` | CPM-aware, containment-checked |
| **Projects** | editing `PackageReference` by hand | `package_list` · `package_add` · `package_remove` | central package management aware: the version lands in `Directory.Packages.props` |
| **Projects** | `Bash: dotnet list package --vulnerable` | `package_list(vulnerable: true)` · `package_list(outdated: true)` | the resolved graph, including transitive packages - the question the project file cannot answer; needs a restore |
| **Projects** | "which properties does this project really have?" | `project_properties(project)` | MSBuild's **evaluated** properties with the file that set each, so a `Directory.Build.props` value is answered instead of `0 properties` |
| **Projects** | editing a `.sln`/`.slnx` by hand | `solution_add_project` · `solution_remove_project` | the solution file only, no MSBuild evaluation |
| **Projects** | "which projects does this solution contain?" for a solution that is **not** loaded | `solution_projects(path: …)` | reads the `.slnx`, `.sln` or `.slnf` directly and loads nothing, so a fixture-scoped question does not cost a `load_workspace` that makes every later un-hinted call ambiguous |
| **Git** | `Bash: git log` / `git show --stat` | `history` | commits touching a path, one line each - short sha, date, author, subject - `baseRef=` for a ref or a range, `contains=` for git's pickaxe (only the commits whose diff added or removed that literal), `message=` for the subject grep, and `commit=<sha>` for one commit's per-file stat. `git blame` stays on the shell: it ran **once** in 683 sessions |
| **Git** | `Bash: git describe` | `history(describe: true)` | HEAD's position in one line - `tag=`, `ahead=`, `sha=`, `dirty=` - which is the MinVer question a release asks; creating or verifying a tag stays on the shell |
| **Git** | `Bash: git tag --list` / `git tag -l "v*"` | `history(tags: true)` | every tag newest version first, one line each - name, the short sha it names, its date - bounded by `maxResults`; refused beside `baseRef=`, `path=`, `contains=`, `message=` or `commit=` rather than ignoring them, and creating, annotating or deleting a tag stays on the shell |
| **Git** | `Bash: git diff --cached` for its hunk text or its declarations | `diff_symbols(staged: true)` · `diff_text(staged: true)` | the index against `HEAD`, which is what a pre-commit review asks; without it a bare `diff` compares the working tree against the INDEX, so a fully staged change set answers nothing |
| **Git** | `Bash: git diff --cached --name-only` / `git status --untracked-files=no` | `changed_files(staged: true)` · `changed_files(untracked: false)` | `staged=true` reads the INDEX against `HEAD` - or against `baseRef=` - which is what a pre-commit check asks; `untracked=false` answers tracked changes only |
| **Git** | `Bash: git status` / `git diff --stat` | `changed_files` | one line per file - path, `+added -deleted`, status letter; untracked files included, `path=` scopes it to one pathspec on a shared tree, and `exclude=` drops what a pathspec cannot leave out - `exclude: ".research/**"` for another session's notes; an excluded file is not counted |
| **Git** | `Bash: git diff` to decide what to review | `diff_symbols` | every hunk mapped onto the declaration containing it, answered as symbol ids you feed straight to `get_symbol_source` - `EXACT` inside one declaration, `HEURISTIC` with the raw line range otherwise, and it ends by naming the exact `diff_text path=…` call for the hunks it could not map |
| **Git** | `Bash: git diff` for the hunk text itself | `diff_text(path: …)` | the raw unified diff: whitespace, a non-`.cs` file, a pure deletion, and whatever `diff_symbols` mapped only `HEURISTIC`. It costs about a response line per changed line, so bound it - `path=` scopes it, `paths=[...]` takes up to 10 pathspecs in the same git invocation, `maxLines=` caps it at 1000 and a truncated answer names the exact `maxLines=` that returns the rest |
| **Build and test** | `Bash: dotnet build` / `msbuild` | `build` | deduplicated diagnostics, no MSBuild spew; a successful build is one line whatever it warned about, a failed one lists errors only |
| **Build and test** | `Bash: dotnet build -c Release` | `build(configuration: "Release")` | `configuration` and `targetFramework` map to `-c` and `-f` on `build`, `run_tests`, `rerun_failed` and `list_tests` |
| **Build and test** | `Bash: dotnet build -p:Name=Value` | `build(properties: ["Name=Value"])` | `properties` maps to one `-p:` per entry on the same four tools, applied after `-c` and `-f`; an entry that is not `Name=Value` is refused before anything runs |
| **Build and test** | `Bash: dotnet test` / `vstest` | `run_tests` | a green run is one line, and a run that spanned several projects appends `Name:total/durationMs` per project so "which tier is slow" costs no second run; a failure carries its message, expected/actual and one source frame. A solution is built once, then each test assembly runs directly where its runner allows, skipping the MSBuild and VSTest host `dotnet test` pays per project |
| **Build and test** | one `run_tests` call per test project | `run_tests(projects: [...])` | at most 10, run **concurrently**; the timeout applies to **each** project, and one that timed out is named instead of the merged run being reported as passed. Naming the same project twice is refused - two invocations of one assembly race each other and fail tests that pass alone |
| **Build and test** | bounding parallelism **inside** one test assembly | `run_tests(runSettings: ["xUnit.MaxParallelThreads=1"])` | VSTest RunSettings overrides, passed through as one trailing `-- Name=Value` block - the layer `parallel` deliberately does not touch. `xUnit.StopOnFail`, `MSTest.Parallelize.Workers` and `NUnit.NumberOfTestWorkers` live here too; an entry that is not `Name=Value` is refused before anything runs |
| **Build and test** | re-running what broke | `rerun_failed` | replays the previous failures only |
| **Build and test** | `dotnet test --list-tests` | `list_tests(contains)` | names without running |
| **Build and test** | `Bash: dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's file locks; `path=` sweeps a `.slnx`/`.sln`/`.slnf`/project that is **not** loaded |
| **Analyse** | one `analyze` call per touched file | `analyze(paths: [...])` | up to 10 files, directories or globs in one pass, so the end-of-task per-file sweep is one call; an entry carrying a comma or a brace is refused by name rather than mis-scoped. A batch that **saturates** the 10-path cap ends with `next: analyze changed=true`, which answers the same end-of-task sweep over every modified file in ONE call - take it rather than sending a second batch |
| **Analyse** | `dotnet format whitespace` / an IDE inspection | `analyze` | compiler + every referenced analyzer + dead code, down to `info` |
| **Analyse** | running `analyze` → `format` → `cleanup` → `analyze` at the end of a task | `gate` | the same four calls in the mandated order, answering one verdict line - `clean  analyzed=N fixed=M remaining=0`, where `analyzed` counts the **documents** in scope - and keeping only the diagnostics still unfixed, each carrying the declaration it sits in exactly as `analyze` does |
| **Analyse** | `dotnet format style` / `dotnet format analyzers` | `cleanup fix=style\|analyzers\|all` | applies the referenced analyzers' code fixes, compile-gated, `UNFIXED <id>` for what no fixer covers |
| **Analyse** | `dotnet format --verify-no-changes` | `format verify=true` · `cleanup verify=true` | one verdict line (`clean` or `VERIFY_FAILED n`), no diff; each named file carries the step that would change it - `whitespace`, `fixers` or `fixers+whitespace` - and a mode that also reformats names the byte-equivalent CI pair, so the verdict says whether CI would really be red |
| **Analyse** | formatting only what you touched | `format changed=true` · `cleanup changed=true` | files modified since the workspace loaded, so a sweep stops rewriting files the task never opened; the change set survives the unload-and-reload a locked `build` performs |
| **Analyse** | reading build output for a consumer you broke | `get_diagnostics` | the solution-wide warning and error sweep a per-file pass cannot see |
| **XAML** | `Read` a `.xaml` file | `xaml_outline(path)` | element tree with `x:Name`/`x:Key`, no attributes |
| **XAML** | `Grep` a `.xaml` file | `xaml_find(query)` · `xaml_names()` · `xaml_resources()` | by element, attribute or content; `x:Name` declarations; every `x:Key` with its dictionary |
| **XAML** | hunting a resource through `App.xaml` | `xaml_resolve(key)` | every declaration with its scope, one call; a key with no keyed declaration lists the implicit styles targeting it, `HEURISTIC`, and names no winner |
| **XAML** | "why does this control look like that" | `xaml_styles(typeName)` | implicit and keyed styles with the `BasedOn` chain, capped by `maxResults` (100) |
| **XAML** | eyeballing a `{Binding}` | `xaml_bindings(path, validate: true)` | each path type-checked through Roslyn |
| **XAML** | `Read` a `.xaml.cs` to see what the markup wires | `xaml_codebehind(path)` | `x:Class` plus every handler |
| **XAML** | "is this element translated" | `xaml_localization()` | every `x:Uid` joined to its `.resx`/`.resw` entry |
| **XAML** | guessing whether the markup is sound | `xaml_validate()` | duplicate `x:Key`/`x:Name`, and resources that resolve to no declaration anywhere under the root |
| **XAML** | `Edit` a `.xaml` file | `xaml_set_property` · `xaml_add_element` · `xaml_remove_element` | addressed by element, formatting preserved, an unparseable result refused |
| **Localization** | `Read` a `.resx`/`.resw` | `resx_get(path, cultures)` | every key with its value per culture; absent ones print `MISSING`, `values=false` lists keys only |
| **Localization** | `Glob` for resource files | `resx_files()` | every family with its cultures, counts, missing total and designer |
| **Localization** | `Grep` a resource key | `resx_find(query)` | key, value or comment, across every family |
| **Localization** | "is this key still used" | `resx_usages(key)` | designer property through Roslyn, plus `GetString`, localizer, `x:Uid`, Razor, with `composedLookups=` so an empty answer is never claimed as proof |
| **Localization** | "which strings are untranslated" | `resx_validate()` | `RESX001` missing · `RESX002` placeholder mismatch · `RESX003` unused (`includeUnused` only) · `RESX004` duplicate · `RESX005` orphan · `RESX006` empty · `RESX007` trimmed whitespace · `RESX008` unsorted · `RESX009` stale designer |
| **Localization** | one `resx_set` call per key | `resx_set(entries: "Key=Value\nOther=Second")` | every key in one pass; a line with no separator is named and refuses the batch rather than being dropped |
| **Localization** | `Edit` a `.resx`/`.resw` | `resx_set` · `resx_remove` · `resx_rename` | one `<data>` element rewritten; header, order, indentation, line endings and BOM kept, and `resx_set` creates a missing culture file from the neutral header |
| **Razor** | `Read` a `.razor` or `.cshtml` file | `razor_outline(path)` | directives, component tree and `@code` members, each component resolved to its type |
| **Razor** | "how do I use this component" | `razor_component(name)` | every `[Parameter]`, which are `[EditorRequired]`, from source **or** a referenced package |
| **Razor** | `Grep` a tag, directive or route in markup | `razor_find(query, kind)` | component, element, attribute, directive, expression or route |
| **Razor** | "is this `@bind` real" | `razor_bindings(path, validate: true)` | each `@bind`/`@on`/`@ref`/`asp-for` resolved against the component type |
| **Razor** | `Read` a `.razor.cs` | `razor_codebehind(path)` | the partial class behind the component and the members it declares |
| **Razor** | "what breaks at render" | `razor_validate()` | unknown parameter, duplicate route, unregistered `@inject` — none of which the compiler reports |
| **Razor** | `Edit` a `.razor` file | `razor_set_attribute` · `razor_add_element` · `razor_remove_element` · `razor_set_directive` | element-addressed, formatting preserved, compile-gated through the Razor generator |

## 🚫 HARD GATE — take the tool from the table; the built-ins are the last resort

**Take the tool the table above names, on every call.** That is the whole rule, and it holds for
`.cs`, `.razor`, `.cshtml`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`/`.slnf`, `.xaml`,
`.axaml`, `.paml`, `.resx` and `.resw`, and for every question about C# symbols, references,
diagnostics, builds, tests or the working tree.

**So a `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call on one of those is
forbidden.** Not "discouraged" — forbidden. There is a TerseSharp tool for it in the table above.

**And issue independent calls in ONE message.** Several `tool_use` blocks in one message run
concurrently; one call per message pays a **6 097 ms (p50)** model gap before its tool even starts, and
**36 070 of 36 071** tool-bearing messages in a measured week issued exactly ONE call. Before every
message carrying a call: *is there another call I already need whose arguments do not depend on this
one's result?* If yes, send them together — but never guess an argument to make a call parallel. Inside
one tool the same lever is `paths=`, `symbolIds=`, `queries=`, `edits=`, `files=`, `projects=`.

**The shell does not launder it.** `grep`, `rg`, `find`, `fd`, `cat`, `head`, `tail`, `sed`, `awk`,
`ls`, `dir`, `tree`, `wc`, `nl`, `findstr`,
`type`, `dotnet build`, `dotnet test`, `dotnet watch build`, `dotnet watch test`, `dotnet msbuild` and
`msbuild` run through `Bash` are built-ins
too and are covered by the same gate — including later in a compound command
(`cd src && dotnet test`).

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
check, so never shell out for them. `dotnet list package` routes to `package_list`
(`vulnerable=true`, `outdated=true`, same restored graph). `dotnet restore`, `pack`,
`publish`, `run` and `tool` are **not** covered: nothing here replaces them.

**A bare `sleep` is denied too, and nothing replaces it.** A segment whose COMMAND WORD is `sleep`,
outside a `while`/`until`/`for` loop, is refused: 156 such calls burned **7.0 h** of wall clock in one
measured week. `docker run … sleep 3600` and `python sleep.py` are untouched. Background work
re-invokes you when it finishes, and when you need its result and have nothing else to do, **end the
turn** — stopping is free, sleeping is billed. The one allowed shape is the pause inside a loop that
also detects the process dying: `while :; do kill -0 "$PID" || break; sleep 1; done`.

**One replaced command no longer kills a batch.** The guard strips those commands, rewrites the
rest and lets them RUN, naming what it removed — call the tools for those, do NOT re-run the batch. It
rewrites only sound shapes: uniform `&&`/`;`/newline separators, a whole pipeline at a time. `||`, a background `&`, a subshell, a redirect, a substitution, a comment, a backslash escape, a mixed `;`/`&&` run or a shell keyword is **denied
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
`diff_text` — **all three take `staged=true`**, so a `--cached` diff asked for its declarations or its
hunk text is answered rather than handed the counts tool, and all three take
`baseRef=`, so `main`, `HEAD~3` and a range work, and the paths come back workspace-relative and
re-usable as arguments. A bare `git ls-files` is served by `find_files tracked=true`. Running them in
`Bash` is the same breach as `grep` — but only for the tree TerseSharp serves: the guard reads the
directory the command actually addresses (`-C` target, then a directory operand, then the working
directory), so `git -C ../some-other-repo status` is allowed, because no tool here answers it. Git **history** is served too now: `git log` and `git show --stat` are `history`, and
`git show <ref>:<path>` is `read_text ref=` / `get_file_outline ref=`, and a `git tag` **listing** —
bare, or any flag-only form such as `--list`, `-l` or `--sort=` — is `history tags=true`. Still on the shell: `git blame`
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
`xaml_*` tools, no `.razor`/`.cshtml` the 10 `razor_*`, no `.resx`/`.resw` the 8 `resx_*`: 57 tools
instead of 88 on a plain C# solution, because the full catalogue costs tokens on every request and
measurably lowers selection accuracy. Loading a second solution that does hold them re-advertises
those families through `notifications/tools/list_changed`; `--tools all` (or `TERSE_TOOLS=all`)
advertises everything regardless and `--tools core` narrows to about twenty. A hidden tool still
answers when called by name — but an agent can only call what its client lists, so treat a narrowed
surface as narrowing what you can reach, not merely what you can see. `workspace_status` prints
`tools=core - N advertised` under a profile and `tools=<families> hidden` when the workspace narrowed
it.
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

**`failures=` and `warnings=` are different things.** `failures=` counts projects that did not load;
`warnings=` counts MSBuild diagnostics that did not stop a load — NuGet advisories (NU1903), target
framework notes (NU1701) and the like. A big solution routinely reports `failures=0 warnings=20` and
is fully usable. **Neither is listed by default**: the warnings are a count, and the failures are
folded to one `FAILED <project>  messages=N` line per project under a `N load failure(s) in M
project(s)` header. `verbose=true` prints every message of both. So do not read a warning count as a
broken workspace, and do not fall back to the built-ins over one.

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
time**, and **48% of its calls were byte-identical repeats inside one session** (`build`: 75%). Per
edit, climb only as high as the edit reaches:

| Rung | Call | Measured mean | When |
|---|---|---|---|
| 1 | `analyze` on the touched file, down to `info` | **7.0 s** | after EVERY edit |
| 2 | `build` scoped to the project | **13.0 s** | when the edit crosses a signature or a consumer |
| 3 | `run_tests` scoped to the affected project | **85 s** | once the slice compiles |
| 4 | `run_tests` over the whole solution | 85 s+, p99 **16 min** | ONCE, at the end of the task |
| — | `rerun_failed` | 20 s | after a red run — never re-run a whole suite to watch the same test fail twice |

A tier is never dropped; only how often it is re-run. Banned: a full-suite run between two edits of one
slice · re-issuing `build`/`run_tests` with identical arguments when nothing was written in between · a
run to "confirm" one that already passed · reading a test result before the build result.

**Analyse — at the end of a task, call `gate` and stop there.** It runs `analyze` at `info`,
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

**`format verify` and `cleanup verify` are not the same gate.** `format` compares against the Roslyn
whitespace formatter, which `dotnet format style` and `dotnet format analyzers` do not run — a
`VERIFY_FAILED` there can still be a green CI leg. `cleanup verify=true fix=style` and
`fix=analyzers` are exactly those two CI commands — they apply code fixes only and never reformat —
while `fix=all` and the default `fix=usings` do reformat, so those two are supersets that may name
files CI accepts. **You no longer have to work that out**: every file a verify names carries the step
that would change it — `whitespace`, `fixers` or `fixers+whitespace` — and a mode that also reformats
names the byte-equivalent CI pair. Every file `whitespace` is a green CI leg; any `fixers` is a red one.

**A missing path is answered, not just refused.** `get_file_outline` and `read_text` on a path named
after a type the workspace declares elsewhere name the file that declares it, and `add_member path=`
on a `.cs` file nobody has written yet names `write_text path=… force=true` — neither sends you to
`find_files`, which cannot find a type that does not name its file.

**`replace_symbol` replaces the whole declaration, attributes included, and says when yours dropped
them** — `WARNING attributes dropped: McpServerTool, Description`. The edit still applies, because
dropping an attribute is sometimes the intent, but an un-advertised tool is exactly what a clean
build, `analyze` and `get_diagnostics` cannot show you. Copy the attributes in, or use
`replace_symbol_body`.

**`add_member` refuses a duplicate member from syntax, before anything is compiled.** A declaration
whose name and parameter list the type already declares answers `ERROR NameTaken` naming that member
and its line - it used to cost a full compile round trip and the whole rejected declaration. An
overload whose parameter list differs still lands.

**A mutation names the warnings it introduced** as `WARNING introduced  <diagnostic>`, up to five and
saying `5 of 12 shown` when there are more, so learning *which* three no longer costs an `analyze`. **`replace_symbol add=` takes `addTo=`** when the targets do not share one containing
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

**From the second consecutive call of one tool the response gains one line** —
`2 read_text calls in a row - pass paths=[...] with the next 2+ in ONE call` — naming the plural
parameter that tool declares. It is framing, never payload, it says nothing when the call already
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
every file - and `changed_files(path: "src")` is the difference between reading your own change set and reading
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
`.git`, `.claude`, `.vs`, `.idea`, `artifacts`, `TestResults`, `node_modules` and directory symlinks —
the same set every index uses, so a nested agent worktree never doubles a result.

**A `.cs` file returned verbatim ends with `symbolIds=[...]`** when the read covered the whole file and
it has at most ten members, so the *next* read is member-scoped. A line-ranged read gets nothing.

**`read_text` on a `.cs` path asked for whole answers the outline, not the text** — no `startLine`,
`endLine`, `tail`, `section` or `verbose`. Whole-file `.cs` reads were 71 % of everything this tool
has ever returned and an outline is a third of the tokens. A `.cs` file that is not a document of this
workspace is read as text unchanged. `read_text` also accepts an **absolute path outside every
workspace root**, tagged `outside-workspace`, so comparing a file against another repo needs no second
`load_workspace` and no `workspace=` even with several loaded; every writer still refuses to leave the
workspace. It clips at **40 960** characters unless `maxChars` says otherwise (ceiling 131 072): the
default is set so a whole-file read stays inline in your client rather than being spilled to a file
that answers nothing, and the clip always names `next: startLine=`.

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
   `errors=N (+D) warnings=N (+D)` for the changed projects and their dependents — you do not need a
   separate `analyze` afterwards. A `dryRun` that *would* be rolled back says
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

14. **Independent calls go in one message.** If you intend to call several tools and there are no
    dependencies between them, make all of the independent calls in parallel, in a single assistant
    message, rather than one after another. `changed_files` and `workspace_status` have nothing to do
    with each other and belong in the same message. Prioritize calling tools simultaneously whenever
    the actions can be done in parallel.
    **But when a call needs a value a previous call returns — a symbol id from an outline, a path
    from `changed_files`, a `retryWith` token from a rollback — call them sequentially, and never
    guess a parameter to make a call parallel.** A measured week of this server's own sessions
    carried 17 567 tool calls and **not one** parallel message, while 5 989 of them sat in runs of
    three or more consecutive calls of the same tool; at this server's median call latency that is
    hours of wall clock nothing depended on.
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

To narrow it deliberately, `write_text` a `.terse.json` at the repo root - it is found by walking up
from the directory the server runs in, and never above the repository root:

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
## Code policy - when an edit is refused for style, not for compiling

A project can make this server **reject an edit that violates its standards**, through a `policy`
section in the same `.terse.json`. It is **off unless that section exists**. When on, an edit answers
`ERROR PolicyViolation` naming each rule, the declaration, measured against allowed, and a `fix:` line.

**Only what the edit INTRODUCES counts** - a violation already in the file does not block you, so never
"fix" unrelated members to get an edit through. A finding is keyed by rule, path and declaration, not
by its measured value, so neither improving nor worsening an already-violating member registers.

Twelve rules, `TERSE100`-`TERSE111`: cognitive complexity, method statements, methods per type,
constructor dependencies, parameter count, method-name length, meaningless type suffixes, naming per
declaration kind, `async void`, condition operands, chained references (off by default), nesting depth.
Each is `reject`, `warn` or `off`; a `warn` rule lets the edit land and answers `WARNING policy  ...`.
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
batch rather than vanishing from it. `resx_remove` covers every file of
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
change is nearly free. A run that spanned **more than one project** appends `concurrency=<summed/wall>x` plus
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
| a raw VSTest expression | `run_tests(filter)` |
| only the test projects your change can reach | `run_tests(changed: true)` — the test projects that transitively reference a project you changed since the workspace loaded, at **assembly** granularity, naming both what it ran and what it skipped. Falls back to one whole-solution run, saying why, whenever it cannot reason (nothing changed, a changed file belongs to no project, no test project depends on it) or the change reaches more than 10 test projects. It never silently runs less than it should. Ignored when `project=` is passed |
| several projects at once | `run_tests(projects: [...], parallel: N)` — concurrent; `1` is serial |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` |
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
`clean` runs, the response says so (`WARNING a locked output file blocked the operation`) and, with one
workspace loaded, the server unloads it, retries and reloads, reporting which happened in a `NOTE` — so
no `unload_workspace` by hand. When the output is **still** locked it lists every process the build
named, one
`holder pid=… <name> startedUtc=… exe=…` line each — the executable workspace-relative when it lives
under the root, which tells a test host running out of *this* tree's `bin/` from another session's —
classified as this terse server, an MSBuild or BuildHost (including one an earlier terse load spawned
out of this tree's `bin/`), a live `testhost` to wait for rather than stop, a bare `dotnet` host, or a
pid already gone. **A bare `dotnet` holder is classified against the process table**: when a test host
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
the name matched too many symbols to resolve safely — and it is now reached only by a **bare** name:
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
