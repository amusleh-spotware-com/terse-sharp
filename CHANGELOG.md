# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

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

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.4.0
[0.3.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.1
[0.3.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.0
[0.2.2]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.2
[0.2.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.1
[0.2.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.0
[0.1.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.1
[0.1.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.0
