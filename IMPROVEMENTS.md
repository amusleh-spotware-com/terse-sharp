# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I895** Code health (`TERSE114`) still lacks CodeScene's history biomarkers - Developer Congestion (many authors on one file) and change coupling (files that change together) - because the edit gate scores syntax only and a `git log` per edited file would add a process spawn to every compile-gated edit | `analyze` / policy | Score them in `analyze` only, from one cached `history` pass per workspace (authors and co-changed files over the last 200 commits, as `find_usages impact=true` already reads), never in the edit gate; report them as a separate `TERSE114` smell line tagged `HEURISTIC` | full CodeScene biomarker parity on `analyze`; zero added latency on edits | per-edit `git log` in the gate - one process spawn per edited file on the hottest path |
