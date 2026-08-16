---
description: Re-derive the MCP tool surface from source, bring every repo document back in sync with it, verify with the census gates, then commit and push. No review subagent.
argument-hint: "[optional: a document to check first, e.g. SKILL.md]"
---

# 🚫 HARD GATE — the docs are part of the tool surface. Sync them from the code, not from memory.

`README.md` is what a user reads before installing, `NUGET_README.md` is what nuget.org renders, and
`src/TerseSharp.Server/Assets/SKILL.md` is an **embedded resource shipped by `terse install --skill`
and loaded straight into an agent's context** — a tool it does not name might as well not exist, and
a response format it describes the old way is **worse than no skill**. This command makes all three,
plus `CLAUDE.md` and the rest, true again.

`$ARGUMENTS` — if it names a document, check that one first; every other document is still checked.
Nothing else takes input from the user.

**Banned for the whole run:**

- `AskUserQuestion`, `ExitPlanMode`, "shall I continue?", handing work back. Autonomous from P0 to P8.
- **Spawning any subagent.** No `Agent` call, no `Workflow`, no `code-review-gate` R2a reviewer, no
  `caveman:*` reviewer. This run's deliverable is documents; the arbiter is the census tests in P6
  plus the written self-check in P7. That is a deliberate, user-stated exception to the standing
  reviewer rule, and it holds **only** while the change set is documents.
- **Editing any `.cs`, `.csproj`, `.props`, `.targets`, `.slnx`, `.resx`, `.xaml` or `.razor` file.**
  When a document and the code disagree, the **code wins and the document changes**. If the code is
  what is wrong, log it as a row in `IMPROVEMENTS.md` `## Open` (never in the archive) and say so in P8 — do not fix it
  here. A code edit in this run voids the no-reviewer exception above.
- `Read` / `Grep` / `Glob` / `Edit` / `Write`, and `Bash: git status`, `git diff`, `grep`, `rg`,
  `cat`, `head`, `tail`, `sed`, `awk`, `ls`, `find`, `dotnet build`, `dotnet test`. All of those are
  terse-sharp calls. Only git **history**, index/history mutation (`add`, `commit`, `push`) and `gh`
  stay on `Bash`.
- Answering from the **connected** `terse` MCP server about how a tool behaves. It is whatever
  `dotnet tool install/update` last put on PATH — not `HEAD`. Three sessions have been spent arguing
  with docs written against a three-release-old binary. Evidence order: an E2E test against the
  freshly built `terse.dll` > a hand-run `dotnet run --project src/TerseSharp.Server -- serve …` >
  current source read with `get_symbol_source`. **Say which one answered.**
- `git add -A` / `git commit -am`. Stage by path — this tree is shared with other sessions and with
  `.claude/worktrees/agent-*`.
- A `Co-Authored-By:` trailer.
- Rewriting a document from memory. Every edit is `edit_text section="## Heading"` or an `oldText`
  taken from a **read in this run** — a remembered anchor is the 102-`InvalidArgument` trap.

---

## P0 — Preflight

1. Invoke the `terse-sharp` skill via the Skill tool.
2. `workspace_status`; `load_workspace TerseSharp.slnx` if needed. Pass `workspace: "TerseSharp"` on
   every call from here.
3. `changed_files`. Record every path already dirty — another session's work, never staged in P7.
4. `Bash: git fetch origin && git pull --ff-only` on `main`.
5. `TaskCreate` one task per phase and one per document in the P2 inventory.

---

## P1 — Derive the surface from source (the single source of truth)

1. `search_regex` over `src/TerseSharp.Server/Tools/*.cs` for `\[McpServerTool\(Name = "([a-z_]+)"`
   → the **authoritative tool list and count**. Use `maxResults=1` when only the count is wanted; the
   count line answers it without 86 records.
2. For every tool whose document text you are about to touch, `get_symbol_source` the tool method to
   read its real signature: **parameter names, types and C# defaults**, and its `[Description]`.
   A documented parameter that has no C# default is a bug in the *code* (the MCP SDK marks it
   required) — log it, do not fix it here.
3. `get_symbol_source` on the response builder / service for any **format** the docs quote: the
   success one-liner shape, the `N unit` vs `N/T unit truncated - narrow with X` summary, the
   `ERROR <Code>: …` + `remedy:` shape, the `EXACT`/`HEURISTIC` tag, `errors=N (+D) warnings=N (+D)`.
   A quoted response in a document is a **claim** — it is either copied from a verified call or it
   is deleted.
