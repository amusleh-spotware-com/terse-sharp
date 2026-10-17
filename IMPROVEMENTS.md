# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I762** [speed] `terse guard --folder-rules` (I761) runs as a `PostToolUse` hook on every terse-sharp call: 6 runs of the Debug `terse.dll` on Windows took 234-265 ms (median ~250 ms) against 204-235 ms for the existing `PreToolUse` guard - all process start, the hook's own work is a few file probes | `terse guard` | give the `guard` entry a start-up path that skips System.CommandLine and host construction (dispatch on `args[0]` before `RootCommand`), and measure ReadyToRun for the tool package; the same path speeds every existing `PreToolUse` call | ~150 ms of the ~250 ms per terse call - at 48 calls a task, ~7 s a task | — |
| **I763** [failure] A harness `TaskStop` of a backgrounded `run_tests` stops the client's wait but not the server's run: the next `run_tests` answered `RunInFlight ... started 734.0s ago` and the suite could not be re-run until the stale one drained | `run_tests` · `ToolContext` | honour the MCP `notifications/cancelled` for the build and test family by killing the child process tree, and have `RunInFlight` name `run_tests status=` plus how to cancel it | the remainder of a stale whole-solution run (here 12+ min) each time a run is abandoned | — |
| **I770** [correctness] MSBuildWorkspace evaluates every project at its own default Configuration and Platform and ignores the solution's per-project mapping: a project the slnx builds as Release x64, with `TreatWarningsAsErrors` only under Debug AnyCPU, answered `get_diagnostics` `CS4014 error` while `build` answered `warning CS4014` (scratch repro of I764, project P2) | `get_diagnostics` · edit `errors=` · `load_workspace` | read the solution's per-project configuration mapping at load; when it differs from the project's evaluated Configuration and Platform, say so on `load_workspace` and as a note on `get_diagnostics` and the `errors=` counter; later, load mismatched projects with matching global properties | severities that match the build on solutions with x64 or Release project mappings | — |
