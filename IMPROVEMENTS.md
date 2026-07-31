# Improvements backlog

One line per finding from the end-of-task tool-usage review: observed cost, the tool, the proposed
change, the expected saving. Shipped entries keep their measurement so a regression is visible.

## Shipped

| Finding | Tool | Change | Measured |
|---|---|---|---|
| The documentation id was 205 of the 336 characters on a member line, all of it derivable from the signature beside it | `get_file_outline`, `get_type_outline` | print `OrderService.Submit(Order)`; keep the id only where a name cannot address the member | outline budget tightened from ⅔ to ½ of the file it replaces; `OrderBook.cs` 261 → 167 tokens |
| Reproducing a 205-character id byte-exactly cost a round trip per typo | every `symbolId` parameter | accept names, qualified names and parameter lists; list candidates instead of guessing | one failed call removed per typo |
| `find_usages` gave the file but not the member, so an agent opened the file anyway | `find_usages` | add `src`/`test` and optional `in <Type>.<Member>` | container is opt-in because it measured 3× on a 46-usage symbol |
| `analyze` after every edit re-derived what `EditGate` had just computed | every mutating tool | report `errors=N (+D) warnings=N (+D)` on apply and `dryRun` | one `analyze` call removed per edit |
| `truncated=true` said nothing about what to do, so the retry widened `maxResults` and paid for the whole list | every listing tool | append `- narrow with <parameter>` | one wide re-query removed per truncation |
| Orienting on a symbol cost `get_symbol` + `find_usages` + `find_implementations` | `explore_symbol` | one call returns signature, doc, reach, implementations, XAML sites | 3 calls → 1 |
| Judging a rename's blast radius cost `find_usages` + `find_implementations` + manual project reasoning | `impact_of` | one call adds the projects that would recompile | 3+ calls → 1 |
| "Where is `IFoo` registered?" was unanswerable — open generics, factories and `Add*` extensions defeat grep | `find_registrations` | syntactic scan over the loaded solution, and an explicit "declared nowhere" answer | replaces a multi-file hunt that often failed outright |
| "What endpoints exist?" meant grepping `Program.cs` and every extension it calls | `list_endpoints` | every `Map*` registration with its member | replaces a multi-file hunt |
| Resolving one `{StaticResource}` meant reading `App.xaml` and every merged dictionary in order | `xaml_resolve` | workspace-wide key index with scope | ~8-15k tokens → ~100 |
| A `{Binding}` typo had no static answer in WPF at all | `xaml_bindings validate=true` | resolve the data context and walk the path against Roslyn | a class of runtime-only bug becomes a static one |
| Editing XAML meant line-based `Edit` on the file shape agents are measured worst at | `xaml_set_property`, `xaml_add_element`, `xaml_remove_element` | element-addressed edits, formatting preserved, malformed results refused | removes the last routine reason to `Read`+`Edit` a `.xaml` |
| A red→green loop re-printed every unchanged diagnostic on each iteration | `analyze sinceLast=true` | report only what appeared, plus what was fixed | the loop pays for the delta, not the set |
| An agent with the server installed still reached for `Read`/`Grep` out of habit | `terse guard`, `terse install --guard` | a `PreToolUse` hook that denies the built-in and names the replacement | closes the only failure that scales with every session |

## Open

| Finding | Tool | Proposal | Why not yet |
|---|---|---|---|
| **I1** (2026-07-31) No heading-level outline for a non-`.cs` file: locating two sections of `CLAUDE.md` to edit required pulling the whole file — 216 lines (~2.6k tokens) read to change 3 spots, where `search_text "## "` returned the same map in 39 | `read_text` | a `headings: true` mode, or extend `get_file_outline` to Markdown, so the agent reads the section it edits | Still open. Carried forward from the 2026-07-31 review; this release spent its budget on the C# and XAML surface. |
| **I2** (2026-07-31) `edit_text` needs a byte-exact unique `oldText`, forcing a prior full read of the region even when the target is addressable — 1 extra read per edit on every doc change | `edit_text` | section-addressed replacement for Markdown (`section: "## Commands"`), the way `replace_symbol_body` addresses C# | Still open, and felt repeatedly in this task: every doc edit in this release paid I2. |
| A generic `detail=` verbosity knob was proposed for every tool | all listings | one parameter instead of `signatures`, `ids`, `containers`, `filter` | **Rejected for now.** Those four already give per-tool control with names that say what they do; a generic `detail` would duplicate them and make each tool's cheapest form less discoverable. The repo rule is that improving an existing parameter beats adding a surface. |
| Implicit styles (`Style` with a `TargetType` and no key) and `BasedOn` chains are not resolved | `xaml_resolve` | follow `BasedOn` and index `TargetType` so "why does this button look like that" is one call | Needs a resolution order model per dialect; a wrong winner would be a confident wrong answer, which the response contract forbids. Keyed styles already resolve. |
| `x:Uid` is reported but not linked to its `.resx` entry | `xaml_names`, a future `xaml_localization` | join `x:Uid` to the resource entries that define it | Needs `.resw`/`.resx` parsing and per-dialect naming conventions; `xaml_find kind=uid` covers locating one today. |
| Every subagent spawns its own server process and its own Roslyn workspace | `terse serve --shared` | one warm daemon over a local socket, N clients | Architectural: needs a transport, a lifetime owner and a lock story. Measured elsewhere at ~100 MB and ~10 s per extra process; worth doing, too large to land safely inside a feature release. |
| `xaml_add_element` inserts as the last child only | `xaml_add_element` | `position=first\|before\|after` | The common case is append; anything else is a `dryRun` away from `edit_text` today. |
| `find_registrations` matches container calls syntactically, so a registration wrapped in a helper method is reported at the helper, not the call site | `find_registrations` | follow one level of `Add*` extension methods | Following the chain arbitrarily becomes whole-program analysis; the current answer is honest about what it matched. |
