# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I632** [failures] a suite that parses 148 projects needs more than any foreground call can wait for; the client moved 11 calls to background tasks and each cost a `TaskOutput` poll turn; re-measured 2026-09-26 over 176 transcripts: `run_tests` p90 **120 013 ms** and p99 122 730 ms sit on that ceiling, 17 `run_tests` calls answered `RunInFlight`, and `TaskOutput` still cost 24 calls / 0.98 h - the MCP 2026-07-28 release candidate adds `tools/call` task handles driven by `tasks/get`, the protocol form of this row | `run_tests` | a detached mode - `run_tests detach=true` answering a run id at once, and `rerun_failed`/`run_tests status=<id>` reading its verdict | one poll turn per long run, and runs past the client's 120 s foreground limit | — |
