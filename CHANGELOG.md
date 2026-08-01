# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

## [0.15.1] - 2026-08-01

Seven defects found after 0.15.0 shipped — six by the review, one by CI on macOS.

### Fixed

- **`WorkspaceNotLoaded` on the first tool call after start.** The 0.15.0 fix for the cold-start
  handshake (**I28**) started the stdio transport *before* calling `Preload`, which is what assigns the
  `ready` task every tool awaits. That opened a window where a request arriving immediately found an
  empty registry and was answered `ERROR WorkspaceNotLoaded` instead of waiting. Reproduced by CI on
  macOS (`ReadOnlyServerE2ETests.ReadTools_StillWork`), which is the runner fast enough to hit it.
  `Preload` is assigned on the startup path again; the heavy work stays off it via the `Task.Run`
  introduced in 0.15.0, so the handshake is still not blocked by MSBuild registration or the first load.

- **`PathBoundary.SameFile` no longer calls `File.ResolveLinkTarget` on every comparison.** Because `||`
  short-circuits the other way, the symlink clause added in 0.15.0 ran a filesystem syscall on **both**
  operands for every pair that did *not* match — the common case in every scan over documents. Measured
  at ~108 µs per non-matching call, **~270× slower** than 0.14.0 (8 ms → 2 164 ms over 20 000
  comparisons), on `DocumentLookup`, `CodeFixService` (per diagnostic × per file × up to 25 passes) and
  `RazorContext`. Worse, it did not do what it was added for: a symlinked worktree is a *directory*
  link, so `File.ResolveLinkTarget` on the solution file returns `null`. Link resolution now lives in
  `WorkspaceRegistry` identity only, resolves the **parent directory** with `Directory.ResolveLinkTarget`,
  and runs once per `load_workspace` rather than once per comparison.
- **A `.razor` edit invalidates the generated-symbol cache again.** 0.15.0 replaced
  `RazorIndex.Invalidate` with `Sync.Noticed` and deleted the method — which was `RazorGeneratedMap.Forget`'s
  only caller. Since that map self-invalidates only on a *count* change, editing a component's content
  left `razor_usages`, `razor_codebehind` and `rename_symbol` resolving members from the pre-edit
  compilation, tagged `EXACT`, for the life of the process. `Noticed` now forgets the map on a Razor bump.
- **`project_*`, `package_*` and `solution_*` no longer force a solution reload for a `dryRun`.** The
  0.15.0 write-guard notified on any `IsOk`, and a `dryRun` returns `IsOk`; `ChangeKind.Project`
  unconditionally requests a rebuild, so previewing a diff cost a full MSBuild reload on the next call.
- **`solution_add_project` and `solution_remove_project` notify the solution file they actually wrote**,
  not the `.csproj` argument — which for `project_create` may not even exist yet.
- **`cleanup ids=` is case-insensitive again.** The 0.15.0 analyzer filter compared ids ordinally while
  the result filter used `OrdinalIgnoreCase`, so `cleanup fix=all ids=ca1822` selected no analyzer,
  produced no diagnostics and reported a clean pass having fixed nothing — a silent wrong answer.
- **The XAML `Mentions` pre-filter and the binding finder share one predicate.** The filter only looked
  at values *starting* with `{` while `XamlBindingService` matches a binding anywhere in the value, so
  `Text=" {Binding Amount}"` could make `rename_symbol` skip the file and report success.
- `GeneratedCode.InOutputDirectory` tests the final path segment again, matching pre-0.15.0 behaviour for
  a path that ends in `obj` or `bin`.

## [0.15.0] - 2026-08-01

Closes every remaining row in the improvements backlog.

### Changed

- **The watcher now covers Razor.** `ChangeKind.Razor` joins Code, Project, Xaml and Resx, and
  `WorkspaceGenerations` gains a fifth counter, so `workspace_status` prints
  `gen=c12/p1/x3/r0/rz2`. `.razor` and `.cshtml` were classified as `null` before, which meant no
  watcher coverage at all and no generation to key an index on. **Status-line format change.**
- `find_registrations` follows one level of `Add*` extension methods. A registration wrapped in
  `services.AddTrading()` is now reported at the call site as `AddSingleton<…>  via AddTrading()`, not
  only inside the helper. The helper's own body is still reported, and the chain is followed exactly
  one level - following it arbitrarily is whole-program analysis.
- `xaml_resolve` on a key that matches no keyed resource now lists the implicit styles whose
  `TargetType` is that key, tagged `HEURISTIC`, and **explicitly declines to name a winner** because
  the index does not model per-dialect resource lookup order. A wrong winner would be the confident
  wrong answer the response contract forbids.

### Fixed

- **`replace_symbol_body` accepts the expression body its own error message advertises.** `=> 42;`
  was wrapped as `{=> 42;}`, which parsed into an error-node block, passed the `is BlockSyntax` check
  and produced broken code that only the compile gate caught. Expression bodies are now applied as
  `ArrowExpressionClause`, and a block that fails to parse is refused instead of applied.
- **`RazorIndex` is per-workspace and generation-keyed.** It was a process-wide `static
  ConcurrentDictionary` with no bound, plus a full directory walk and one `stat` per file on every
  call at five sites - including `workspace_status`, which paid it on every status call. It now lives
  in `WorkspaceIndexes` beside the XAML and resx indexes, reuses unchanged documents from the previous
  generation, and is reported in the `index=` line. Closes **I21**.
- **`resx_files` and `resx_validate` no longer re-parse the overflow beyond the 128-document LRU.**
  The per-file translatable key set is cached on the index itself, which is replaced wholesale when
  the resx generation changes, so it is bounded without being unbounded. Closes **I22**.
- **The XAML sweep in `find_usages`, `rename_symbol` and `explore_symbol` no longer parses every XAML
  file.** Each `XamlFileRecord` now carries the identifiers its handlers, binding paths and `x:Class`
  mention, so only files that could match are parsed. Closes the half of **I25** those three tools
  pay; `xaml_find` and `xaml_validate includeUnused=true` still need whole documents by nature.
- **`cleanup fix=…` drives the analyzers with the requested id set.** `ids=` narrowed only the filter,
  so the whole analyzer set ran once per diagnostic id, up to 25 times per project. Analyzers are now
  filtered to those whose `SupportedDiagnostics` intersect the request. Closes **I14**.
- `unload_workspace` clears the fixer catalog, so an unloaded workspace stops pinning analyzer
  assemblies. Closes the practical half of **I15**; the collectible load context remains the only way
  to release the files themselves, and is now the sole content of that row.
- `project_set_property`, `package_add`, `package_remove` and `solution_add_project` tell the workspace
  which file they wrote, so they are correct under `--no-watch`. Closes **I19**.
- `PathBoundary.SameFile` resolves symlinks with `File.ResolveLinkTarget`, so a symlinked worktree no
  longer produces two registry entries for one solution. Closes **I20**.
- **The server answers `initialize` before it touches the workspace.** The preload ran on the startup
  path ahead of `host.RunAsync`, so MSBuild registration and the first solution load could eat into the
  fixed 60 s handshake ceiling - the cold-runner timeout seen on the v0.14.0 tag. The host starts
  serving first and the preload runs on the thread pool. Closes **I28**.
- An interleaved `edit_text` and symbol edit on the same file is covered by a regression test that
  asserts **both** changes survive. Closes **I10**, whose silent-revert form the watcher had already
  fixed; the failure that remained was the expression-body bug above.
- The E2E fixture retries the MCP handshake once when it times out, so a cold runner is a retry rather
  than a false red. This is belt-and-braces beside the `initialize` fix above.


## [0.14.0] - 2026-08-01

### Changed

- **`format`, `cleanup` and `clean` report one line per changed file instead of a diff.** `format` and
  `cleanup` print `path  changedLines=N` per file plus the `errors=/warnings=` counters; `clean` prints
  its counters and stops. `verbose=true` restores the diff and the per-directory list. A rolled-back
  edit, a locked directory and every `dryRun` keep the full output, because those are results that have
  something to say. Response-format change to three tools. Closes **I26**.
- **`write_text force=true` on a `.cs` file that is already a workspace document is now compile-gated.**
  It runs through `EditGate` exactly like `replace_symbol`: the diff, the `errors=N (+D)` counters, and
  a rollback if the write introduces a compile error. `allowErrors=true` opts out for a deliberate
  mid-refactor write. This closes the last hole in the compile gate the server advertises — the index
  task did 9 unchecked whole-file rewrites and the previous release did 6. A file that is not yet a
  document is still written directly; there is nothing to compare it against. Closes **I24**.

### Added

- **`format(changed: true)` and `cleanup(changed: true)`** limit the pass to files modified since the
  workspace loaded, so a post-edit sweep stops reformatting files the task never touched. Closes the
  half of **I23** that was still open; generated code under `obj/` was already excluded.
- **`xaml_add_element(position: "first" | "last")`.** `last` is the default and inserts before the
  closing tag; `first` inserts right after the opening tag. An element with no matching closing tag is
  refused rather than guessed at.

### Fixed

- **`replace_symbol` and `replace_symbol_body` no longer emit the replacement's opening brace at column
  0.** The new node is annotated and run through the Roslyn formatter, so a body passed with its own
  braces lands at the member's own indentation. Observed 20+ times in the previous task, each costing a
  `format` sweep afterwards. Closes **I27**.