4. Read the census sets that the documents restate: `ToolCensus` exemptions and their ratchets,
   `ToolCoverageE2ETests.Exercised`, the `TokenBudgetE2ETests` budgets, `DocsCoverageE2ETests`.
5. Write the **Surface Ledger** for this run: tool count, tools added/removed/renamed since the docs
   were last synced (`Bash: git log --oneline -- src/TerseSharp.Server/Tools` for the window),
   changed parameters, changed defaults, changed response formats, new census gates.

---

## P2 — Document inventory

**In scope — every one is checked, every run:**

| Document | What must be true |
|---|---|
| `README.md` | tool **count**; the tool table (every tool, correct group); the "what each one replaces" table; the numbers/measurements table; the Status table (a shipped row is out of 🔜); the architecture diagram's tool count and service list; the paste-ready hard-gate block; every `bash` example still valid |
| `NUGET_README.md` | the same, in **pure Markdown** — nuget.org does not render the GitHub README's HTML. It is a separate file and diverges silently; never assume the README edit covered it |
| `src/TerseSharp.Server/Assets/SKILL.md` | **every** tool named in the surface-by-job list; the swap table, working rules and hard gate describing behaviour **as it is now**; parameter names and defaults correct; the git trio (`changed_files` / `diff_symbols` / `diff_text`) stated as replacing `git status` / `git diff`; response-format examples matching P1.3 |
| `CLAUDE.md` | tool count; the Core service list; the request-pipeline description (`ToolContext` entry points, `ToolBoundary`, `ToolArgumentFilter`); the census-gate table (**exhaustive** — a gate added since the last sync must appear); the "Adding or changing a tool" checklist; the traps list; Definition of done |
| `CHANGELOG.md` | a `## [Unreleased]` entry for anything this run discovered was undocumented; every `## [x.y.z]` heading has a link definition and `[Unreleased]` compares against the newest tag |
| `CONTRIBUTING.md` | the build/test/format commands still exist and still pass; the add-a-tool steps match `CLAUDE.md` |
| `RELEASING.md` | the workflow file names, job steps and nuget trusted-publishing policy fields match `.github/workflows/release.yml` |
| `SECURITY.md` | supported-version line matches the newest released tag |
| `IMPROVEMENTS.md` | **adding rows only** — a doc defect this run found and did not fix becomes a `## Open` row in `IMPROVEMENTS.md`. Closing a row, and therefore writing to the archive, is `/ship-improvements`' territory |
| `fixtures/*/README.md` | still describes what the fixture actually contains |
| `.claude/commands/*.md` | still name tools and phases that exist |

**Out of scope — never edited, never staged:** untracked working notes at the repo root
(`terse-sharp-token-savings-*.md`, `analyzer-assembly-lock-plan.md`, `sharp-mcp-*.md`), `.research/`,
`.serena/`, `.claude/worktrees/**`.

---

## P3 — Diff each document against the Surface Ledger

Per document, in inventory order:

1. `read_text <doc> headings=true` for the section map — never a whole-file read when a section
   answers it.
2. `read_text <doc> section="## …"` for each section the Ledger touches.
3. Produce, in writing, a **verdict per row of the Ledger**: `CORRECT` / `STALE` / `MISSING` /
   `UNVERIFIABLE`. A verdict of `UNVERIFIABLE` means the document makes a claim nothing in P1 could
   confirm — that claim is deleted or replaced with one that can be proven. **Never answer something
   you cannot prove** is the rule the reviews keep enforcing, and it binds the prose too.
4. Check the four silent-drift classes the past syncs actually hit:
   - a tool named in README but **not** in NUGET_README or SKILL.md (they diverge independently);
   - a **count** — "86 tools", "83-tool surface", "four census gates" — restated in more places than
     anyone remembers;
   - a **response format** described the old way after a framing change;
   - a rule stated as enforced that has **no census gate** (`CLAUDE.md` forbids exactly that: a rule
     with no census gate is a suggestion — if the gate is missing, log the gap, and say the rule is
     unenforced rather than implying it is enforced).

---

## P4 — Edit

1. `edit_text <doc> section="## Heading"` replaces a whole section with no `oldText` at all — the
   preferred form. Otherwise `oldText` copied from the P3 read, long enough to be unique.
