# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I703** [fallback] inspecting the 8 packed `.nupkg` files for I686 (which packages exist, which carry a precompiled `Microsoft.CodeAnalysis.dll`, which `DotnetToolSettings.xml` runner) cost 3 `python zipfile` Bash calls - no tool lists or reads inside a zip/nupkg | `find_files root=` · `read_text` | let `find_files root=<file>.nupkg\|.zip` list the archive's entries with sizes, and `read_text path=<archive>!/<entry>` read one text entry | 3 Bash calls per package inspection (release and packaging work) | — |
| **I725** [round-trip] `edit_text edits=` with multi-line anchors pasted from a dedented `get_symbol_source` failed 5 of 15 entries (`starts mid-line or sits shallower`), costing 2 extra calls | `edit_text` | re-indent a multi-line anchor per line when every line matches at one common offset | 2 calls per multi-line batch from a symbol read | — |
