# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I796** [accuracy] `write_text` on a file a multi-target project compiles updates one target framework's document only; after a write adding `#if NET10_0_OR_GREATER` code, `delete_symbol` answered `SymbolNotFound` until a reload - the single-document twin of the linked-file fix shipped for `delete_symbol symbolIds=` | `write_text` · `edit_text` | set the new text on every linked document of the file, as the batch delete now does | one reload per multi-target write | — |
| **I811** [fallback] `write_text` of a plain `.txt` outside every workspace answers `WorkspaceNotLoaded` while nothing is loaded, yet lands `outside-workspace` once ANY unrelated workspace is loaded; in the folder-rules E2E 3 of 7 agents spent 5-9 calls on `load_workspace` and one fell back to the built-in `Write` | `write_text` | serve an outside-workspace non-code write with no workspace loaded, as `read_text` already does | 5-9 calls and a built-in fallback per occurrence | — |
| **I820** [accuracy] `write_text` refuses a NEW `.cs` file without `force=true` ("C# file(s) no project compiles yet are written only with force", `FileService.Unforced`) and the refusal carries no `retryWith` token, so the retry re-sends the whole content: 1-week scan, 10 such refusals of 31 `write_text` `InvalidArgument`s, `write_text [InvalidArgument] -> write_text` 24 times, at a mean 4 422 input chars per `write_text` call | `write_text` (`FileService.Unforced`) | end the `Unforced` refusal with a `retryWith` token holding the batch, replayed with `force=true` on the entries it named, as compile-gate rollbacks already do; say in the `[Description]` opening clause that a new `.cs` file needs `force=true` | ~10 retries a week re-send nothing, ~11 000 input tokens | dropping the `force=true` requirement for new C# files (it is the deliberate opt-in that a write creates a compiled document) |
