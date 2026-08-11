---
name: terse-sharp
description: Use when reading, searching, navigating, editing, refactoring, building or testing C#/.NET, XAML, .resx localization or Razor/Blazor in a solution served by the TerseSharp MCP server. Teaches which TerseSharp tool replaces which built-in, and how to drive all 87 of them, so a .cs file is never read whole, a symbol is never found by text search, and a .xaml, .resx or .razor file is never edited by line number.
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

## Replace the built-in on the left

| Instead of | Use | Why |
|---|---|---|
| `Read` a `.cs` file | `get_file_outline(path)` | every type and member with signatures and line ranges, no bodies; `usings: true` adds the file's own using directives, `parameterNames: false` prints parameter types without their names for about an eighth fewer tokens |
| `read_text` a whole `.cs` file | it already answers the outline | a `.cs` path with no `startLine`, `endLine`, `tail`, `section` or `verbose` returns `get_file_outline`'s answer plus a steer, because the text is ~3x the tokens; pass `verbose: true` or a line range for the text |
| `Read` a whole class's source | `get_symbol_source(symbolId)` on a **type** id | answers `get_type_outline`'s member list plus a steer to one member, not the whole file's text; `verbose: true` opts back into the source |
| `Read` to see one method | `get_symbol_source(symbolId)` | that member only, dedented; `verbose: true` for it verbatim, `comments: false` to drop doc and inline comments when you are orienting rather than editing |
| `Read` to see **several** methods | `get_symbol_source(symbolIds: [...])` | all of them in one response; an id that does not resolve is reported `NOT_RESOLVED <id>`, never a failed call |
| `Read` to learn a class's API | `get_type_outline(symbolId)` | member list, no bodies; `parameterNames: false` there too |
| a name an outline printed that answers `SaturatedName` or `AmbiguousSymbol` | `get_symbol_source(symbolId, path: "src/Trading/OrderService.cs")` | `path=` resolves the name inside that file first and only falls back to the solution when the file holds no match — `get_symbol` and `get_type_outline` take it too, `symbolIds=` scopes every id in the batch, and a `path=` naming no document answers `DocumentNotFound` instead of being ignored |
| `Grep` for a type or member name | `search_symbols(query)` | declarations only; CamelHump (`OSvc` finds `OrderService`) |
| a name the tests declare dozens of times | `search_symbols(query, scope: "src")` | keeps one half of the solution - `src` for the production projects, `test` for the ones referencing a test framework; an unknown value is refused rather than searching everything |
| `Grep` to find callers | `find_usages(symbolId)` | real references, one line per file, each marked `src` or `test` |
| `Grep` for implementers | `find_implementations(symbolId)` | resolved through the interface |
| `Glob` / `ls` | `find_files(glob)` | `bin`, `obj`, `.git`, `.claude`, `.vs`, `.idea`, `artifacts`, `TestResults`, `node_modules` and directory symlinks excluded |
| `ls -l` / `Get-Item` for a size or a timestamp | `find_files(glob, stamps: true)` | each record gains the file's UTC last-write time and byte length, so "when was this written, and how big is it?" needs no shell |
| `Bash: git ls-files` to tell a checked-in file from a scratch one | `find_files(glob, tracked: true)` | only the files git tracks, so build output and another session's untracked notes drop out; the bare `git ls-files` is denied by the guard, every flagged form is not |
| `Grep` in non-code files | `search_text(query)` / `search_regex(query)` | tagged `HEURISTIC` once for the whole response, not per record - these two answer nothing else; the count line counts matching **lines**, at most one per line, and a zero result proves absence only in the files it searched |
| `grep -n -e A -e B -e C` / one search per literal | `search_text(queries: ["I175", "I176", "I177"])` | up to 10 literals in **one** pass over the same file set; every record carries `q1`..`qN` for the position of its literal in `queries=`, which a regex alternation cannot tell you. A line matching several is **one** record tagged `q1,q3` in query order, so a tag absent from a record means that literal is absent from that line. No legend is echoed back — you passed the array |
| a search that keeps hitting a folder you do not want | `search_text(query, exclude: ".research/**")` | dropped after `glob=` has selected, so one call answers what two used to |
| `Grep -C3` / a search then a read | `search_text(query, context: 3)` | the surrounding lines arrive on the hit's own record, indented — no follow-up `read_text` |
| `grep -r` in a log folder outside the repo | `search_text(query, root: "C:/logs")` | an absolute directory outside every workspace, tagged `outside-workspace` |
| `sort \| uniq -c` over repeated log lines | `search_text(query, unique: true)` | identical matching lines collapse to the first record plus `x<count>` |
| `Read` a non-`.cs` file | `read_text(path)` | line ranges, bounded response; a line number is printed only where the numbering jumps, so a contiguous read carries one — `verbose: true` numbers every line; a clipped read ends with `next: startLine=…` |
| `tail -n 200 log.txt` | `read_text(path, tail: 200)` | the last N lines, so the end of a huge log is addressable |
| a file whose lines are enormous | `read_text(path, maxChars: 20000)` | `maxLines` cannot bound those; the clip still names the line to continue from, and says `line N was cut mid-way` when the budget ran out **inside** a line — raise `maxChars` for that line, because a line range cannot resume at a character offset |
| `Bash: rm file` | `write_text(path, delete: true)` | containment-checked; a `.cs` document goes through the compile gate and is covered by `undo_last_change` |
| `Read` a whole `.md` to find a section | `read_text(path, headings: true)` then `read_text(path, section: "## Commands")` | the heading map with line ranges and each heading's GitHub anchor slug, then only that section |
| `Edit` a `.md` section | `edit_text(path, section: "## Commands", newText: …)` | no `oldText`, so no read-then-match round trip |
| three or more `edit_text` calls on the **same** file | `edit_text(path, edits: [{oldText, newText}, …])` | applied in order as one write, at most 10; an entry whose anchor fails is reported with its own code and remedy and the others still land, so one bad anchor never costs the batch |
| an anchor that deliberately repeats — a table of near-identical rows | `edit_text(path, oldText: "\| row \|", occurrence: 3)` | picks the Nth match instead of forcing you to lengthen the anchor; a multi-match refusal lists the candidate lines with their numbers, so `occurrence=` is picked from the refusal and needs no re-read, and an out-of-range value names the range it could have picked |
| `Edit` a `.cs` file | `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` | addressed by symbol, immune to line drift, compile-gated; `add_member` and `replace_symbol` take several declarations in one edit |
| a signature change that breaks its callers | `replace_symbol(symbolIds: [...], declarations: [...])` | one declaration per symbol, paired positionally, applied as **one** compile-gated edit across every file they live in — the way to land a signature change together with the callers it breaks instead of paying a `CompileRegression` and a retry |
| adding an **enum member** | `add_member(typeSymbolId: "T:…MyEnum", declaration: "Retry")` | an enum id takes enum members; `replace_symbol` and `delete_symbol` work on one too |
| adding a **sibling type** to an existing file | `add_member(path: "Foo.cs", declaration: "public sealed record Bar(int X);")` | appended to that file's namespace as one compile-gated edit — no whole-file rewrite, no forced text edit |
| `Edit`/`Write` a non-`.cs` file | `edit_text` · `write_text` | line endings normalized before matching; an ambiguous match is refused and a miss names the file's closest lines |
| `Write` a **new** `.cs` file | `write_text(path, content, force: true)` | no symbol tool creates a file; the new type is resolvable on the very next call, and the next `.cs` write's compile gate already sees it, so two interdependent new files land in either order |
| rewrite an **existing** `.cs` file whole | `write_text(path, content, force: true)` | compile-gated: rolled back if it introduces an error, `allowErrors: true` to opt out |
| find-and-replace a name | `rename_symbol(symbolId, newName)` | solution-wide, incl. interfaces, overrides, doc crefs **and XAML** |
| `Read` a `.xaml` file | `xaml_outline(path)` | element tree with `x:Name`/`x:Key`, no attributes |
| `Edit` a `.xaml` file | `xaml_set_property(path, target, property, value)` | addressed by element, formatting preserved |
| `Read` a `.xaml.cs` to see what the markup wires | `xaml_codebehind(path)` | `x:Class` plus every handler |
| hunting a resource through `App.xaml` | `xaml_resolve(key)` | every declaration with its scope, one call; a key with no keyed declaration lists the implicit styles targeting it, `HEURISTIC`, and names no winner |
| eyeballing a `{Binding}` | `xaml_bindings(path, validate: true)` | each path type-checked through Roslyn |
| "where is `IFoo` registered?" | `find_registrations(query)` | open generics, factories and `Add*` extensions defeat grep; a registration inside an `Add*` helper is also reported at the call site as `via AddTrading()` |
| "what endpoints exist?" | `list_endpoints()` | every `Map*` with the member it sits in |
| orienting on a symbol | `explore_symbol(symbolId)` | signature, doc, reach, implementations, XAML sites in one call |
| judging a rename before doing it | `impact_of(symbolId)` | every affected file, XAML site and recompiling project |
| "why does this control look like that" | `xaml_styles(typeName)` | implicit and keyed styles with the `BasedOn` chain, capped by `maxResults` (100) |
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
| `Bash: git status` / `git diff --stat` | `changed_files` | one line per file - path, `+added -deleted`, status letter; untracked files included, `path=` scopes it to one pathspec on a shared tree, and `exclude=` drops what a pathspec cannot leave out - `exclude: ".research/**"` for another session's notes; an excluded file is not counted |
| `Bash: git diff` to decide what to review | `diff_symbols` | every hunk mapped onto the declaration containing it, answered as symbol ids you feed straight to `get_symbol_source` - `EXACT` inside one declaration, `HEURISTIC` with the raw line range otherwise, and it ends by naming the exact `diff_text path=…` call for the hunks it could not map |
| `Bash: git diff` for the hunk text itself | `diff_text(path: …)` | the raw unified diff: whitespace, a non-`.cs` file, a pure deletion, and whatever `diff_symbols` mapped only `HEURISTIC`. It costs about a response line per changed line, so bound it - `path=` scopes it, `maxLines=` caps it at 400 |
| `Bash: dotnet build` / `msbuild` | `build` | deduplicated diagnostics, no MSBuild spew; a successful build is one line whatever it warned about, a failed one lists errors only |
| `Bash: dotnet build -c Release` | `build(configuration: "Release")` | `configuration` and `targetFramework` map to `-c` and `-f` on `build`, `run_tests`, `rerun_failed` and `list_tests` |
| `Bash: dotnet build -p:Name=Value` | `build(properties: ["Name=Value"])` | `properties` maps to one `-p:` per entry on the same four tools, applied after `-c` and `-f`; an entry that is not `Name=Value` is refused before anything runs |
| `Bash: dotnet test` / `vstest` | `run_tests` | a green run is one line, and a run that spanned several projects appends `Name:total/durationMs` per project so "which tier is slow" costs no second run; a failure carries its message, expected/actual and one source frame |
| re-running what broke | `rerun_failed` | replays the previous failures only |
| `dotnet test --list-tests` | `list_tests(contains)` | names without running |
| `dotnet format whitespace` / an IDE inspection | `analyze` · `format` · `cleanup` | compiler + every referenced analyzer + dead code |
| running `analyze` → `format` → `cleanup` → `analyze` at the end of a task | `gate` | the same four calls in the mandated order, answering one verdict line - `clean  analyzed=N fixed=M remaining=0`, where `analyzed` counts the **documents** in scope - and keeping only the diagnostics still unfixed |
| `dotnet format style` / `dotnet format analyzers` | `cleanup fix=style\|analyzers\|all` | applies the referenced analyzers' code fixes, compile-gated, `UNFIXED <id>` for what no fixer covers |
| `dotnet format --verify-no-changes` | `format verify=true` · `cleanup verify=true` | one verdict line (`clean` or `VERIFY_FAILED n`), no diff |
| formatting only what you touched | `format changed=true` · `cleanup changed=true` | files modified since the workspace loaded, so a sweep stops rewriting files the task never opened; the change set survives the unload-and-reload a locked `build` performs |
| rewriting a whole `.cs` file | `write_text(path, content, force: true)` | compile-gated like `replace_symbol` when the file is already a document: rolled back on a new error unless `allowErrors: true` |
| `Bash: dotnet clean` | `clean` | freed-byte counters, also removes `obj`, releases the workspace's file locks; `path=` sweeps a `.slnx`/`.sln`/`.slnf`/project that is **not** loaded |
| editing a `.csproj` by hand | `project_*` · `package_*` · `solution_*` | CPM-aware, containment-checked |