- **`replace_symbol` no longer reports `applied` for a no-op.** A declaration whose full text matches
  what is already there answers
  `0 files changed - the declaration is identical to what is already there, so nothing was written`
  instead of a success that wrote nothing. Closes **I9**.
- **`replace_symbol` and `delete_symbol` work on fields.** A field symbol's declaring syntax is the
  variable declarator, so replacing it threw `InvalidCastException: FieldDeclarationSyntax →
  VariableDeclaratorSyntax` and deleting it left a dangling `private int ;`. The target is now promoted
  to its field declaration, and a field that shares one declaration with others (`int a, b;`) is refused
  with a remedy naming what to do. Closes **I8**.
- **`get_file_outline` on a file of top-level statements no longer answers `0 types`** — a claim it
  cannot support, which reads as "the file is empty". It now reports the statement count, the file's
  length and `use read_text`, with a line range per statement. Closes **I18**.
- **`SymbolNotFound`'s `nearest:` line no longer suggests a candidate the resolver would also reject.**
  A name that cannot round-trip — a constructor, an operator, a generic method, a member of a generic
  type — is offered as its documentation id instead of the short form. Closes **I16**.
- **A rebuilt analyzer at an unchanged path no longer serves stale `CodeFixProvider` instances.**
  `FixerCatalog`'s key now includes each analyzer reference's last-write time and length, and the
  process-wide cache is bounded at 32 entries. Closes the correctness half of **I15**; the collectible
  load context it also asks for is still open.
- `xaml_set_property`, `xaml_add_element` and `xaml_remove_element` tell the workspace which file they
  wrote instead of relying on the watcher, so they are correct under `--no-watch` too. Partly closes
  **I19**; `project_*`, `package_*` and `solution_*` still rely on the watcher.
- `load_workspace` matches an already-loaded solution by file identity rather than by normalised path
  string, so two spellings of the same solution no longer produce two entries that make every later
  call ambiguous. Closes the practical half of **I20**.
- CI: `dotnet format style --verify-no-changes` failed on `IDE0022` after 0.13.0. The rule is now part
  of the pre-push check (`cleanup verify=true fix=style` and `fix=analyzers`, plus `format verify=true`).

## [0.13.0] - 2026-08-01

### Changed

- **A green `run_tests` and a clean `build` now answer in one line.** Measured over this repo's own
  sessions, a passing suite cost 60-150 tokens of counters, warnings and timing blocks that no agent
  ever acts on. `run_tests` on a run where `exitCode=0`, nothing timed out, `total > 0` and there are
  no failures returns
  `run_tests PASSED  passed=478 skipped=0 total=478 durationMs=122371  (verbose=true for the full report)`;
  `build` with `exitCode=0`, zero diagnostics and no locked output returns
  `build ok  0 diagnostics  elapsedMs=4235  (verbose=true for the full report)`. **Any failure, any
  diagnostic, a timeout, a zero-test run and a locked output all keep the full report** - the short
  form is only ever emitted for a result that has nothing else to say. `verbose=true` restores the old
  response, and `includePassed` or `slowest` on `run_tests` imply it. `rerun_failed` takes `verbose`
  too. This is a **response-format change** to `build`, `run_tests` and `rerun_failed`.
- `run_tests` prints up to 30 lines of a failure message, was 12, so a multi-line assertion diff
  survives.
- `AmbiguousWorkspace` and `WorkspaceNotFound` now list each workspace as
  `App.slnx (worktree) -> C:\full\path` instead of the path alone, so the remedy names something that
  actually resolves.

### Added

- **`read_text(path, headings: true)`** returns a markdown file's heading map with line ranges and no
  body, and **`read_text(path, section: "## Commands")`** returns one section. Locating two sections of
  a 216-line `CLAUDE.md` used to mean pulling the whole file (~2.6k tokens); the heading map is ~40
  lines. Closes **I1**.
- **`edit_text(path, section: "## Commands", newText: ...)`** replaces a whole markdown section with no
  `oldText` at all, which removes the read-then-match round trip on every documentation edit. Closes
  **I2**. `oldText` is now optional; passing neither `oldText` nor `section` is refused.
- `write_text` creates the directories its target needs instead of failing with
  `DirectoryNotFoundException`. Closes **I3**.

### Fixed

- **`edit_text` no longer fails on a line-ending mismatch.** Matching falls back to a
  line-ending-normalized comparison and maps the result back to the file's real offsets, so an `\n`
  `oldText` matches a CRLF file and only the replaced region is rewritten. Measured on this repo's own
  session log, **130 of 577 `edit_text` calls (22.5%) failed with `oldText matched 0 times`**, and the
  remedy - "include more surrounding text" - made the next attempt *less* likely to match. When
  nothing matches, the error now names the file's closest lines
  (`L21: public static async Task<Result<string>> ...`) instead. Closes **I7**.
- `write_text` keeps the line endings of the file it overwrites, and uses the solution file's dominant
  ending for a new file, so the next `format` no longer rewrites the whole document. Closes **I12**.
- **`add_member` no longer glues the new member to the previous one or to the type's closing brace.**
  It inserts a blank line before the member and keeps `}` on its own line. In two prior tasks this
  defect cost **9 `add_member` calls -> 12 corrective `edit_text` calls** and **6 -> 8** - every one of
  them a `force=true` line edit on C#, the exact fallback the server exists to remove. Closes **I11**.
- **`workspace=` resolution no longer answers `AmbiguousWorkspace` for a hint that names exactly one
  workspace.** Hints are ranked - full path, solution file name, solution name without extension,
  worktree name, root directory name, then substring - and only ties *within the best tier* are
  ambiguous. Loading a repo and a solution nested inside it (this repo and its `fixtures/`) now
  resolves a path hint to the **innermost** workspace containing it rather than refusing. 88
  `AmbiguousWorkspace` errors appear in this repo's own session log. Closes **I5** and **I13**.
- `read_text` no longer counts a phantom trailing line: a file ending in a newline reported
  `total=N+1`.

### Performance

- **Every file-system call on the request path is asynchronous.** `read_text`, `write_text`,
  `edit_text`, `search_text`, `search_regex`, every `.resx`, XAML and Razor writer, the project and
  solution file writers and `terse install` now use `File.ReadAllTextAsync`/`WriteAllTextAsync` and
  `FileStream` with `FileOptions.Asynchronous`; `AtomicWrite.Text` became `AtomicWrite.TextAsync`.
- **`search_text` and `search_regex` scan files in parallel and allocate nothing per non-matching
  line.** They used to materialize one `string` per line of every file and call `string.Contains` on
  each. They now read each file once and walk it with a vectorized `MemoryExtensions.IndexOf` over the
  span (`Regex.EnumerateMatches` for `search_regex`), materializing a string only for a line that
  matched, and fan out over `Parallel.ForEachAsync`. File sizes come from the directory enumeration
  rather than a `FileInfo` stat per candidate.
- `edit_text` counts occurrences with a span scan instead of allocating a full copy of the file with
  every occurrence removed.
- `FileGlob` matches against a `stackalloc` buffer instead of allocating a separator-normalized copy of
  every path it tests, and skips the copy entirely for a path that has no backslash.

## [0.12.0] - 2026-08-01

### Fixed

- **A file created or edited outside the symbol tools is now part of the workspace.** A loaded
  solution was a snapshot taken at load time and nothing ever re-read it, so `write_text` on a new
  `.cs` followed by `replace_symbol` returned `SymbolNotFound`, and an external edit — your IDE,
  `git checkout`, `dotnet format` — was answered from the load-time snapshot **with an `EXACT` tag**,
  which is the response contract's worst failure: a confident wrong answer the agent cannot detect.
  Each workspace now runs a `FileSystemWatcher`, but the watcher is only a hint: state changes after a
  **content comparison**, so a dropped, duplicated or out-of-order OS event can delay a refresh and
  never corrupt one, and the server's own writes are naturally no-ops. Sync is **lazy** — events
  accumulate and are drained by the next call that needs semantics, so a `git checkout` storm costs
  one reload rather than one per file. Before answering about a specific file, its
  `(LastWriteTimeUtc, Length)` is compared against the last known stamp, which catches an event the OS
  dropped and is why `--no-watch` is still correct. A changed `.csproj`, `.props`, `.targets`, `.sln`,
  `global.json` or `.editorconfig`, a `.cs` added or removed under a project's directory, a watcher
  buffer overflow and an over-cap pending set all reload the solution rather than guess; a call
  already holding a lease keeps answering from the snapshot it was addressed against.

- **`undo_last_change` actually reverts now.** It stored whole `Solution` snapshots and replayed them
  through `TryApplyChanges`, which refuses a solution whose workspace version has moved on — so every
  undo after a real edit answered `the workspace refused the revert`. No test had ever exercised a
  successful undo, only the empty-history path. Undo now replays the previous **document texts** onto
  the current solution.

- **A workspace lease is released when a tool call fails.** The sync point held a lease across an
  `await` with no `try`/`finally`, so a cancelled call or an `IOException` from a file being written
  leaked it: the lease count never returned to zero, the `MSBuildWorkspace` was never disposed, and
  `unload_workspace` reported success while MSBuild kept its file locks — defeating the documented
  unload → build → load recipe.

