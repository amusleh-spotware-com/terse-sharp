# Improvements backlog

Closed rows: [IMPROVEMENTS-ARCHIVE.md](IMPROVEMENTS-ARCHIVE.md).

## Open

| Finding | Tool | Proposed change | Expected saving | Rejected |
|---|---|---|---|---|
| **I935** [correctness] I916 planning probe: `L=/tmp/ci; for f in a; do L=src; cat "$L/README.md"; done` is ALLOWED on 0.84.1 and HEAD, because `Resolved` does not record a keyword-led `do L=src` and `Assigned` trusts the earlier literal | `ToolGuard` (`Assigned`, `Resolved`) | refuse the expanded `(true,false)` verdict when any filled name is `Rebound` in the command, keyword-led assignments included | one in-tree read per loop body that reassigns | — |
| **I936** [round trips] I924 follow-up: an ambiguous `find_usages symbolIds=[...]` entry still answers `NOT_RESOLVED` while the single-id path now expands 2-5 overloads | `find_usages` (`symbolIds=`) | expand an ambiguous batch entry the way `get_symbol_source`'s `AppendOverloadsOrUnresolvedAsync` does | one re-sent id per overloaded batch entry | — |
| **I937** [speed] I929 planning: an `edit_text` that only changes a string literal inside a member-level attribute (a tool `[Description]`) always widens the compile gate to dependents, because `BodyOnly` compares top-level syntax with attributes | `edit_text` · `EditGate.BodyOnly` | narrow to `gate=project` when the only top-level difference is a string literal inside a member attribute that is not `Obsolete`, never an assembly attribute or `InternalsVisibleTo`; prove soundness with a dependent-observing test first | the `dependents` share of the I929 `.cs` edit p90, once the timing lines measure it | — |
| **I938** [correctness] I917 residual: the tail side of `SnippetSearch` (`Tailed`/`Fits`) checks `StartsWith` only, so an anchor ending `bar: 1` should match `  bar: 10` and rewrite only its prefix; a kebab key (`foo-enabled: true`) still matches `enabled: true` mid-line | `edit_text` (`SnippetSearch.Tailed`) | refuse a tail whose last character and the next file character are both identifier characters; leave `-` allowed before a head (fencing it would refuse `a-Call(` code) | one silent wrong-value edit per collision | fencing `-` before a head |