## 🚫 HARD GATE — take the tool from the table; the built-ins are the last resort

**Take the tool the table above names, on every call.** That is the whole rule, and it holds for
`.cs`, `.razor`, `.cshtml`, `.csproj`, `.props`, `.targets`, `.sln`/`.slnx`/`.slnf`, `.xaml`,
`.axaml`, `.paml`, `.resx` and `.resw`, and for every question about C# symbols, references,
diagnostics, builds, tests or the working tree.

**So a `Read`, `Grep`, `Glob`, `Edit`, `Write` or code-touching `Bash` call on one of those is
forbidden.** Not "discouraged" — forbidden. There is a TerseSharp tool for it in the table above.

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

`dotnet format` and `dotnet clean` are covered too, and the guard names the **exact** replacement per
sub-command: `dotnet format analyzers` -> `cleanup fix=analyzers` (`cleanup verify=true fix=analyzers`
for `--verify-no-changes`), `dotnet format style` -> `cleanup fix=style`, a bare `dotnet format` ->
`format` for whitespace plus `cleanup fix=all`, and `dotnet clean` -> `clean`. Those two verify modes
check exactly the rule sets the two CI commands check, so there is never a reason to shell out for
them. `dotnet restore`, `pack`, `publish`, `run` and `tool` are **not** covered: no
TerseSharp tool replaces them, so shelling out is the right call.

