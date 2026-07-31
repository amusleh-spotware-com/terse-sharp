# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

### Fixed

- **A glob with a directory in it made `find_files` and `search_text` fail.** The glob went straight
  to `Directory.EnumerateFiles` as its `searchPattern`, which rejects `**` and path separators, so
  `**/Views/*.xaml` returned `ERROR InvalidArgument IOException: The filename, directory name, or
  volume label syntax is incorrect` instead of matching. Path-shaped globs are now matched against
  each file's workspace-relative path, with `**/` meaning "any directories or none", `*` and `?`
  confined to one segment; a bare glob such as `*.csproj` still matches on the file name.

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

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.0
[0.2.2]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.2
[0.2.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.1
[0.2.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.0
[0.1.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.1
[0.1.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.0