- **The resx document cache was `static`, unbounded and shared by every workspace in the process.**
  Keyed by absolute path and pruned only by an edit, it grew monotonically for the life of the server
  and outlived the workspace that filled it. It is now a per-workspace bounded cache that dies with
  its workspace, so a long-lived server holding several worktrees cannot accumulate parsed resources
  it will never read again.

- **An edit made through TerseSharp's own tools now moves the generation counters.** The counters only
  ever moved for a change the watcher *found on disk*, and an edit applied through `add_member`,
  `replace_symbol`, `rename_symbol` or `undo_last_change` leaves the in-memory solution and the file
  byte-identical, so the drain saw nothing to report. That was invisible while nothing depended on the
  counters; with an index keyed on them it would have meant `find_registrations` answering *"no
  AddSingleton/AddScoped/AddTransient call mentions this type"* for a registration the same session had
  just written — a confident wrong answer with no staleness marker. Applying a solution change now
  bumps `Code`, and `xaml_set_property`/`xaml_add_element`/`xaml_remove_element` and the `resx_*`
  writers bump `Xaml` and `Resx`, so a tool's own write invalidates the indexes that read it instead of
  waiting on watcher latency.

### Added

- **`load_workspace(reload: true)`** discards the in-memory solution and reads it from disk again.
  Generation counters carry over across the reload and the undo history is cleared, because those
  snapshots belong to a workspace that no longer exists. Concurrent callers that all notice the same
  staleness cost **one** reload, not one each.

- **Per-kind generation counters on `workspace_status`** — `Code`, `Project`, `Xaml` and `Resx`, not
  one shared number, so a `.cs` edit does not invalidate a XAML graph and a `.resx` edit invalidates
  nothing Roslyn holds. `workspace_status` grows exactly one line:
  `watch=active gen=c12/p1/x3/r0 pending=0 lastSyncMs=8 gaps=0`. A reload bumps `Code` and `Project`
  only, because it rebuilds the Roslyn solution and says nothing about markup or resources — so a
  `.csproj` save does not invalidate a XAML cache. The counters carry across a reload instead of
  restarting at zero; they answer "changed since I last looked", so a consumer compares them for
  inequality rather than ordering.

- **`--no-watch` and `TERSE_WATCH=0`** turn the watcher off for constrained containers where inotify
  limits make it unreliable; freshness then rests on the per-file stamp check. `terse doctor` reports
  whether this platform supports file watching at all.

- **Undo provenance.** An external change to a file an undo snapshot covers drops that snapshot and
  every snapshot above it, and `undo_last_change` says so — `nothing to undo - 2 snapshot(s) were
  dropped after an external change to src/Foo.cs` — rather than silently reverting someone else's
  work. A reload reports the whole stack as dropped for the same reason.

### Changed

- **The guard names a tool that can actually create a file.** `Write`/`Edit` on a `.cs` path that does
  not exist was denied with a remedy listing `replace_symbol_body`, `replace_symbol`, `add_member` and
  `rename_symbol` — **none of which creates a file**. An agent that needed a new type was left with a
  denial and no legal move, which is exactly how a 0.8.0 session ended up on `edit_text force=true`.
  The denial now names `write_text(path, content, force=true)` for a missing **rooted** path; for a
  relative path, which the hook process cannot resolve against the agent's working directory, it
  offers creation only as the "if it does not exist yet" case, so it never recommends overwriting a
  file that does exist. Every `.cs` **write** denial carries the clause that a file written that way
  is picked up automatically. `find`, `fd`, `ls`, `dir`, `tree`, `wc` and `nl` joined the shell
  text-read list, because `find . -name "*.cs"` walked straight past the guard that `find_files`
  replaces.

- **`write_text` and `edit_text` tell the workspace what they wrote**, and the six file and text tools
  opt out of the sync point: they answer from disk, so forcing a reload before a `read_text` would be
  pure cost.

- **XAML, resx and DI questions are answered from a per-workspace index instead of re-walking and
  re-parsing the whole tree on every call.** Thirteen call sites each did a full recursive scan:
  `xaml_resolve` re-parsed every `.xaml` in the solution to answer about **one** key, `xaml_validate`
  did it to check **one** file, `xaml_styles` to look up **one** type name, and `xaml_localization`
  paid **two** whole-tree walks — one for markup, one for resources — in a single call. The index is
  built once per (kind, generation) and reused until the watcher's per-kind counter moves, so a repeat
  question costs one interlocked read and **zero** file I/O; concurrent callers that all miss share a
  single build rather than one each. When a generation does move, only the files whose
  `(LastWriteTimeUtc, Length)` changed are re-parsed and the rest are carried over: on a 200-file tree
  a one-file edit costs **1 parse instead of 200**. When the watcher is `Off` or `Degraded` the index
  verifies by stamp sweep before answering, which is why `--no-watch` still sees an external change on
  the next call. Any doubt — a watcher gap, an over-cap pending set, a reload in flight — rebuilds from
  scratch rather than guessing. Per-file *records* (keys, names, styles, `x:Uid`s, resource references)
  are always cached; parsed documents live behind a bounded LRU (128 documents or 32 MB of estimated
  document bytes, whichever binds first) because an `XDocument` costs 5-10× its file and caching 1 500
  of them would be a 150-300 MB regression. No tool's response format changed.

- **`workspace_status` reports the index counters** — one more line,
  `index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-) documents=9/128 parses=9` —
  so the hit rate is provable from a status call rather than paid for on every response.

- **The guard names the XAML query tools before `find_files`, and sees PowerShell.** `Glob` or a shell
  walk over a `.xaml`/`.axaml`/`.paml` pattern now names `xaml_find`, `xaml_resolve` and `xaml_styles`
  first, because globbing XAML is nearly always a search for a key, a name or a style rather than a
  question about which files exist; the `.resx` remedies name `resx_find` and `resx_validate` beside
  `resx_files`. `Get-ChildItem`, `gci`, `Get-Content`, `gc`, `Select-String` and `sls` joined the shell
  text-read list — on Windows the fallback is PowerShell, and it walked straight past the guard.

## [0.11.0] - 2026-08-01

### Added

- **Ten `razor_*` tools — Razor and Blazor answered through the compiler.** The Razor compiler is a
  Roslyn source generator, so a loaded workspace already knows the type behind every `<Card />`;
  nothing surfaced it. `razor_outline` prints a `.razor`/`.cshtml` file's directives, its element tree
  with every component resolved to its type, and the members declared in `@code`, each at its
  **Razor** line. `razor_component` answers "how do I use this" from source **or** from a referenced
  package: every `[Parameter]` and `[CascadingParameter]` with its type, which are `[EditorRequired]`,
  and the routes it declares. `razor_find` searches components, elements, attributes, directives,
  expressions and routes. `razor_bindings(validate: true)` resolves every `@bind`, `@on*`, `@ref` and
  `asp-for` against the component's own type and reports `EXACT`, `NO_SETTER`, `UNRESOLVED` or
  `UNRESOLVED_CONTEXT`. `razor_codebehind` links the `.razor` to its `.razor.cs`, `.razor.css`,
  `.razor.js` and its `_Imports` chain.
- **`razor_validate` — the faults the compiler does not catch.** An attribute matching no
  `[Parameter]` compiles clean and throws `InvalidOperationException` at render; two components on one
  `@page` route throw `AmbiguousMatchException` at navigation; an `@inject` nothing registers throws at
  first render. `RZR001`–`RZR010` report those, plus a missing `[EditorRequired]`, a `@bind` with no
  setter, a route parameter with no property, a mistyped `@ref`, an orphan `.razor.css` and markup
  that will not parse — each naming the runtime failure it prevents.
- **`razor_set_attribute`, `razor_add_element`, `razor_remove_element`, `razor_set_directive` —
  compile-gated Razor edits.** An element is addressed by the path `razor_outline` prints or by
  `#ref`, formatting outside the edited span survives byte-for-byte, the result is re-parsed, and the
  **Razor generator re-runs** so an edit that introduces a compile error is rolled back with the error
  at its `.razor` line (~170 ms per regeneration). `dryRun` and `allowErrors` behave exactly as they do
  for C# edits.

- **The C# edit tools reach into `@code`.** `replace_symbol_body`, `replace_symbol`, `delete_symbol`
  and `add_member` now recognise a member whose declaration maps into a `.razor` file, edit the Razor
  source through that mapping, and go through the same regeneration gate. `add_member` on a component
  inserts into its `@code` block, creating one when the file has none.
- **`rename_symbol` renames a component properly.** A Blazor component's class name comes from its
  file name, so renaming the type alone is meaningless: the file, its `.razor.cs`, `.razor.css` and
  `.razor.js` siblings, the partial class inside the code-behind and every `<Card …>` / `</Card>` in
  markup are renamed together, all-or-nothing, with `dryRun` support.

### Fixed

- **Razor answers pointed into `obj/`.** `get_diagnostics` and `analyze` reported a `@code` error at
  `obj/…/Home_razor.g.cs:117` where `dotnet build` says `Home.razor:13`, and `find_usages` reported a
  component used in markup inside the generated file. Both now report the **mapped** location, and no
  response contains a generated `*_razor.g.cs` path — following one meant editing a file the next
  build overwrites. **Response-format change:** locations for Razor-backed symbols now carry the
  `.razor` path and line.
