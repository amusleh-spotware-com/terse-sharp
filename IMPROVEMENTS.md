# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I610** [latency] field report on a 148-project WPF solution: two symbol queries issued while a test build ran sat 3-4 minutes each, and the workspace was unloaded and reloaded twice for MSBuild locks; each reload threw the realized compilations away, so the next query paid realization again (47 s, against 119 s for the first) while competing with the build for CPU | `build` · `run_tests` · `WorkspaceRegistry` | keep the last realized `Solution` across the lock-retry unload/reload and answer read-only symbol queries from it tagged `snapshot=pre-build` until the reload finishes, or skip the unload when no holder the build names is this process | 47-119 s per symbol query issued during a build on a large solution | answering from a stale snapshot WITHOUT saying so: a confident answer about code the build may have changed is the defect this server exists to prevent |
| **I632** [failures] a suite that parses 148 projects needs more than any foreground call can wait for; the client moved 11 calls to background tasks and each cost a `TaskOutput` poll turn; re-measured 2026-09-26 over 176 transcripts: `run_tests` p90 **120 013 ms** and p99 122 730 ms sit on that ceiling, 17 `run_tests` calls answered `RunInFlight`, and `TaskOutput` still cost 24 calls / 0.98 h - the MCP 2026-07-28 release candidate adds `tools/call` task handles driven by `tasks/get`, the protocol form of this row | `run_tests` | a detached mode - `run_tests detach=true` answering a run id at once, and `rerun_failed`/`run_tests status=<id>` reading its verdict | one poll turn per long run, and runs past the client's 120 s foreground limit | — |