**The working tree is covered as well.** `git status`, `git status --porcelain`, `git diff` and
`git diff <ref>` are served by `changed_files`, `diff_symbols` and `diff_text` — all three take
`baseRef=`, so `main`, `HEAD~3` and a range work, and the paths come back workspace-relative and
re-usable as arguments. A bare `git ls-files` is served by `find_files tracked=true`. Running them in
`Bash` is the same breach as `grep` — but only for the tree TerseSharp serves: the guard reads the
directory the command actually addresses (`-C` target, then a directory operand, then the working
directory), so `git -C ../some-other-repo status` is allowed, because no tool here answers it. Only git **history** —
`git log`, `git log -p`, `git blame`, `git show <ref>:<path>` — and anything that mutates the index or
history — `git add`, `git commit`, `git push` — stay on the shell, because TerseSharp does not model
them.

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

## The whole surface, by job

**Workspace** — `load_workspace` · `workspace_status` · `list_workspaces` · `unload_workspace` ·
`list_projects`. Start with `workspace_status`; the server usually auto-discovers the solution, and
its last line is `terse=<version>`, which is the one place the running binary names itself — read it
before claiming what a tool does or does not do.
`list_projects(filter: "Tests")` keeps only the projects whose name contains it, and the name it
prints is exactly what `build`, `run_tests`, `list_tests` and `clean` accept as `project=`.
`unload_workspace` is the one workspace tool addressed by the solution **path** rather than a name —
`workspace=` is accepted as an alias for `path=`, but a worktree name is not a path and will answer
`not loaded`; `list_workspaces` prints the path to pass. **Four solutions stay loaded at once**, the
least recently used being unloaded beyond that; a workspace that vanished from `list_workspaces`
was evicted, not lost, and the next call naming it reloads it. The user can change the limit with
`terse serve --max-workspaces N` or `TERSE_MAX_WORKSPACES` — worth telling them when a big solution
is making the server heavy, because a loaded workspace costs roughly 3 GB on a 148-project tree.
**The server may be running a tool profile.** `terse serve --tools core` (or `TERSE_TOOLS=core`)
advertises about twenty tools instead of all 87, because the full catalogue costs tokens on every
request and measurably lowers tool-selection accuracy. It is **opt-in**, and the whole surface is the
default. The server still answers a hidden tool called by name — but an agent can only call what its
client lists, so treat the profile as narrowing what you can reach, not merely what you can see; the
`core` subset omits 33 tools this guard names as replacements, including every `xaml_*`, `resx_*` and
`razor_*`. `workspace_status` prints `tools=core - N advertised` when a profile is active.
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
Facing an unfamiliar repository, `load_workspace(path, discover: true)` lists every solution and
project under a directory without loading one — auto-discovery only walks *up* from the working
directory, so this is the call that replaces globbing for `*.sln`. Its
last line reports freshness — `watch=active gen=c12/p1/x3/r0/rz2/f4 pending=0 lastSyncMs=8 gaps=0`: the
watcher state, the per-kind generation counters (Code / Project / Xaml / Resx / Razor / Files), how many paths are
waiting to be examined, and how many watcher events were lost. `load_workspace(reload: true)` forces a
re-read from disk; you should almost never need it. The line after it reports the workspace index —
`index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-) razor(hit=3 miss=1 files=10)
paths(hit=7 miss=1 files=31324) documents=9/128 parses=9`.