- **`search_symbols` was blind to Blazor components.** Roslyn's source-declaration search skips
  source-generated documents, so `search_symbols Card` returned nothing for a component declared in
  `Card.razor`; components are now listed at their `.razor` path.

### Changed

- **`list_endpoints` includes Razor routes.** Every `@page` template is reported with the component
  it sits in, beside the `Map*` registrations.

- **`workspace_status` reports Razor generator health** — `razor=<n> files generator=ok|unavailable`.
  When the Razor generator does not run (a target SDK newer than the server's Roslyn), Razor
  semantics are reported unavailable rather than silently empty, and `razor_validate` says so as
  `RZR000` instead of reporting component rules it cannot compute.

- **The guard covers Razor.** `.cshtml`, `.razor.css` and `.razor.js` are denied to `Read`/`Edit`
  alongside `.razor`, `Grep type=cshtml` is denied, and the denial names the `razor_*` tool to use
  instead. Plain `.css` and `.js` stay allowed — matching is by extension plus the `.razor.css` /
  `.razor.js` pair. **Behaviour change:** `.cshtml` was previously documented and tested as allowed.

- **The guard intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so

## [0.10.0] - 2026-08-01

### Added

- **`clean` — the `dotnet clean` equivalent, surface 72 → 73.** It deletes the `bin` and `obj` directories of the workspace or of one project and answers with `projects=`, `files=` and `freedBytes=` instead of MSBuild output. Unlike `dotnet clean` it also removes `obj`, which is the case that actually unsticks a stale build, and when the loaded workspace's own MSBuild file locks block the delete it unloads, retries and reloads - the recovery `build` already had, now shared. It refuses any path outside the workspace root, only ever deletes a directory literally named `bin` or `obj`, honours `--read-only`, and `dryRun=true` lists what would go. It is **not** covered by `undo_last_change`, because that history holds Roslyn solutions, not files.

- **`cleanup` applies code fixes: `fix=usings|style|analyzers|all`.** `fix=usings` is the default and is byte-for-byte what `cleanup` did before. `style` applies every `IDE*` code fix, `analyzers` every non-`IDE` one (CA and third-party), `all` both - the in-process equivalent of `dotnet format style` and `dotnet format analyzers`. Fixers come from the project's own analyzer references plus the bundled Roslyn feature assemblies; `ids=` and `severity=` narrow the pass with the same vocabulary as `analyze`, so `analyze` names an id and `cleanup ids=<that id>` fixes it. Every fix goes through the compile gate and is rolled back if it introduces an error, and a diagnostic that no fixer covers - or whose fixer throws or offers nothing - is reported as `UNFIXED <id> x<count> - <reason>` rather than silently skipped.

- **`verify=true` on `format` and `cleanup`.** Replaces `dotnet format --verify-no-changes`: no write, no diff, one verdict line - `clean`, or `VERIFY_FAILED n file(s) would change` followed by the paths. The green case is the common case and now costs a line instead of a diff.

### Changed

- **`format` and `cleanup` take a glob or a directory in `path=`.** A file path still resolves to one document; a path containing `*` or `?` is matched against every document's workspace-relative path, and an existing directory takes everything under it. `path=null` still means the whole solution and an empty `path=""` is still refused with `DocumentNotFound`.

- **`format` and `cleanup` never rewrite generated code.** A whole-solution, glob or directory pass now skips anything under `obj`/`bin` and anything named `*.g.cs`, `*.generated.cs`, `*.Designer.cs`, `AssemblyInfo.cs` or `AssemblyAttributes.cs`. An explicitly named file is still honoured. Rewriting `obj/…GlobalUsings.g.cs` was a real, silent side effect of every whole-solution cleanup.

- **The guard intercepts `dotnet format` and `dotnet clean` as well.** They were allowed because nothing replaced them; `format`, `cleanup fix=…`, `cleanup verify=true` and `clean` now do, so both are denied wherever they appear in a compound command, naming the replacement. `dotnet restore`, `pack`, `publish`, `run` and `tool` stay allowed.

- **`TerseSharp.Core` references `Microsoft.CodeAnalysis.CSharp.Features`.** The SDK ships the IDE code-style analyzers with fixer assemblies that fail to load against the Roslyn version this server runs on (`TypeLoadException` on first use), so the fixers now come from the matching Roslyn feature package instead. This grows the packaged tool.


## [0.9.0] - 2026-08-01

### Added

- **Eight `.resx`/`.resw` localization tools** — the surface goes from 64 to 72. `resx_files` lists every
  resource family with its cultures, entry counts, missing-translation total and designer file;
  `resx_get` prints each key with its value per culture and `MISSING` where a translation is absent
  (`values=false` lists keys only, at a fraction of the cost of reading the file); `resx_find` searches
  key, value or comment across every family; `resx_usages` reports the generated designer property
  resolved through Roslyn as `EXACT` plus `GetString`, localizer indexers, `x:Uid`, `[Display]` and Razor
  literals as `HEURISTIC`, with `composedLookups=N` so "no usages" is never claimed as proof when the
  solution builds keys at runtime; `resx_set` adds or updates one key or a batch of `Key=Value` lines and
  creates a missing culture file from the neutral header; `resx_remove` deletes a key from one culture or
  the whole family and refuses while it is still referenced unless `force=true`; `resx_rename` renames
  across the family and rewrites the references it can prove, all or nothing; `resx_validate` reports
  `RESX001` missing translation, `RESX002` placeholder mismatch (separating the missing-`{n}` case from
  the extra-`{n}` case that makes `string.Format` throw), `RESX003` unused (opt-in, `HEURISTIC`),
  `RESX004` duplicate name, `RESX005` orphan, `RESX006` empty value, `RESX007` whitespace trimmed for
  want of `xml:space`, `RESX008` unsorted and `RESX009` stale designer.
- Every write is **surgical**: only the addressed `<data>` element is rewritten, so the schema header,
  `resheader` rows, entry order, indentation, line endings and byte order mark survive, and a result that
  would not parse is refused before anything is written. Typed and binary entries are reported
  `TYPED`/`BINARY` and passed through untouched. A multi-file edit that fails part way restores the files
  it already wrote.

### Changed

- **`terse guard` covers `.resx` and `.resw`.** A denied read, glob, grep or edit on a resource file now
  names `resx_get`, `resx_find` and `resx_set` instead of the C# tools.
- **`AtomicWrite` preserves the byte order mark of the file it replaces.** Every write went out as UTF-8
  without a BOM, so editing a Visual Studio-written `.resx` or `.xaml` showed a whole-file encoding change
  in git. It now detects the existing preamble and writes the same one; a new file is still BOM-free.
- **`xaml_localization` shares the resource index** instead of carrying its own `.resx` reader, so the two
  cannot drift; its `resourceFiles=` count is unchanged in meaning.
- `SKILL.md` teaches the eight tools, the `RESX00n` rules, and that `resx_*` and `xaml_*` writes are file
  writes and therefore outside `undo_last_change`.
- **The guard also intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so
- **The guard intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so
  the two shell-outs the server most obviously replaces — `build` and `run_tests` — went straight
  through, and the README even documented `dotnet build App.csproj` as an intentional allow. Now
  `dotnet build`, `dotnet test`, `dotnet msbuild`, `dotnet vstest` and bare `msbuild` are denied
  wherever they appear in a compound command, naming the tool that replaces them. `dotnet`
  `restore`, `pack`, `publish`, `run` and `tool` stay allowed: **no TerseSharp tool replaces
  them**, and a denial that cannot name an alternative is a wall rather than a redirect. The shell
  text-read check is also evaluated per command segment now rather than against the whole string.

- **The README and NuGet page document `terse install --guard`.** It shipped in 0.8.0 but the
  enforcement section still described the hook as something you write yourself; both files now give
  the command, a worked example of a denial, and the exact matrix of what the guard denies, what it
  allows (`.css`, `.csv`, `.cshtml`, `.csx` — matching is by file extension, not substring) and why a
  malformed payload allows rather than blocks. Every row was verified against the shipped binary.

## [0.8.0] - 2026-08-01

### Added

- **`explore_symbol` and `impact_of` — one call where three were needed.** Orienting on a symbol meant
  `get_symbol` + `find_usages` + `find_implementations` and assembling the answer by hand;
  `explore_symbol` returns the signature, the XML doc, the location, the usage count split into `src`
  and `test`, the implementation count, the XAML sites and the files it is used in. `impact_of` adds
  the projects that would recompile, so a rename's blast radius is one call instead of three plus
  reasoning.
- **`find_registrations` and `list_endpoints` — the .NET questions grep structurally cannot answer.**
  `AddScoped(typeof(IRepository<>), …)`, a factory delegate or an `AddMyFeature()` extension means the
  concrete type never appears beside the interface, so a text search finds nothing and the agent
  concludes the service is unregistered. `find_registrations` scans the loaded solution's syntax for
  every container call and, when nothing matches, **says that assembly scanning or a container module
  may be responsible** rather than implying the type is unregistered. `list_endpoints` does the same
  for every `Map*` call.
- **`terse guard` and `terse install --guard`.** Every token the server saves on a call the agent never
  makes is zero, and an agent with TerseSharp installed still reaches for `Read`/`Grep` out of habit.
  `terse install --guard` writes a Claude Code `PreToolUse` hook; `terse guard` is the hook itself —
  it reads the payload on stdin and **denies** a built-in on a `.cs`, `.csproj`, `.xaml` or `.axaml`
  path, naming the tool to use instead. It covers the shell too: `grep`, `cat`, `sed` and friends do
  not escape by running in `Bash`. Malformed input allows rather than blocks, so a hook failure can
  never wedge a session.
- **`xaml_styles`** reports every `Style`, `ControlTemplate` and `DataTemplate` that targets an element
  type — keyed and implicit — with the `BasedOn` chain resolved, so "why does this control look like
  that" stops meaning "read `Generic.xaml` and every theme dictionary".
- **`xaml_localization`** joins every `x:Uid` in the workspace to the `.resx`/`.resw` entries that name
  it. A uid with no entry is reported `UNRESOLVED` rather than omitted, so an untranslated element is
  visible instead of silently absent.
- **`xaml_add_element` and `xaml_remove_element`** complete the structured XAML edit surface, addressed
  the same way as `xaml_set_property` and refusing anything that would not parse. Adding to a
  self-closing element is refused with the reason rather than producing invalid markup.
- **`xaml_validate includeUnused=true`** reports `x:Key` and `x:Name` declarations that no XAML
  attribute and no C# string literal references. It is opt-in and tagged `HEURISTIC`, because
  reflection and `FindResource` can reach a declaration no static scan sees.
- **`analyze sinceLast=true`** reports only the diagnostics that appeared since the previous `analyze`
  of the same scope, plus which ones were fixed, so a red→green loop pays for the delta rather than
  re-printing the unchanged set on every iteration.
- Test count: 267 unit and 330 E2E.

## [0.7.0] - 2026-07-31

> **This is a MAJOR change — several tools changed their response format.**
> `get_file_outline` and `get_type_outline` print short member references instead of documentation
> comment ids (`ids=full` restores them); `find_usages` gained a `src`/`test` column and an optional
> `in <Type>.<Member>` one; every mutation and `dryRun` carries `errors=N (+D) warnings=N (+D)`; a
> truncated listing appends `- narrow with <parameter>`; and the XAML tools print workspace-relative
> paths, carry a `dialect=` note, report `HEURISTIC` where `xaml_find` used to claim `EXACT`, and count
> the whole tree in `total` rather than only what they printed.

### Fixed

- **Dialect detection could not fire for Avalonia or MAUI.** `DetectDialect` matched substrings that do
  not occur in either framework's root namespace — `avaloniaui.net` (the documentation site, not the
  markup namespace `https://github.com/avaloniaui`) and `dotnet/maui` (the real one is
  `http://schemas.microsoft.com/dotnet/2021/maui`). Every Avalonia and MAUI file was reported as
  `dialect=wpf`, and so was every WinUI file that did not happen to declare a `Microsoft.UI.Xaml`
  prefix. Detection now matches the real namespaces, treats the UWP/WinUI `using:` prefix form as
  WinUI, and falls back to Avalonia for `.axaml`/`.paml`. No fixture existed for any dialect but WPF,
  which is why no test could fail; there is one per dialect now.
