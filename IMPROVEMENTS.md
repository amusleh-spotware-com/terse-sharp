# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I770** [correctness] MSBuildWorkspace evaluates every project at its own default Configuration and Platform and ignores the solution's per-project mapping: a project the slnx builds as Release x64, with `TreatWarningsAsErrors` only under Debug AnyCPU, answered `get_diagnostics` `CS4014 error` while `build` answered `warning CS4014` (scratch repro of I764, project P2) | `get_diagnostics` · edit `errors=` · `load_workspace` | read the solution's per-project configuration mapping at load; when it differs from the project's evaluated Configuration and Platform, say so on `load_workspace` and as a note on `get_diagnostics` and the `errors=` counter; later, load mismatched projects with matching global properties | severities that match the build on solutions with x64 or Release project mappings | — |
