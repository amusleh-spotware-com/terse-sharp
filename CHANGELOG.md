# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

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

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.1
[0.1.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.0