**`find_files`, `search_text` and `search_regex` answer from that `paths` index, not from a fresh
walk.** The tree is enumerated once and re-enumerated only when the watcher sees a file appear,
disappear or get renamed, so a repeat `find_files` on a 31 000-file solution costs a glob match over
an in-memory list rather than a full directory walk. Ask them as often as you like; a file you or the
user just created, deleted or renamed is in the answer without a reload — the writers say so directly,
so it does not wait on a watcher event. When the watcher is off or degraded the index is not trusted
and the tree is walked again — correct, just slower.

**`failures=` and `warnings=` are different things.** `failures=` counts projects that did not load;
`warnings=` counts MSBuild diagnostics that did not stop a load — NuGet advisories (NU1903), target
framework notes (NU1701) and the like. A big solution routinely reports `failures=0 warnings=20` and
is fully usable. **Neither is listed by default**: the warnings are a count, and the failures are
folded to one `FAILED <project>  messages=N` line per project under a `N load failure(s) in M
project(s)` header. `verbose=true` prints every message of both. So do not read a warning count as a
broken workspace, and do not fall back to the built-ins over one.

**Navigate** — `search_symbols` (production declarations first; when the test half also matches, it
is folded to one `N more in test projects - scope=test` line, and `scope=src|test` keeps one half
outright) · `get_symbol` · `get_file_outline` · `get_type_outline` ·
`get_symbol_source` · `find_usages` · `find_implementations` · `explore_symbol` · `impact_of`.
A usage inside generated code is tagged `gen` rather than `src` — it is a real reference, but the file
is regenerated, so never edit it.