2. `write_text` only for a full-file rewrite, and **only from a read taken in this run** — a
   stale-read overwrite silently reverts whatever landed since.
3. Preserve each document's own voice and structure. This is a **sync, not a rewrite**: do not
   reorganize a document that was merely stale, do not delete a measurement, a refuted approach or a
   recorded decision, and do not "tidy" a section the Ledger did not touch.
4. Keep the two READMEs in step. Every edit to a shared claim is applied to both, in the same pass,
   with `NUGET_README.md` re-rendered as pure Markdown.
5. `CHANGELOG.md` gets a `## [Unreleased]` entry **only** when this run documented behaviour that was
   previously undocumented, or corrected a documented format. A pure typo sweep is not a changelog
   entry.

---

## P5 — Re-read whole, once

For `README.md`, `NUGET_README.md`, `SKILL.md` and `CLAUDE.md`, read the **whole** file after the
edits and check nothing else it claims has quietly become false. This is the step `CLAUDE.md`'s docs
gate names explicitly for `SKILL.md`, and it is where cross-section contradictions surface — a count
fixed in the table and stale in the intro paragraph.

---

## P6 — Verify with the gates

1. `build` — `SKILL.md` is an **embedded resource**, so a docs run does change the assembly. Read the
   result before anything else.
2. `run_tests` over the whole solution. The gates that own this command's output are
   `DocsCoverageE2ETests` (every advertised tool named in README, NUGET_README, SKILL.md — read from
   `tools/list` of the freshly built server), `ToolCoverageE2ETests`, `ToolCensusE2ETests`,
   `SchemaCensusE2ETests`, `InstallCommandE2ETests` (the skill asset it installs). A full green is the
   requirement; a filtered run is not this gate.
3. `cleanup verify=true fix=all` and `format verify=true`. A markdown-only change should move
   neither; if one moves, something in step 1 changed more than documents — stop and re-check the
   change set.
4. Red at any step → fix the **document** (or, if the code is at fault, revert your document change to
   what the code says and log the row), then return to step 1.

---

## P7 — Self-check, then commit and push

**No reviewer subagent is spawned.** Instead, answer these in writing — this is the substitute, and
P8 must say it was run:

1. Does every document name every tool `tools/list` advertises, and no tool it does not? (P6.2 proves
   the first half; state how you checked the second.)
2. Is every count in every document the same number, and is that number the one P1.1 derived?
3. Is every response format quoted in a document copied from something verified in P1.3?
4. Is every rule stated as enforced backed by a named census gate?
5. Did this run touch **only** documents? `changed_files` is the answer, not memory.
6. Is anything staged that was already dirty in P0.3? It must not be.

Then:

- `changed_files`, stage **by path**, only the documents this run edited.
- `Bash: git commit -m "docs: sync … "` — subject naming the surface change that drove it, body
  listing each document and what changed. **No `Co-Authored-By`.**
- `Bash: git show --stat HEAD` — confirm the commit contains what is claimed.
- `Bash: git push origin main`.

---

## P8 — Confirm CI, then report

1. `Bash: git rev-parse HEAD`; find the run with
   `gh run list --workflow=ci.yml --branch main --limit 5 --json databaseId,headSha,status,conclusion,url`.
2. Poll `gh run view "$RUN_ID" --json status,conclusion,url` every 20 s, capped at 20 minutes —
   a live check, never a blind `sleep`, and never idling between polls. A docs commit that reds `main`
   is fixed in this run, not left: `gh run view --log-failed`, fix, re-run P6, commit by path, push,
   re-check.
3. **Report**, in one message:

| Section | Content |
|---|---|
| Surface Ledger | tool count, and every tool / parameter / default / format that changed since the last sync |
| Per document | verdicts — `CORRECT` / `STALE` (and what was fixed) / `MISSING` (and what was added) / `UNVERIFIABLE` (and what was deleted) |
| Evidence | which of the three sources answered each behavioural claim: freshly-built E2E, hand-run server, or `get_symbol_source` |
| Gates | `build`, full `run_tests`, `DocsCoverageE2ETests`, `cleanup verify`, `format verify` |
| Self-check | the six answers from P7 |
| Code defects found | every code-side row logged to `IMPROVEMENTS.md ## Open` and **not** fixed here |
| Commit | SHA, the paths staged, and the CI run URL and conclusion |
| DEGRADED | any phase that could not run in full, the substitute, and why |
