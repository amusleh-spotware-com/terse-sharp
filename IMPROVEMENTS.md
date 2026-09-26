# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I653** [failures] concurrent E2E servers leave the shared fixture dirty: `RenameEcho.cs` survived its own `finally` delete and `Order.cs` kept a `using System.Text;` after a green run, then pushed `gate solution=true` past its 900-token budget in another collection; isolated reruns stayed clean | `LoadedWorkspace.TryApplyAsync` · E2E fixtures | find which server re-writes an absorbed document after an external delete (memory: TryApplyChanges writes absorbed text) and make it re-read disk instead; add a census assertion that the fixture tree is clean after every collection | one false-red budget test and a manual restore per occurrence | — |
| **I659** [speed] after an idle drop the next `search_symbols` re-realizes every compilation (46 of 84 slow warm calls in the I642 scan carried the realization note), and 38 slow warm calls carried no note at all | `WorkspaceRegistry` · `SymbolSearch` | profile the 38 unexplained slow warm calls, and consider re-warming a dropped workspace in the background only when heap pressure has cleared | up to ~1 h a week of `search_symbols` wall time (I642's measurement) | answering from syntax (I642, refuted: generated declarations and the bound id format need compilations) |
