# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I703** [fallback] inspecting the 8 packed `.nupkg` files for I686 (which packages exist, which carry a precompiled `Microsoft.CodeAnalysis.dll`, which `DotnetToolSettings.xml` runner) cost 3 `python zipfile` Bash calls - no tool lists or reads inside a zip/nupkg | `find_files root=` · `read_text` | let `find_files root=<file>.nupkg\|.zip` list the archive's entries with sizes, and `read_text path=<archive>!/<entry>` read one text entry | 3 Bash calls per package inspection (release and packaging work) | — |
