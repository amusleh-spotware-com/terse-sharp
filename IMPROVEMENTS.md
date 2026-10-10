# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I740** [quality] `edit_text force=true` on `.cs` bypasses the compile gate, and it is now the second most common C# edit path: 2 654 of 6 759 C#-source edits (39%) across 30 days / 628 transcripts. Builds preceded only by ungated `edit_text` edits came back red with a `CS` error 8.7% of the time (17/196) against 2.3% (3/130) after only compile-gated edits | `edit_text` | run a `force=true` edit of a workspace `.cs` document through `EditGate` (rollback on a new compile error, the same `errors=N (+D)` line), keeping `allowErrors=true` as the raw text path | ~17 red builds a month, each a ~30 s `build` plus a fix round trip; the gated path already rolled back 393 bad edits in the same window | — |
