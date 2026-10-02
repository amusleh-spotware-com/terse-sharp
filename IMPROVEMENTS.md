# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I700** [cost] standing offer lines the next call ignores, aggregated below the per-row floor: the `containers=true names …` offer on `search_text`/`search_regex` (296 responses, 23 tokens in both encodings; the next search used `containers=` after **63 of 280**), the complete-listing offers of `get_file_outline` (131, then a re-call with new arguments 7x), `diff_text` (158, 11x), `find_files` (54, 4x), `history` (82, 14x) and `search_text` (118, ~10x), and `read_text`'s `condensed=true - blank lines dropped …` marker (220 responses, 22 tokens) | `TextSearchService.Write` · `ResponseBuilder` offers · `FileService.CondensedMarker` | print each offer and the condensed marker once per tool per server process, the way the batching steer prints once per run; every real `truncated` steer stays on every call | ~11 600 tokens a week for the two tokenizer-measured lines, plus ~540 offer lines a week | dropping `HEURISTIC  text match`: refused in **I169** and **I588**, it is that response's confidence tag |
