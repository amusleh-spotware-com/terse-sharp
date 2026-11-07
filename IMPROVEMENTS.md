# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I865** [speed] one `analyze` call in a large employer solution ran **25 267 s (7.0 h)** in the window - 82% of all `analyze` wall time (8.6 h over 290 calls; the other 174 calls in that project p50 18.1 s) - and no deadline exists on the analyze path (`search_text Timeout\|CancellationTokenSource` over `AnalysisService.cs` and `AnalysisTools.cs`: 0 hits) | `analyze` | give `analyze` a `timeoutSeconds` (default 600) and answer the projects finished by the deadline plus `next: analyze paths=[...]` for the rest, marked partial, instead of running unbounded | up to 7.0 h of one session's wall clock per hang; cause of the hang itself unmeasured (lever c) | — |
| **I868** [round trips] 20 refusals a week reject a whole batch that could have been answered in part or combined: `analyze` over 10 paths 6, `get_file_outline` over 10 paths 4, `read_text ref=` with `paths=` 6, `get_symbol_source ref=` with `symbolIds=` 4 | `analyze` · `get_file_outline` · `read_text` · `get_symbol_source` | over the cap, answer the first 10 and end with `next:` naming the rest verbatim; let `ref=` combine with `paths=` / `symbolIds=` (every entry read at the one ref) | 20 refusals + 20 retries a week | dropping entries silently - the overflow is named in `next:`, never lost |