**.NET semantics grep cannot reach** — `find_registrations` (DI) · `list_endpoints` (ASP.NET Core).

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

**Analyse — at the end of a task, call `gate` and stop there.** It runs `analyze` at `info`,
`format`, `cleanup fix=all` and `analyze` again, in the order this project mandates, over the files
changed since the workspace loaded, and answers **one verdict line**. That is the whole end-of-task
sweep in one call instead of four, and it is the first thing to reach for — a measured week of this
server's own sessions made 356 `analyze` calls and **zero** `gate` calls. Reach for the individual
tools below only when you need one of them on its own, or when `gate` reports `FAILED` and you are
fixing what it named.

`analyze` (compiler + analyzers + dead code, down to `info`; `path=` takes a file, a
directory or a glob and `changed=true` limits it to files modified since the workspace loaded — and
that change set is carried across the unload-and-reload `build`/`run_tests` perform on a locked
output, so an analyze after a build no longer answers `no document under that scope was modified` — so the
end-of-task gate over a task's touched files is **one** call, not one per file; `sinceLast=true` reports
only what appeared since the previous run of the same scope, plus what was fixed) ·
`get_diagnostics` · `format` (whitespace; `verify=true` for a one-line verdict, `path=` takes a file, a directory or a glob) · `cleanup` (`fix=usings` by default; `fix=style|analyzers|all` applies the referenced analyzers' code fixes with `ids=` and `severity=` filters, reports `UNFIXED <id>` for what no fixer covers, and never rewrites generated code) · `clean` (deletes `bin`/`obj`, `dryRun=true` to preview, not covered by `undo_last_change`) ·
`gate` (the end-of-task sequence as one call: `analyze` at `info`, `format`, `cleanup fix=all`, then
`analyze` again, over the files changed since the workspace loaded unless `path=` or `solution=true`
says otherwise). `gate` answers **one verdict line** - `clean` or `FAILED` - and, when it is not clean, each step's
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

**`format verify` and `cleanup verify` are not the same gate.** `format` compares against the Roslyn
whitespace formatter, which `dotnet format style` and `dotnet format analyzers` do not run — a
`VERIFY_FAILED` there can still be a green CI leg. `cleanup verify=true fix=style` and
`fix=analyzers` are exactly those two CI commands; `fix=all` and the default `fix=usings` are
supersets and may name files CI accepts.

**A missing path is answered, not just refused.** `get_file_outline` and `read_text` on a path named
after a type the workspace declares elsewhere name the file that declares it, and `add_member path=`
on a `.cs` file nobody has written yet names `write_text path=… force=true` — neither sends you to
`find_files`, which cannot find a type that does not name its file.

**Edit** — `replace_symbol_body` · `replace_symbol` · `add_member` · `delete_symbol` · `rename_symbol`
· `undo_last_change`. `add_member` and `replace_symbol` accept **several declarations in one call**,
applied as a single compile-gated edit — so a set of members that reference each other needs no
dependency ordering, and `replace_symbol` can split a member into overloads. On a member that is
already expression-bodied, `replace_symbol_body` accepts a bare expression as well as `=> expr` and a
statement block.

**`usings=` lands the import in the same edit.** `replace_symbol_body`, `replace_symbol` and
`add_member` take `usings: ["System.Collections.Immutable"]`, added to the file's using block —
sorted System-first, one already present ignored — inside the **same** compile-gated write as the
declaration. That is the answer to a `CS0246` rollback: pass the namespace instead of paying a
rejected edit, an `edit_text force=true` on the file header and a `retryWith`.

**`replace_symbol` also edits several files as one compile-gated edit.** Pass `symbolIds` and
`declarations` — one declaration per symbol, paired positionally, at most 20, and more than one entry
per file is allowed. That is how a signature change lands **together with the callers it breaks**:
sent one at a time it is rolled back as a `CompileRegression`, and callee-first ordering does not help
because the callee is what is changing. Unpaired arrays are refused naming both counts, and two edits
where one declaration **contains** the other are refused whichever order you send them in, rather than
silently dropping the inner one.

**Refactor** — `extract_interface` · `move_type_to_file` · `move_type_to_namespace` ·
`change_signature`.

**Projects** — `solution_projects` · `solution_add_project` · `solution_remove_project` ·
`project_create` · `project_properties` · `project_set_property` · `project_add_reference` ·
`project_remove_reference` · `package_list` · `package_add` · `package_remove`.
**"Which projects does this solution contain?" for a solution that is _not_ loaded** is
`solution_projects(path: "fixtures/FixtureSolution/FixtureSolution.slnx")` — it reads the `.slnx`,
`.sln` or `.slnf` directly and loads nothing, so writing a fixture-scoped test does not cost a
`load_workspace` that makes every later un-hinted call ambiguous. `list_projects` is the loaded-
workspace answer and carries the language and document counts a file cannot know.

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

**Git** — `changed_files` · `diff_symbols` · `diff_text`. The only other deliberate shell-out beside
`build`/`run_tests`, and the answer to the end-of-task review, which is defined over the diff. Start
with `changed_files` (one line per file: path, `+added -deleted`, status; untracked included), then
`diff_symbols` to turn the hunks into declaration ids, then `get_symbol_source` on the two or three
bodies you actually intend to read. `diff_text` returns the raw unified diff and is the last resort —
scope it with `path=`. **`changed_files` and `diff_text` also take `root=`** - any absolute directory, answered without
loading it and tagged `outside-workspace` - so a sibling worktree or another repository needs no
second `load_workspace` and no `git -C` in `Bash`. `diff_symbols` deliberately does **not**: mapping a
hunk onto a declaration needs that directory's Roslyn compilation, so it refuses and names the two
tools that can answer. All three take `baseRef=` (empty compares the working tree against `HEAD`) and
`path=`, and are scoped to the workspace root with git's own `--relative`, so a workspace nested
inside a larger repository never reports a file outside it. On a tree shared with other sessions,
`changed_files(path: "src")` is the difference between reading your own change set and reading
everybody's, and `changed_files(exclude: ".research/**")` drops the folders a positive pathspec
cannot leave out. `diff_symbols` tags a hunk `EXACT` only when it sits
inside exactly one declaration; anything else is `HEURISTIC` with the raw line range and the reason.

**Files** — `read_text` · `write_text` · `edit_text` · `find_files` · `search_text` · `search_regex`.
`search_text` and `search_regex` take `query` — or `queries=[...]`, up to 10 literals or expressions
answered in **one** pass over the same file set, every record tagged `q1`..`qN` by the position of its
query in the array. The count stays *matching lines, at most one per line*: a line matching several
queries is **one** record carrying every matching tag, comma-separated in query order (`q1,q3`).
An entry that matches across a line break — a literal containing a newline, or `[\s\S]` / `(?s).` in
a regex — is reported **once, at the line its text starts on**, and the scan resumes on the next
line, so every other entry still sees the lines that match spanned.
`query` and `queries` combine, `query` first; an 11th entry is refused naming the
cap rather than truncated, and a blank entry is refused rather than matching everything.
`find_files` takes `glob`, and each accepts `pattern`
as an alias — `find_files` accepts `query` too — so the wrong name of the three is never a failed
call. A parameter name **no** tool declares is refused before the call runs, naming every accepted
spelling: an argument the server does not understand is never silently dropped, because a listing
that ignored your `maxResults` is a confidently wrong answer you cannot detect. `find_files`, `search_text` and `search_regex`
skip `bin`, `obj`, `.git`, `.claude`, `.vs`, `.idea`, `artifacts`, `TestResults`, `node_modules` and
directory symlinks — the same set every index uses, so a nested agent worktree never doubles a result.
`read_text` also accepts an **absolute path outside every workspace root**, tagged
`outside-workspace`, so comparing a file against another repo needs no second `load_workspace` and no
`workspace=` even with several loaded; every writer still refuses to leave the workspace.
`search_regex` anchors `^` and `$` to each line, and a match that **spans** lines - `^\s*(public|private)`
starting on the blank line above - is reported once, at the first line carrying its text, instead of
twice with the blank line first. Both searches take `matchesOnly=true`, which prints the matched span
instead of the whole line the way `grep -o` does and composes with `unique=true` to answer "which
distinct values of this shape exist"; a match that is only whitespace still prints its line, so no
record is ever empty. Both take `exclude=`, a glob applied after
`glob=` has selected, for the folder a positive glob cannot leave out. `find_files(stamps: true)`
appends each file's UTC last-write time and byte length. `read_text` clips at **40 960** characters
unless `maxChars` says otherwise (ceiling 131 072): the default is set so a whole-file read stays
inline in your client rather than being spilled to a file that answers nothing, and the clip always
names `next: startLine=`.

**`read_text` on a `.cs` path asked for whole answers the outline, not the text.** No `startLine`,
no `endLine`, no `tail`, no `section`, no `verbose` — you get exactly what `get_file_outline` would
have returned, plus one line naming `get_symbol_source` and the opt-in. Whole-file `.cs` reads were
71 % of everything this tool has ever returned and an outline is a third of the tokens, so this is
the default that matches what the question almost always is. Pass `verbose: true` for the raw text,
or any line range when you already know which lines you want. A `.cs` file that is not a document of
this workspace is read as text unchanged.

**Build and test** — `build` · `clean` · `run_tests` · `rerun_failed` · `list_tests`.

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
   an id that does not resolve is reported inline as `NOT_RESOLVED <id>` instead of failing the call.
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
5. **Edits are compile-gated.** An edit introducing a new compile error is rolled back and the error
   returned. `allowErrors: true` opts out — use it only mid-refactor on purpose.
   **A rollback keeps your text**: the error ends `retryWith=r3`, and `replace_symbol`,
   `replace_symbol_body` and `add_member` take `retryWith: "r3"` to replay exactly what was rejected —
   after you add the missing callee, or together with `allowErrors: true`. Never re-send the whole
   declaration to retry; the server holds the last 8 rejections and says so if a token has expired.
   **When every new error is just a missing import, the remedy names it**: a rollback whose errors are
   all `CS0246`/`CS0103` for names the project resolves in exactly one namespace each answers
   `remedy: add: using System.Collections.Immutable; then replay the rejected text with retryWith`.
   Add the using with `edit_text force=true`, then `retryWith` — the using is never added for you,
   because that would edit a region you did not address.
   **A token belongs to the workspace it was rejected in**: replaying it against another one - a
   sibling worktree where the same symbol id resolves - is refused naming both roots, instead of
   landing the held declaration in the wrong tree. Every diagnostic a rollback lists names its file
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
   source-generator assemblies (or under `verbose=true`); a non-zero count means an external
   `dotnet build` over those files will fail `MSB3027` until the server restarts.
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

12. **A line starting `UPDATE terse` is not part of the answer — it is a message for the user.** Once per
    server process, at most once a day, the first tool response may carry one extra last line:
    `UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp`. Everything
    above it is the tool's real answer and is unaffected. Tell the user the newer version exists and
    what to run; do **not** run the update yourself, do not retry the call, and do not treat the line as
    an error. It appears once and never repeats in that session. After the user updates, the next
    `terse serve` rewrites the installed `SKILL.md` and the `terse guard` hook to match the new binary,
    so the skill you are reading always describes the binary you are talking to.

13. **Independent calls go in one message.** If you intend to call several tools and there are no
    dependencies between them, make all of the independent calls in parallel, in a single assistant
    message, rather than one after another. Reading three files is three `get_symbol_source` calls
    issued together; outlining four files is four `get_file_outline` calls issued together;
    `changed_files` and `workspace_status` have nothing to do with each other and belong in the same
    message. Prioritize calling tools simultaneously whenever the actions can be done in parallel.
    **But when a call needs a value a previous call returns — a symbol id from an outline, a path
    from `changed_files`, a `retryWith` token from a rollback — call them sequentially, and never
    guess a parameter to make a call parallel.** A measured week of this server's own sessions
    carried 17 567 tool calls and **not one** parallel message, while 5 989 of them sat in runs of
    three or more consecutive calls of the same tool; at this server's median call latency that is
    hours of wall clock nothing depended on.

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
`run_tests PASSED  passed=478 skipped=0 total=478 durationMs=122371` — so running the suite after every
change is nearly free. A run that spanned **more than one project** appends `Name:total/durationMs`
per project to that same line
(`… durationMs=122371  TerseSharp.UnitTests:310/12043ms  TerseSharp.E2ETests:168/110328ms`), so
"which tier is slow" and "did every tier actually run" are answered by the run you already paid for,
not by a second one. A single-project run is unchanged. `build` behaves the same way
(`build ok  errors=0 warnings=0  elapsedMs=4235`), warnings included: a build that succeeds is one
line however many warnings it produced, and a build that fails lists errors only. `warnings=` counts
what that build emitted, so a build that recompiled nothing reports `0`.
The short form is only ever emitted when there is nothing else to report, so do not pass
`verbose=true` "to be sure". Anything that is not a clean pass returns the full report:
`run_tests` reports `passed= failed= skipped= total= durationMs=`, then one block per
failure: the message, expected and actual values, and one workspace-relative `file:line` frame. Fix
the test from that block — do not shell out to `dotnet test` for the stack trace.

| Goal | Call |
|---|---|
| whole solution | `run_tests` |
| one project | `run_tests(project)` — a project **name** or a path to the `.csproj` |
| one test, or a class/namespace prefix | `run_tests(test)` — not combined with `filter` |
| a raw VSTest expression | `run_tests(filter)` |
| only the test projects your change can reach | `run_tests(changed: true)` — selects the test projects that transitively reference a project you changed since the workspace loaded, at **assembly** granularity, and names both what it ran and what it skipped. It falls back to the whole solution, saying why, whenever it cannot reason — nothing changed, a changed file belongs to no project, or no test project depends on the change — so it never silently runs less than it should. Ignored when `project=` is passed |
| skip the rebuild | `run_tests(noBuild: true)` |
| only what just failed | `rerun_failed` |
| the slowest N | `run_tests(slowest: 10)` |
| names without running | `list_tests(contains)` |
| the full report on a green run | `run_tests(verbose: true)` |

`test=` is a **substring** match, so a name that is a prefix of another (`…Submits` vs
`…SubmitsTwice`) runs both — check `total=`, and use `filter="FullyQualifiedName=<name>"` for exactly
one.

`total=0` with a `WARNING` means **nothing ran** — a filter typo, not a green suite. A run that
produced no results reports `FAILED …, no test results were produced` and never `0 failures`.

`project=` takes the name `list_projects` prints as readily as a path — `run_tests(project: "Trading.Tests")`
resolves against the solution's projects first and then against the `*.csproj` under the workspace
root, so a test project outside the solution still runs. An unknown name answers `ERROR ProjectNotFound`
naming the closest projects and a name two projects share answers `ERROR AmbiguousProject` listing
both; neither is ever handed to MSBuild as a path.

When a locked output file blocks the build that `build`, `run_tests`, `rerun_failed`, `list_tests` or `clean` runs,
the response says so (`WARNING a locked output file blocked the operation`) and, with a single
workspace loaded, the server unloads it, retries and reloads, then reports which of the three happened
in a `NOTE`. You do not need to `unload_workspace` by hand first. When the note says the output is
**still** locked it also lists every process the build named, one `holder pid=… <name> startedUtc=…`
line each, classified as this terse server, an MSBuild or BuildHost — including one an *earlier*
terse load spawned out of this tree's own `bin/` — a live `testhost` you should wait for rather than
stop, a bare `dotnet` host, or a pid that is already gone. The only holder the note rules out is the
analyzer and source-generator set, which is mapped from a shadow copy and never from a project's own
output; read the `holder` lines before stopping anything.

## When a tool refuses

Errors are `ERROR <Code>` plus a `remedy:` line. `SymbolNotFound` suggests the nearest names;
`AmbiguousSymbol` lists the candidates and says how many of the total it shows; `SaturatedName` means
the name matched too many symbols to resolve safely — and it is now reached only by a **bare** name:
a `Type.Member` whose member name saturates is resolved through the members of the types called
`Type`, so qualifying the name really is the fix the remedy names; `OutOfWorkspace` means the path
escaped the workspace root; `ProjectNotFound` and `AmbiguousProject` come from a `project=` that names
no project or two, and list the candidates; `InvalidArgument` naming a **missing** or **unrecognized**
parameter means the argument names were wrong, and the remedy lists the ones the tool declares; an
`InvalidArgument` carrying a `JsonException` also names the **array** parameter it could not convert
and quotes the ~80 characters around the offending byte, so a 9 000-character `declarations=` is
located without re-sending it;
`ReadOnly` means the server runs with `--read-only`.

Read the `remedy:` and fix the call. Falling back to `Read`/`Grep` is the one outcome this server
exists to prevent.

**Need a call of a tool that actually works?** For the tools whose valid arguments are not derivable
from the schema — the ten `razor_*` tools and `package_add`/`package_remove` — the `remedy:` of a
rejected call ends with `example: <a complete, working call>`. Calling one of them with no arguments
on purpose is a one-call way to get that shape; do not go read a test file for it.
