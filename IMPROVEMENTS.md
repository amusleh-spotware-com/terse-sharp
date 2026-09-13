# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I536** [accuracy] `history tags=true` answers LOCAL tags only, so the release question "was vX ever pushed?" fell back to `Bash: git ls-remote --tags origin` - this run needed it to discover that 0.59.0 was cut in the changelog but never tagged anywhere | `history` | a `remote=true` flag that shells `git ls-remote --tags` read-only and merges the answer (tag, local yes/no, remote yes/no), or an explicit carve-out note in the guard/skill naming `git ls-remote` as unserved | one Bash call plus one guard-exemption judgement per release run | — |
| **I537** [accuracy] `slowAssembly=<name> Nms/test` fires on every single-test E2E run - server spawn and fixture build dominate one test, so a 5-9 s "per test" number names a healthy suite pathological on every scoped run of this session (5+ occurrences) | `run_tests` | suppress the `slowAssembly` heuristic when a project's executed test count is below ~5, where per-test cost is all fixed overhead | one misleading warning line per scoped E2E run; prevents a false perf lead from being chased | — |