- **`xaml_validate` reported a resource as unresolved when it was declared in another file.**
  Resolution was file-local, so on any real application — where keys live in `App.xaml`,
  `Themes/Generic.xaml` or a chain of `MergedDictionaries` — `XAML003` fired on keys that resolve
  perfectly at runtime. A confident false error is worse than no check: it sends an agent hunting for
  a declaration that exists and invites it to "fix" working markup. `XAML003` now consults every XAML
  file under the workspace root and reports a key only when it is declared nowhere.
- **`xaml_bindings` printed the file name instead of the workspace-relative path**, so two views of
  the same name were indistinguishable. Every XAML record is workspace-relative now, like the rest of
  the surface.
- **`xaml_find` tagged a substring match on an element's type name `EXACT`.** `EXACT` means
  Roslyn-resolved; a text match is `HEURISTIC` and now says so.
- **`xaml_outline` counted elements it did not print.** With a `depth` cut the summary reported the
  whole tree as shown, so `truncated` read `false` on a truncated answer.
- **`xaml_find` aborted on one unreadable file or denied directory.** It walked with a single
  `EnumerateFiles`, the same defect fixed for `search_text` in 0.4.0. Enumeration is isolated per
  directory now, and `bin`, `obj`, `.git` and `node_modules` are pruned during the walk rather than
  filtered afterwards.

### Added

- **`xaml_resolve` — where a resource key actually comes from.** One call reports every declaration of
  an `x:Key` across the workspace with its file, line, type and scope (`local`, `app`, `theme`),
  ordered nearest-first, instead of the agent reading `App.xaml` and each merged dictionary in turn.
  A key declared nowhere says so explicitly rather than answering with an empty list.
- **`xaml_bindings validate=true` — binding paths checked against the real type.** The data context is
  resolved from `x:DataType` (Avalonia, MAUI, WinUI) or `d:DataContext="{d:DesignInstance …}"` (WPF),
  including inheritance from an ancestor element, the XAML prefix is mapped through its
  `clr-namespace:`/`using:` declaration, and each path segment is resolved against the Roslyn symbol —
  nested paths included. A missing member is reported with the nearest member name as a suggestion.
  WPF has no compile-time binding check at all, so this is the only static answer available there.
  When no data context is in scope, or the declared type is not in the solution, the record says
  `UNRESOLVED_CONTEXT` and stays `HEURISTIC` — it never reports an error it cannot prove.
- **`xaml_validate scope=solution`** checks every XAML file in one call and reports how many it read.
- **`xaml_outline filter=named|keyed`** lists only the elements that carry an `x:Name` or an `x:Key`,
  so a large `ResourceDictionary` does not have to be printed in full.
- **`x:Uid` is a first-class citizen.** `xaml_names` reports it alongside `x:Name`, and `xaml_find`
  takes `kind=uid` — the link between XAML and its localization keys was previously invisible.
- **The binding validator refuses to guess.** A path it cannot resolve member by member — `{Binding .}`,
  an indexer, a WPF current-item `/` path, an attached property in parentheses — is reported
  `UNSUPPORTED`, never `ERROR`. Interfaces are searched through `AllInterfaces`, so an interface-typed
  data context does not report every valid binding as missing. A prefixed type name whose `xmlns` does
  not resolve, or whose simple name is ambiguous across the solution, answers `UNRESOLVED_CONTEXT`
  rather than validating against a same-named type from an unrelated namespace.
- **A XAML file that cannot be parsed is never silently dropped.** It would otherwise remove its keys
  from the resource index and make every one of them look unresolved. `xaml_validate` and `xaml_resolve`
  report how many files were unreadable and switch unresolved-resource checking off while any are;
  `scope=solution` reports the unparseable file itself as `XAML000`.
- **The XAML walk does not follow directory junctions or symlinks**, which a self-referential link would
  otherwise turn into an unbounded traversal.
- **`find_usages` names the member each usage sits in, and whether it is production or test code.**
  A record was `path  EXACT  ref  12:5, 40:9` — enough to find the file, never enough to end the
  investigation, so the agent opened the file anyway. It is now
  `path  EXACT  ref  src  12:5, 40:9`, and with `containers=true`
  `path  EXACT  ref  src  in OrderRouter.Route  12:5, 40:9`. The containing declaration comes from the
  document's syntax tree, which is already parsed, so it costs no compilation; the `src`/`test` column
  comes from whether the owning project references a test framework. Naming the member splits the
  answer into one line per member rather than per file, which on a widely-used symbol measured 3× the
  tokens, so it is off by default. This is a response-format change.
- **Every edit reports the diagnostics it leaves behind.** `EditGate` already compiled the changed
  projects and their dependents to decide whether to roll back, then threw the numbers away. Each
  mutation — and each `dryRun` — now carries `errors=N (+D) warnings=N (+D)`, so an agent stops issuing
  a separate `analyze` after every edit, and `dryRun` becomes a real preview. The delta alone is not a
  rollback oracle — one error can disappear while another appears, leaving `(+0)` on an edit that would
  be refused — so a `dryRun` that would be rolled back also says `WARNING … would be rolled back` and
  names the errors it introduces. `allowErrors=true` still skips the analysis and reports no counts;
  it is also the way to get the old cheap diff-only preview back, since the gate now compiles the
  changed projects and their dependents on `dryRun` too.
- **A symbol can be addressed by name, not only by its documentation id.** `M:Trading.OrderService.Submit(Trading.Order)`
  is 60 characters an agent has to reproduce byte-exactly, and one typo cost a whole round trip. Every
  tool that takes a `symbolId` now also accepts `OrderService.Submit`, `Submit`, or
  `OrderService.Submit(Order)` when a parameter count disambiguates an overload. A name that matches
  one symbol resolves; a name that matches several returns `ERROR AmbiguousSymbol` listing their full
  ids — which is the disambiguation call the agent would have had to make anyway — and a name that
  matches nothing names the nearest symbols. Documentation ids keep working exactly as before.
  The qualifier may be as long as you like: `OrderService.Submit`, `Trading.OrderService.Submit` and
  `Fixture.Trading.OrderService.Submit` all resolve, so pasting back an id with the `M:` removed works.
  A name is never resolved by guessing: a qualifier only matches a containing **type** (or a namespace
  when the symbol is itself a type), a parameter list is counted at nesting depth zero so a generic
  argument's comma cannot select the wrong overload, the candidate list declares how many of the total
  it is showing, and a name matching more than 100 symbols is refused outright rather than resolved
  from a truncated search.
