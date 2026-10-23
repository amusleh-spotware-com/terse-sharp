# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I796** [accuracy] `write_text` on a file a multi-target project compiles updates one target framework's document only; after a write adding `#if NET10_0_OR_GREATER` code, `delete_symbol` answered `SymbolNotFound` until a reload - the single-document twin of the linked-file fix shipped for `delete_symbol symbolIds=` | `write_text` · `edit_text` | set the new text on every linked document of the file, as the batch delete now does | one reload per multi-target write | — |