- **The token budget suite covers the widest symbol, not only the narrow one.** `find_usages` was
  asserted against a 4-usage fixture symbol, which a format change that tripled the cost on a
  46-usage symbol passed unchanged. There is now a budget on the widest symbol in the fixture and an
  assertion that the default answer costs less than the `containers=true` one.
- **Outlines name members the short way, and the name they print is a name every tool accepts.**
  `get_file_outline` and `get_type_outline` emitted a documentation comment id on every line —
  `M:TerseSharp.Core.ReferenceService.FindUsagesAsync(TerseSharp.Core.LoadedWorkspace,Microsoft.CodeAnalysis.ISymbol,System.Int32,System.Threading.CancellationToken)~System.Threading.Tasks.Task{System.String}`
  is 205 characters against 125 for the signature beside it, so roughly 60% of every member line was
  an id derivable from the rest of the line. They now print `ReferenceService.FindUsagesAsync(LoadedWorkspace, ISymbol, int, CancellationToken)`,
  which resolves back to the same symbol through the name resolution above. The short form is used
  **only where it round-trips**: a constructor, destructor, operator, indexer, explicit interface
  implementation, generic method or member of a generic type keeps its documentation id, because a
  name cannot address those — an E2E test feeds every reference each outline prints back into
  `get_symbol` and asserts none of them errors. `ids=full` prints documentation ids for everything,
  and any other value is refused rather than silently treated as `short`. The outline budget test
  tightened from two thirds of the file it replaces to half. This is a response-format change.
- **A truncated answer names the parameter that narrows it.** `truncated=true, total=412` told an
  agent it was missing results without saying what to do, so the usual next move was to re-run with a
  bigger `maxResults` and pay for the whole list. Every listing tool now appends
  `- narrow with <parameter>` when, and only when, it truncated: `glob=` for text search, `severity=`,
  `ids=` or `path=` for diagnostics, `depth=` or `filter=` for a XAML outline, `kind=` for XAML search.
- **`rename_symbol` rewrites the XAML that names the symbol, and `find_usages` shows it.** Renaming a
  code-behind handler left `Click="OnSubmit"` pointing at a method that no longer exists, and renaming
  a bound property left `{Binding Symbol}` bound to nothing — neither is a compile error in WPF, so the
  compile gate certified a broken UI as clean. Both now travel with the rename, and both appear in
  `find_usages` so the blast radius is visible first. The rewrite happens **only** where an `x:Class`
  or an `x:DataType`/`d:DataContext` proves the reference is to that member; a binding with no declared
  context is listed as `NOT rewritten` rather than rewritten on a guess.
- **`xaml_codebehind`** reports the `x:Class` a file binds to and every event handler it names, with
  the element and event each sits on, instead of reading the `.xaml.cs` to find out what the markup
  wires up.
- **`xaml_set_property`** sets or adds one attribute on one element, addressed by the element path
  `xaml_outline` prints, `#Name` or `key=Key`. It edits the tag in place so the file's formatting
  survives, returns a diff like every other mutation, honours `dryRun` and `--read-only`, and refuses
  an edit whose result would not parse rather than writing broken markup. This replaces line-based
  `Edit` on the file shape agents are measured worst at.
- Test count: 232 unit and 285 E2E.

## [0.6.0] - 2026-07-31

### Fixed

- **Central Package Management was inferred from a file name.** Any `Directory.Packages.props` on the
  way up made `package_add` write the version there and leave the reference version-less — even when
  the file sets `ManagePackageVersionsCentrally` to `false`, or does not set it at all, in which case
  NuGet is not managing versions centrally and a version-less `PackageReference` does not restore.
  The property must now say so, and only the nearest file is consulted, as MSBuild does. The property
  is an ordinary MSBuild property, so the project file and every `Directory.Build.props` up to the
  workspace root are consulted too; a value that is an unresolved MSBuild expression is treated as
  enabled, because writing a version into a CPM project fails the restore with NU1008.
- **`find_implementations` had no result cap.** Every other listing tool declares `truncated`/`total`
  and caps; this one returned every implementation of an interface, which on a wide abstraction is an
  unbounded response. It takes `maxResults` (default 100) like its siblings.
- **A single enormous line could blow the `read_text` response budget** by its own length, because the
  budget was charged after the line was appended. A line that would exceed the remaining budget is now
  truncated with a `(+N chars)` marker.
- **A multi-gigabyte file could exhaust memory during a text search.** `StreamReader.ReadLine`
  materialises one line at a time, which is no protection against a file with no newlines. Content
  search skips files over 16 MB and says how many it skipped; `find_files` still lists them.
- **`PositionFormat.Relative` returned an empty string** for a diagnostic with no file, where the rest
  of the codebase renders `-`.
- **Two more E2E suites leaked a server process each.** The fixture leak fixed in 0.4.0 was fixed only
  in the shared fixture; `CompileGateE2ETests` and `ReadOnlyServerE2ETests` each start their own
  server and still relied on disposing the client alone. All three now go through one
  `TerseServerProcess` helper that owns the process and kills the tree on teardown — including when
  the MCP handshake itself fails, which is the case that used to strand a server holding MSBuild
  locks on the fixtures.

### Changed

- **`undo_last_change` and `unload_workspace` answer with a header line** like every other tool,
  instead of a bare sentence. This is a response-format change; the text of the outcome is unchanged
  and still on its own line.

### Added

- **A positive-path matrix over the whole tool surface.** `ToolHappyPathE2ETests` calls every tool
  with valid arguments and asserts a non-`ERROR` response headed by the tool's own name. Until now the
  robustness sweep only proved that tools *fail* well — a server that answered `ERROR` to everything
  would have passed it. A completeness test forces every advertised tool to be either on the matrix or
  in a named exclusion list, so a new tool cannot arrive untested. Mutating tools run with
  `dryRun: true`. Each case asserts a record only that tool can produce, so a tool that resolves
  nothing and returns an empty body fails; a header alone is not a pass. The four process-spawning
  tools and four whose success path the fixture cannot express are listed explicitly, and a second
  test fails if that list names a tool the server no longer advertises.
- **A read-only sweep.** Every one of the 22 mutating tools is called against a `--read-only` server
  and must answer `ERROR ReadOnly`, so a new mutating tool that forgets its `RejectWrite()` gate is
  caught rather than silently writing.
- **Negative coverage for `build`, `run_tests` and `list_tests`**, which every sweep had excluded:
  a project outside the workspace, and `test` combined with `filter`.
- Test count: 167 unit and 237 E2E.

## [0.5.0] - 2026-07-31

### Fixed

- **`package_add` could write outside the workspace it was given.** With a blank `project` the path
  resolved to the workspace root itself, passed the containment check, and the Central Package
  Management lookup then walked *parent directories without any boundary* until it found a
  `Directory.Packages.props` — in a nested checkout that is the outer repository's file, which it
  edited. Found by the new robustness sweep, which corrupted this repository's own
  `Directory.Packages.props` on its first run. The lookup now stops at the workspace root, a blank
  package id or path is refused, and a sentinel test asserts no tool writes outside the workspace.
- **`solution_add_project` accepted anything.** A blank path added `<Project Path="." />` to the
  solution. A blank path is refused and the target must end in `.csproj`, `.fsproj` or `.vbproj`.
- **`package_list` reported success for a project that does not exist**, answering `0 references`
  instead of `ERROR DocumentNotFound` — an agent would conclude the project had no dependencies.
- **`package_list` and `project_properties` read project files outside the workspace.** Unlike every
  write tool they never went through the containment guard, so
  `package_list(project:"../../../elsewhere/App.csproj")` returned that file's references — and, once
  `package_list` learned to fail on a missing file, became a filesystem-existence probe. Both are
  contained now.
- **A parallel failure could escape as an untyped error.** `Parallel.ForEachAsync` surfaces
  `AggregateException`, which `ToolBoundary` did not recognise, so an expected inner failure would
  have been rethrown instead of rendered. Aggregates are unwrapped and rendered like any other.

### Changed

- **Every reported path is workspace-relative.** `find_usages`, `find_implementations`,
  `search_symbols`, `get_symbol`, `get_symbol_source`, `analyze`, `get_diagnostics` and the dead-code
  findings printed absolute paths, repeating the workspace root on every record. Paths outside the
  workspace are still printed in full. This is a response-format change.
- **`get_file_outline` and `get_type_outline` take `signatures` (default `true`).** With
  `signatures=false` the outline is ids, accessibility and line ranges only — measured on
  `EditGate.cs` at 50% of the raw file against 71% with signatures. The default is unchanged.
- **`build` recovers from its own file locks.** It now runs without holding the workspace lease, and
  when MSB3021/MSB3027 or "being used by another process" appears it unloads the workspace, retries
  the build, reloads, and says so. Symbol ids are unaffected; `undo_last_change` history is
  discarded, which the response states. When the retry is still blocked the response says that too,
  and names the real cause: a running process that owns the file — a server started from the output
  directory being rebuilt — which unloading a workspace cannot release.
  The retry only runs when exactly one workspace is loaded: unloading one of several would let an
  unhinted call silently resolve to the wrong checkout during the rebuild, which is the one failure
  `AmbiguousWorkspace` exists to prevent. A reload that fails is reported rather than swallowed.
- **The advertised tool schema is smaller.** Repeated parameter descriptions were trimmed:
  `tools/list` went from 7,488 to 7,121 tokens — a fixed cost paid on every session.

### Performance

- **The per-project loops run in parallel** in `analyze`, `get_diagnostics`, dead-code analysis,
  `search_symbols` and symbol-id resolution, bounded by processor count. Dead-code analysis also
  parallelises across candidate members, and its outer project loop is sequential so the two levels
  cannot multiply into `ProcessorCount²` concurrent solution-wide searches. Output is unchanged and
  deterministic: results are collected per project and flattened in project order — never in
  completion order — then grouped and sorted before rendering. Four stress tests assert byte-for-byte
  identical answers across repeated runs, verified on this repository's own solution as well as the
  fixture.

### Added

- **A robustness sweep over the whole advertised surface.** `ToolRobustnessE2ETests` reads
  `tools/list` from the running server and calls every tool with garbage arguments, with no
  arguments and with empty strings, asserting each answers a structured response with a `remedy:`
  line, never a stack trace, and that the server is still healthy afterwards. New tools are covered
  automatically. Alongside it, `ToolEdgeCaseE2ETests` (inverted ranges, ranges past EOF, negative
  line numbers, invalid and catastrophic regexes, malformed symbol ids, non-C# files, blank
  arguments, out-of-workspace paths) and `ToolStressE2ETests` (determinism under repetition,
  40 concurrent calls, oversized `maxResults`, a 20,000-character pattern).
- Test count: 144 unit and 159 E2E.

## [0.4.0] - 2026-07-31

### Fixed

- **`read_text` refused every file over 64 KB, including when a line range was asked for.** The size
  check ran before the range was applied, so a 194 KB file answered
  `'…' is 194048 bytes, over the 65536 byte cap` with the remedy `pass startLine and endLine to read
  a range` — advice the caller had already followed. An agent that hit this had no way forward inside
  the server and fell back to reading the file with a built-in tool, which is the one outcome this
  project exists to prevent. The cap is gone: `read_text` streams the file, returns the lines asked
  for, and truncates instead of refusing. It never materialises the whole file, so a multi-gigabyte
  file costs a scan rather than the memory.
- **`analyze` scoped to one file reported findings from other files, all of them generated.**
  `analyze path=src/…/FileGlob.cs` answered with five `CS8019` diagnostics, every one of them in
  `obj/Debug/net10.0/*.g.cs`, and none in the file that was asked about: the dead-code findings never
  received the path filter, and generated output was never excluded. The tool that the "check every
  file you touched" workflow depends on was returning pure noise. Dead-code findings now honour the
  path, and `obj/`, `bin/`, `*.g.cs`, `*.designer.cs`, `AssemblyInfo.cs` and `AssemblyAttributes.cs`
  are excluded from `analyze` and `get_diagnostics` alike — **except at `Error` severity**, where a
  generated file's diagnostic is a real build break and is always reported, and except when the
  generated file is the one named in `path`.
- **`get_file_outline` could not see enums or delegates.** The outline collected
  `TypeDeclarationSyntax` only, so a file declaring nothing but an enum answered `0 types` and an
  agent reasonably concluded the file was empty — then read it with a built-in tool. Enums, their
  members, and delegate declarations are now listed.
- **One unreadable file failed an entire search.** `search_text`, `search_regex` and `find_files`
  walked with a single `EnumerateFiles`, so an `IOException` on one locked file — or a denied
  directory — aborted the whole call. Directory and file enumeration, opening and reading are each
  isolated now; an unreadable entry is skipped and the search completes.
- **A workspace evicted while a call was using it was disposed under that call's feet.** LRU eviction
  and `unload_workspace` disposed the `MSBuildWorkspace` immediately, so an in-flight tool call could
  observe a cleared solution or an `ObjectDisposedException` that carried no error code and no remedy.
  `WorkspaceRegistry.Resolve` now hands out a `WorkspaceLease`; disposal waits for the last lease to
  be released. `ObjectDisposedException` and `OperationCanceledException` are also rendered as proper
  `ERROR` records rather than escaping as untyped failures.
- **The compile gate ignored the projects an edit could break.** `EditGate` compared error counts only
  in the projects holding the changed documents, so changing a public signature broke every dependent
  project while the edit was reported as applied. The gate now also compiles the projects that
  transitively depend on the changed ones.
- **The undo history could interleave.** `TryApply` recorded the previous solution outside the lock
  that guards the history, so two concurrent edits could record the wrong snapshot. Applying and
  recording are one critical section now.
- **The E2E suite left orphaned `terse serve` processes behind**, whose file locks then broke the next
  build. The fixture owns the server process itself and kills the process tree on teardown.

### Changed

- **`find_usages` groups its results per file.** A file with twelve usages was twelve lines, each
  repeating the full path; it is now one line — `path  EXACT  ref  12:5, 40:9, 77:3` — with a separate
  line per distinct confidence and reference kind. This is a response-format change.
- **`read_text` takes `maxLines`** (default 2000) and caps a response at 128 KB of text, reporting the
  cut through the existing `truncated`/`total` fields instead of returning an unbounded file.
- **Search results no longer carry a whole minified line.** A match line over 200 characters is cut
  and annotated with how many characters were dropped.
- **`search_regex` runs on the non-backtracking engine** where the pattern allows it, so a
  catastrophic pattern costs linear time rather than a two-second timeout per line; patterns needing
  backreferences or lookaround fall back to the backtracking engine with that timeout.
- **`get_symbol_source` returns every part of a partial declaration** instead of an arbitrary one.
- **`MsBuildBootstrap` prefers the MSBuild instance matching the running runtime's major version**
  rather than the highest installed, so a preview SDK on the machine no longer breaks workspace load.
- **`build` names a locked output file when it sees one**, pointing at `unload_workspace` instead of
  leaving MSB3021/MSB3027 to be read out of raw build output.

### Performance

- **Dead-code analysis no longer searches the whole solution per member.** `analyze` runs with
  `includeDeadCode` on by default and issued one solution-wide `FindReferencesAsync` for every private
  member — on a solution with thousands of private members, thousands of full-solution searches. A
  private member can only be referenced inside its containing type's declaring documents, so the
  search is scoped to those.
- **Searches no longer walk `.git`, `bin`, `obj` and `node_modules` before discarding them.** The walk
  prunes excluded directories as it descends instead of enumerating everything and filtering after,
  stops on the first NUL byte, and computes each file's relative path once instead of three times.
  `search_text` and `search_regex` additionally skip known-binary extensions without opening them;
  `find_files` still lists those files, because locating a `.png` is not the same as reading one.
- **Resolving a symbol id no longer compiles every project.** `SymbolLookup` narrows to the projects
  whose declaration index contains the name before asking for a compilation, falling back to the full
  set when the name cannot be derived from the id.
- **`DocumentLookup` compares file names before normalising paths**, replacing one `Path.GetFullPath`
  per document in the solution with one per same-named document.
- **Server GC and TieredPGO are enabled** for the server process, which holds Roslyn compilations.

### Fixed — test integrity

- **The token-budget test could not fail.** `get_file_outline` was asserted to cost less than *twice*
  the file it replaces, which passes even if the outline is larger than the file. The assertion is now
  a real budget — two thirds of the file — measured against a body-heavy fixture rather than an
  eighteen-line one. On that fixture the outline costs 261 tokens against 456 for the file: a 43%
  saving, well short of what the fully-qualified ids in every member line could allow.

## [0.3.1] - 2026-07-31

### Fixed

- **`replace_symbol` and `add_member` silently dropped every member after the first.**
  `SyntaxFactory.ParseMemberDeclaration` returns only the first member and reports the rest as
  diagnostics on the node, which were never inspected. A declaration holding four methods replaced one
  and discarded three, answering `replace_symbol applied` with `0 files changed` — an agent that did
  not re-read the file believed the edit had landed. Both tools now refuse a declaration that is not
  exactly one member, with `ERROR InvalidArgument` naming the parse errors.
- **A glob with a directory in it made `find_files` and `search_text` fail.** The glob went straight
  to `Directory.EnumerateFiles` as its `searchPattern`, which rejects `**` and path separators, so
  `**/Views/*.xaml` returned `ERROR InvalidArgument IOException: The filename, directory name, or
  volume label syntax is incorrect` instead of matching. Path-shaped globs are now matched against
  each file's workspace-relative path, with `**/` meaning "any directories or none", `*` and `?`
  confined to one segment; a bare glob such as `*.csproj` still matches on the file name.

### Changed

- **A bare glob now follows glob rules rather than Win32 wildcard rules.** Matching no longer goes
  through `Directory.EnumerateFiles`, so the DOS quirks it inherited are gone: `*.*` matches only
  names that contain a dot (it used to match every file, extensionless ones included), `Order?.cs`
  no longer matches `Order.cs` (`?` now requires exactly one character), and a trailing `.` is
  literal. Common globs — `*.cs`, `*`, `Order*.cs`, `*.c?` — are unaffected.

## [0.3.0] - 2026-07-31

### Added

- **`run_tests` reports statistics.** Every run now carries
  `passed= failed= skipped= total= durationMs= exitCode= elapsedMs=`, on green runs too.
- **`run_tests` selects what to run.** `test=` takes a fully-qualified test name or a class or
  namespace prefix, `filter=` still takes a raw VSTest expression, and passing both is refused with
  `ERROR InvalidArgument`. `noBuild=true` reuses the existing binaries, `includePassed=true` lists
  passing tests, `slowest=N` ranks the slowest, and `timeoutSeconds=` replaces the fixed 10-minute cap.
- **`rerun_failed`** re-runs only the tests that failed in the previous `run_tests` call.
- **`list_tests`** names the tests a project or solution contains without running them, with an
  optional `contains=` substring.

### Fixed

- **The server was unreachable on a large solution.** `serve` loaded the whole workspace before it
  started the stdio transport, so `initialize` went unanswered until the load finished. The MCP
  client cancels `initialize` after a fixed 60 s - which `MCP_TIMEOUT` does not raise - so a
  158-project solution failed to connect with `-32001 Request timed out` while small ones were fine.
  The transport now starts first and the workspace loads in the background: `initialize` answers in
  ~1 s regardless of solution size, and the first tool call that needs the workspace waits for the
  load to finish rather than reporting `WorkspaceNotLoaded`. A preload that fails is reported by
  `list_workspaces` instead of being lost.
- **`run_tests` counted output lines, not tests.** A run with 2 failures reported `5 failures`,
  because the header, the message and the final summary line each matched the failure regex. Counts
  now come from the run's TRX report.
- **`run_tests` dropped everything an agent needs to fix a test.** The exception type and message,
  xunit's `Expected:`/`Actual:` values and the whole stack trace were discarded, leaving
  `Error Message:` with nothing after it. Each failure now reports its message (capped at 12 lines)
  and one workspace-relative `file:line` frame, with framework frames skipped.
- **`run_tests` merged two tests that failed with the same message** into one line, and printed the
  run summary twice.
- **A filter that matched nothing looked like a green run** — `0 failures`, `exitCode=0`. It now says
  `WARNING no test matched filter '<expr>'; this is not a green run`.
- **A run that produced no results still printed a `0 failures` headline.** A missing project or a
  crashed runner now reports `FAILED …, no test results were produced` followed by the output tail.
- **`terse install` honours `CLAUDE_CONFIG_DIR`.** Claude Code reads `$CLAUDE_CONFIG_DIR/.claude.json`
  when that variable is set, so registering into `~/.claude.json` left the server invisible to the
  agent. The skill from `install --skill` follows the same directory (`$CLAUDE_CONFIG_DIR/skills`).
- **`terse doctor` verifies registration, not file existence.** The `clients` line now reports only
  clients whose config actually contains the `terse-sharp` entry, and names the config path it read.
  A config that is not valid JSON is reported as such instead of ending the whole diagnostic.
- **`terse install` with no `--client` no longer exits silently.** A client whose config directory
  does not exist yet is still registered, and a run that matches nothing says `no MCP clients matched`
  rather than printing an empty line.
- **A client config that is not valid JSON is skipped, not overwritten.** `install` and `uninstall`
  report `skipped <client> (not valid JSON: <path>)` and carry on with the other clients instead of
  ending on an unhandled parser exception; `doctor` reports the registered clients and the invalid
  files in the same line.

## [0.2.2] - 2026-07-30

### Changed

- The README and NuGet README license sections just say MIT and link the licence file.

## [0.2.1] - 2026-07-30

### Changed

- **`find_dead_code` is gone; `analyze` reports dead code itself.** One call now returns compiler
  diagnostics, analyzer diagnostics and dead code in a single deduplicated list. Unreferenced private
  members appear as `TERSE001` in category `DeadCode`, alongside the compiler's own unused-field and
  unreachable-code hints, and can be isolated with `ids=TERSE001`. Pass `includeDeadCode=false` to
  skip the reference scan on a very large solution.
- README and the NuGet README are in sync; the keyword blob is gone and the license section reads
  like one.

## [0.2.0] - 2026-07-30

Doubles the tool surface from 26 to 52, all Roslyn-only.

### Added

- **Analysis and cleanup, without any external tool or licence** — `analyze` runs the compiler plus
  every analyzer the project already references, down to `info` and `hidden` severity that a normal
  build hides; `format` applies the Roslyn formatter to your `.editorconfig`; `cleanup` removes
  unused `using` directives, sorts the rest System-first and reformats; `find_dead_code` reports
  unreferenced private members, unused fields and unreachable code.
- **Refactorings** — `extract_interface`, `move_type_to_file`, `move_type_to_namespace`,
  `change_signature`, and `undo_last_change` backed by a 10-deep solution snapshot history.
- **Projects and solutions** — `solution_projects`, `solution_add_project`, `solution_remove_project`
  with full `.slnx` support, `project_create`, `project_properties`, `project_set_property`,
  `project_add_reference`, `project_remove_reference`, and Central-Package-Management-aware
  `package_list` / `package_add` / `package_remove`.
- **XAML** — `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_bindings`, `xaml_validate` and
  `xaml_find`, with WPF, Avalonia, WinUI and MAUI dialect detection. Validation reports duplicate
  `x:Key` and `x:Name` and unresolved `StaticResource` references.
- **Token-budget suite** — the response sizes advertised in the README are now asserted in CI rather
  than estimated.

### Changed

- Debugging and profiling are dropped from the roadmap. A debugger needs a live session and a
  profiler needs a trace host; both are separate products.

## [0.1.1] - 2026-07-30

### Fixed

- The NuGet package README rendered as literal HTML markup on nuget.org. The repository README is
  written with centred HTML for GitHub, which nuget.org's renderer does not support, so the package
  now ships a dedicated pure-Markdown README with absolute links.
- Releases authenticate to nuget.org with **trusted publishing** (GitHub OIDC) rather than a stored
  API key. The release job runs in the `production` environment with `id-token: write`.
- The release action took its tag from `github.ref`, so a `workflow_dispatch` run would have created
  a GitHub release named after the branch instead of the tag. The tag is resolved once for both
  triggers and passed explicitly, which also fixes the prerelease flag on dispatched runs.
- `PathBoundary` compared paths case-insensitively on every platform. On Linux, where the file system
  is case-sensitive, that widened containment: `/repo` would accept a path under `/REPO`. Comparison
  is now ordinal on Linux and case-insensitive elsewhere.
- `SECURITY.md` claimed `--read-only` removes the mutating tools; it refuses them at call time.

## [0.1.0] - 2026-07-30

First release. A Roslyn-backed MCP server that lets a coding agent navigate, read, edit and refactor
a .NET solution semantically instead of reading whole files.

### Added

- **26 MCP tools** over stdio:
  - workspace — `load_workspace`, `workspace_status`, `list_workspaces`, `unload_workspace`, `list_projects`
  - navigation — `search_symbols`, `get_symbol`, `get_file_outline`, `get_type_outline`, `get_symbol_source`, `find_usages`, `find_implementations`
  - diagnostics — `get_diagnostics`
  - editing — `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`
  - files — `read_text`, `write_text`, `edit_text`, `find_files`, `search_text`, `search_regex`
  - build — `build`, `run_tests`
- **Symbol addressing** by Roslyn `DocumentationCommentId`, so edits survive line drift.
- **Multi-workspace registry** with LRU eviction, git worktree and branch awareness, and an explicit
  `AmbiguousWorkspace` error instead of guessing between checkouts of one repo.
- **Compact responses** — one record per line, explicit `truncated`/`total`, `EXACT`/`HEURISTIC`
  confidence tag on every record.
- **Edit safety** — `dryRun`, unified-diff-only responses, rollback when an edit introduces a new
  compile error, `allowErrors` to opt out, workspace-root containment on every path.
- **`terse` global tool** with `serve`, `install`, `uninstall` and `doctor` commands that write MCP
  client configuration directly, plus `install --skill` for the agent skill.
- **75 tests** — 29 unit and 46 E2E, where each E2E test drives a real server process over the real
  stdio transport against a real solution and asserts response values.

### Known gaps

XAML tooling, ReSharper command-line-tools integration, project/solution/package editing, the
content-addressed index, the trigram text index, debug and profiling modules, and the token/latency
benchmark harnesses are specified but not implemented.

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.15.1...HEAD
[0.15.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.15.1
[0.15.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.15.0
[0.14.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.14.0
[0.13.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.13.0
[0.12.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.12.0
[0.11.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.11.0
[0.10.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.10.0
[0.9.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.9.0
[0.8.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.8.0
[0.7.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.7.0
[0.6.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.6.0
[0.5.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.5.0
[0.4.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.4.0
[0.3.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.1
[0.3.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.0
[0.2.2]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.2
[0.2.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.1
[0.2.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.0
[0.1.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.1
[0.1.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.0
- **`package_add` refuses when Central Package Management sits above the workspace root.** Bounding
  the lookup fixed the escape, but left a worse failure available: with the file out of reach the
  tool would have written `<PackageReference Include="X" Version="Y" />` into a CPM-managed project,
  which is an NU1008 build break reported as a successful diff. It now says where the file is and
  what to do instead. Loading the workspace at the repository root restores the normal behaviour.
