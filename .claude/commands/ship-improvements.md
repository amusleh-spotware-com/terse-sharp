---
description: Implement every open row in IMPROVEMENTS.md, close them, update the docs, verify, commit, push, wait for CI green, cut a release, and wait until the version is live on NuGet.
argument-hint: "[optional: explicit version, e.g. 0.24.0 — otherwise derived]"
---

# 🚫 HARD GATE — run every phase, in order, to the end. No asking, no stopping, no partial run.

`$ARGUMENTS` — if it parses as `X.Y.Z`, that is the release version; otherwise it is ignored and the
version is **derived** in P6. Nothing else in this command takes input from the user.

**Banned for the whole run, without exception:**

- `AskUserQuestion`, `ExitPlanMode`, "shall I continue?", "let me know if you want me to…", a summary
  that hands work back. This command is autonomous from P0 to P11.
- Stopping at the first failure. A red build, a red test, a red CI leg, a rejected edit, a failed
  release job is **an input to the next step**, not an exit. Diagnose, fix, re-verify, continue.
- Declaring a phase done that was not run. If a phase genuinely cannot execute, run the strongest
  substitute, **say so in the final report as DEGRADED with the reason**, and continue.
- Skipping a row in `## Open` because it looks hard, vague or unmeasurable. Every row leaves the
  `## Open` table in this run — either **shipped** (moved to the shipped table with its measurement)
  or **closed as a measured decision** (moved to `## Known limitations` with the evidence that
  closes it). Deleting a row, or leaving it open, is a failed run.
- `Read` / `Grep` / `Glob` / `Edit` / `Write` on anything under this repo, and `Bash: git status`,
  `git diff`, `git diff --stat`, `grep`, `rg`, `cat`, `head`, `tail`, `sed`, `awk`, `ls`, `find`,
  `dotnet build`, `dotnet test`, `dotnet clean`, `dotnet format` (one carve-out, P4.6).
  Every one of those is a `terse-sharp` call — `CLAUDE.md`'s develop-TerseSharp gate is in force for
  the entire run. Only git **history** (`log`, `blame`, `show <ref>:<path>`), index/history mutation
  (`add`, `commit`, `tag`, `push`), `gh`, and the NuGet probe stay on `Bash`.
- A blind wait. Every wait on an external process detects the process **dying**, not only the
  artifact appearing (P8, P10, P11 give the exact shape).
- `git add -A` / `git commit -am`. Stage by path. This tree is shared with other sessions and with
  `.claude/worktrees/agent-*`.
- A `Co-Authored-By:` trailer in any commit message.
- Weakening, skipping or deleting a test to make a suite go green.
- **Spawning the review agent, committing, tagging, pushing or releasing while any row is still in
  `## Open`.** See the gate directly below — it is the one gate this command cannot trade away.

---

# 🚫 HARD GATE — no review, no commit, no push, no release until `## Open` is empty

P1 finishing every open row is a **precondition** of P7–P10, not a phase that runs alongside them.
Before `code-review-gate`, before the R2a reviewer spawn, before `git add`, before `git commit`,
before `git tag`, before `git push`, and before every retry of those after a CI failure, answer:

> **"Does `read_text IMPROVEMENTS.md section="## Open"` still show a row table?"**

If yes → **do not review, do not spawn, do not stage, do not push.** Return to **P1**, take the
remaining rows in table order, finish them, then re-ask. There is no partial-run exit: not "the
remaining rows are small", not "I'll ship what's done and open a follow-up", not "the reviewer can
look at the finished half while I work", not "CI is already running". A review round spent on a
change set that is still growing is a wasted round — every fix after it is unreviewed — and a push
that leaves rows open makes the release notes claim work that did not ship.

The only rows that may exist in `## Open` at P7 are the ones **P5 itself created** from this run's own
tool-usage review. Those are the next run's work by construction (P5 says so) and are exempt — they
must be identifiable as such: P5 records the ids it added, and the P7 check compares against that
list. **Any id not on it blocks the push.**

If a Ledger row genuinely cannot ship, it does not stay open — it closes as a **measured decision**
into `## Known limitations` with the evidence that closes it (P1 step 6b). Closing is how a row
leaves; leaving it open is a failed run.

---

## P0 — Preflight

1. Invoke, via the Skill tool, in this order: `terse-sharp`, `csharp-standards`,
   `csharp-feature-implementation`, `csharp-bug-fixing`, `code-review-gate`. They are the gates the
   later phases run under; loading them here means no phase pauses to load one.
2. `workspace_status`. If this repo is not loaded, `load_workspace` on `TerseSharp.slnx`. From here
   on **pass `workspace: "TerseSharp"` on every terse-sharp call** — worktrees under
   `.claude/worktrees/` make an un-hinted call ambiguous.
3. `changed_files` (not `git status`). Record every path already dirty **before** this run — those
   are another session's, and they are never staged in P7.
4. `Bash: git fetch origin && git rev-parse --abbrev-ref HEAD && git pull --ff-only`. If not on
   `main`, `git switch main` then pull. A non-fast-forward pull is a fixable state, not an exit:
   rebase onto `origin/main` and continue.
5. `Bash: gh auth status`. If unauthenticated, report it in the final report and still run P1–P7;
   P8–P11 then become DEGRADED with that reason.
6. `read_text IMPROVEMENTS.md section="## Open"`. Enumerate **every** row id in the table, in table
   order (the table's own ordering is a priority ordering — honour it). This list is the **Ledger**.
7. `TaskCreate` one task per Ledger row, plus one per phase P1…P11. `TaskUpdate` to `in_progress`
   when a phase starts and `completed` when its exit criteria are met. The task list is the proof
   that nothing was skipped.

**Exit criteria:** skills loaded, workspace loaded, branch `main` up to date, Ledger written, tasks
created.

---

## P1 — Implement every open row

Process the Ledger **in table order**, one row at a time, each to completion before the next.

For each row:

1. **Classify.** A row describing a wrong answer, a stale snapshot, a leak, a hang → run it under
   `csharp-bug-fixing` (reproduce first, prove the cause by falsification, failing regression test
   before the fix). A row describing a missing capability, a new parameter, a new field → run it
   under `csharp-feature-implementation` (numbered spec, surface sweep, test per requirement).
2. **Read the row's own evidence.** It names the tool, the measured cost and the proposed change.
   The proposed change is a *proposal*, not a spec: if the evidence refutes it, the refutation is the
   deliverable and the row closes into `## Known limitations` (see step 6b).
3. **Reproduce or measure first.** No production edit before the failing test exists and has been
   **observed failing**. Assert **values**, never "did not throw".
4. **Implement** per `csharp-standards` and this repo's own hard gates: logic in `TerseSharp.Core`
   returning `Result<string>`, the `Tools` class wires it; every optional parameter has a real C#
   default; async file system everywhere; span-first, allocation-last; success is the minimum
   response with `verbose=true` restoring the full one; a `dryRun` is never condensed.
   Edits go through `replace_symbol_body` / `replace_symbol` / `add_member` / `write_text`. Add the
   callee **before** the caller — a callee-after-caller edit is rolled back by the compile gate and
   costs the whole declaration.
5. **Census + coverage, same row:**
   - new tool → `ToolCoverageE2ETests.Exercised`, `ToolCensus` probe catalogue, `DocsCoverageE2ETests`
     (SKILL/README/NUGET_README), `TokenBudgetE2ETests` against the **widest** fixture case;
   - a new "every X does Y" rule → its census gate, discovering X from `tools/list` or source, in the
     same change; an exemption carries a written reason and a ratchet;
   - E2E against `fixtures/FixtureSolution` (or `BrokenSolution` / `WarningSolution` /
     `RazorSolution` / `GeneratorSolution` where the path demands it), plus unit tests for formatting
     and error paths. **A test the fixture cannot fail is not coverage** — put the case in the
     fixture, watch the test fail, then make it pass.
6. **Close the row in `IMPROVEMENTS.md`** (`edit_text section=…`, never a remembered `oldText`):
   - a. **shipped** → move the row into the shipped table, rewritten to state what shipped, the gate
     that locks it and the **measured** saving. "Improved" without a number is not closure.
   - b. **closed as a measured decision** → move it to `## Known limitations` with the evidence,
     the refutation, and the condition under which it should be reopened.
   In both cases the row **leaves `## Open`**. When the last row leaves, `## Open` says so in one
   sentence and keeps the heading.
7. **Docs gate, same row, all four:** `README.md` (tool table, tool count, replaces-table, numbers
   table, status table), `NUGET_README.md` (separate pure-Markdown copy — it diverges silently),
   `src/TerseSharp.Server/Assets/SKILL.md` (every tool named; swap table, working rules and hard gate
   describing behaviour *as it is now*), `CHANGELOG.md` under `## [Unreleased]` with the format change
   spelled out. If the row changed a rule this repo's own `CLAUDE.md` states, update `CLAUDE.md` too.
8. `TaskUpdate` the row's task to `completed`, naming which of 6a/6b applied.

**Exit criteria:** `read_text IMPROVEMENTS.md section="## Open"` shows **no row table**; every Ledger
task is `completed`; every shipped row has a test that was observed failing first.

---

## P2 — Diagnostics and format gates

Run in this order, reading each result before trusting the next. All are terse-sharp tools; the
`dotnet` CLI is not a reading of this phase.

1. `analyze` on **every** touched file, at the **lowest severity** (`info`). Default severity is not
   this gate. Fix everything it reports on files this run wrote or modified. Leave pre-existing
   diagnostics in untouched files alone.
2. `format`, then `cleanup`, on every touched file. Re-run `analyze` after — a cleanup surfaces new
   diagnostics.
3. `get_diagnostics` for the solution-wide sweep — the consumer you broke, the project that no longer
   compiles.
4. A truncated response is **not** a pass: narrow and re-run until every record has been seen. A file
   that reported nothing because its project failed to load was **not analyzed** — fix the load.

---

## P3 — Build and test

1. `build`. **Read it.** A red build followed by a test run reports the *previous* binary's green.
2. `run_tests` over the whole solution — unit and E2E. E2E needs `TerseSharp.Server` built first in
   the same configuration; `build` in step 1 satisfies that.
3. On `The pipe is being closed`, a mass E2E collapse, or a locked binary: a stale `terse` /
   `testhost` process is holding it. `Bash: tasklist | findstr /I "terse testhost"` (or `pgrep`),
   kill them, rebuild, re-run. That is a known cause of a false green, not a flake.
4. A one-runner red is not automatically a flake and "it passed on rerun" is not a diagnosis. Name it
   on evidence; if it is a timing budget, widen the budget in the test.
5. Any red → fix under `csharp-bug-fixing`, then return to **P2 step 1**. Loop until P2 and P3 are
   both clean. There is no "known failure" exit.

---

## P4 — The CI-equivalent format gate

1. `cleanup verify=true fix=all` over the solution. It is a **superset** of what CI runs, so a
   `VERIFY_FAILED` naming a file this run did not touch is a prompt to look, not proof CI is red.
2. `format verify=true`.
3. **The one legal shell-out of this command**, stated at the call and only after 1 and 2 are clean —
   because CI's ubuntu leg is the arbiter of this step and runs exactly these two:
   ```bash
   dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info
   dotnet format style     TerseSharp.slnx --verify-no-changes --severity info
   ```
   Scope them to the project touched when the solution-wide run is slow. Any finding → fix → back to
   **P2 step 1**.

---

## P5 — Tool-usage review (this run's own calls)

The continuous-improvement gate, measured over the calls **this command made**. Answer all five in
writing, with counts and response sizes — "it felt verbose" is not a finding:

1. **Round trips** — which answer cost ≥2 calls that one call could have returned?
2. **Payload** — which response carried tokens never used?
3. **Fallbacks** — every `Read`/`Grep`/`Glob`/`Edit`/`Bash` reach, and *which* missing, failing or
   undiscoverable tool caused it. **Every fallback is a product defect**, even when the built-in
   worked.
4. **Failures** — every `ERROR`, every retry with different arguments, every answer a tool could not
   prove.
5. **Unanswerable** — every question about the code no tool answered that Roslyn could have.

Each finding becomes one row in `IMPROVEMENTS.md` `## Open` — observed cost, tool, proposed change,
expected saving. **Rows created by this review are the next run's work and are NOT implemented now**;
P1's exit criterion was measured before this phase and is not reopened by it. An empty review is
legitimate only when it names what was checked and why each of the five came back clean.

---

## P6 — Changelog and version

1. **Derive the version** (or take `$ARGUMENTS` when it parsed as `X.Y.Z`). Read every entry now
   under `## [Unreleased]` and apply this repo's rule from `RELEASING.md`:
   - **MAJOR** — a tool removed or renamed, a parameter made required, a default changed, or a
     response format changed in a way an agent could have parsed;
   - **MINOR** — a new tool, a new optional parameter, a new response field;
   - **PATCH** — a bug fix that changes no contract.
   Take the **highest** class any entry qualifies for. Base it on the newest tag
   (`Bash: git tag --list --sort=-v:refname | head -1`). Never re-use an existing tag.
2. `edit_text CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - <today, ISO>`, open a fresh
   empty `## [Unreleased]` above it.
3. Add the link definition at the bottom:
   `[X.Y.Z]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/vX.Y.Z`
4. Repoint `[Unreleased]` to `https://github.com/amusleh-spotware-com/terse-sharp/compare/vX.Y.Z...HEAD`.
5. **Verify**: every `## [` heading except `[Unreleased]` has a link definition, and every link
   definition names a tag that exists or is the one about to be created
   (`search_text` over `CHANGELOG.md` + `Bash: git tag --list`).

---

## P7 — Review gate, then commit and push

0. **The `## Open` gate, first, before anything else in this phase.**
   `read_text IMPROVEMENTS.md section="## Open"`. Every id it still lists must be on the list P5
   recorded as created by this run's own review. If **any** other id is there — a Ledger row not
   implemented, not shipped, not closed into `## Known limitations` — **stop this phase**, return to
   **P1**, finish those rows, re-run **P2 → P3 → P4**, and re-enter P7 from this step. Do not spawn
   the reviewer, do not stage, do not commit. Re-run this check before **every** re-entry to P7
   after a P8 CI-failure fix, because a fix round can reopen a row.
1. **`code-review-gate` over the full change set**, per the skill: R0 ledger → R1 requirement
   conformance → R2 cold read → R3 the thirteen specialist passes → R4 prove-or-drop every candidate
   → R5 severity → R6 report → R7 fix → R8 exit criteria.
2. **Spawn the R2a fresh-context reviewer** (`general-purpose`, read-only). Its prompt carries, in
   this order: the verbatim terse-sharp preamble with `Workspace: TerseSharp`; the review-specific
   mapping (`changed_files` → `diff_symbols` → `get_symbol_source`; `find_usages` for consumers;
   `read_text`/`search_text` for non-`.cs`; never a raw `git diff`); the one carve-out (git
   **history** only — `git log`, `git blame`, `git show <base>:<path>`); read-only discipline; and the
   closing requirement that the report ends with the list of terse-sharp tools it called. A report
   containing `Read`/`Grep` on solution source is a **failed round** — re-spawn with the preamble.
3. Fix every CRITICAL and WARNING, or leave it open **in writing** with a justification. After the
   fixes, re-run **P2 → P3 → P4** and re-review the fix round. Converge in ≤3 rounds.
4. `changed_files`. Stage **by path**, only the paths this run produced — never a path recorded as
   already dirty in P0.4, never `-A`.
5. `Bash: git commit -m "<message>"`. Conventional, imperative, body naming the closed row ids.
   **No `Co-Authored-By` trailer.**
6. `Bash: git show --stat HEAD` — confirm the commit contains what is claimed, then
   `Bash: git push origin main`.

---

## P8 — Wait for CI, fix, wait again — until green

1. `Bash: git rev-parse HEAD` → `SHA`.
2. Find the run: `gh run list --workflow=ci.yml --branch main --limit 5 --json databaseId,headSha,status,conclusion,url`.
   If no run carries `SHA` yet, poll every 10 s for up to 3 minutes — a push takes seconds to
   register — then re-query.
3. **Wait with a live check, never a blind sleep.** Poll every 20 s, capped at 30 minutes:
   ```bash
   gh run view "$RUN_ID" --json status,conclusion,url -q '.status + " " + (.conclusion // "-")'
   ```
   Stop the moment `status` is `completed`. Between polls, do useful work (read the next phase's
   inputs, prepare the tag body) — never idle.
4. `conclusion == success` → P9.
5. Anything else (`failure`, `cancelled`, `timed_out`) → **fix, do not stop**:
   - `gh run view "$RUN_ID" --log-failed` and `gh run view "$RUN_ID" --json jobs` to find the failing
     leg. Name **which OS leg** failed — a ubuntu-only failure is almost always the
     `dotnet format analyzers|style --severity info` step (`IDE0022`, `IDE0060` and siblings are
     CI-breaking here and invisible to a local build).
   - Diagnose under `csharp-bug-fixing`. **Changing a guard means changing the tests that assert the
     old answer** — a local green on a stale expectation is the classic three-runner red.
   - Fix, re-run **P2 → P3 → P4**, commit by path, push, and return to **P8 step 1**.
   - Loop until `success`. No cap on iterations; each iteration is reported in the final report.

---

## P9 — Tag and release

1. `Bash: git tag vX.Y.Z && git push origin vX.Y.Z` (the tag content is already committed — P6 put
   the version heading and its link definition in the same commit that P7 pushed). Never re-tag an
   existing version; a bad release is fixed forward with a new patch tag.
2. Find the release run:
   `gh run list --workflow=release.yml --limit 5 --json databaseId,headBranch,status,conclusion,url`.
3. Wait with the same live-check loop as P8.3, capped at 30 minutes. The job builds, tests, packs,
   **installs the packed tool globally and runs `terse doctor`**, then publishes via NuGet trusted
   publishing and creates the GitHub release.
4. Failure → `gh run view --log-failed`, fix, re-run P2–P4, commit, push, wait for CI green (P8),
   then tag the **next** patch version and return to P9 step 2. Never delete or move a pushed tag.

---

## P10 — Wait until the version is live on NuGet

1. Poll the flat container — it is authoritative and updates first:
   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/tersesharp/index.json
   ```
   (PowerShell fallback: `powershell -NoProfile -Command "Invoke-RestMethod https://api.nuget.org/v3-flatcontainer/tersesharp/index.json | ConvertTo-Json -Compress"`.)
2. Poll every 30 s, capped at 30 minutes, stopping the moment `X.Y.Z` appears. Each poll also
   re-checks that the release run is still `completed/success` — a wait that only watches for the
   artifact cannot tell a slow publish from a dead one.
3. The **registration** endpoint lags the flat container by about a minute, so
   `dotnet tool install -g TerseSharp --version X.Y.Z` can still 404 briefly after the version is
   listed. Report that plainly rather than claiming the local install is current.
4. Confirm the GitHub release exists: `gh release view vX.Y.Z --json tagName,isDraft,assets`.
5. Do **not** claim the locally connected `terse` MCP server is now this version — it is whatever
   `dotnet tool install/update` last put on PATH, it holds file locks on `terse.dll` while running,
   and it does not pick up this release until Claude Code restarts. Say so.

---

## P11 — Final report

One report, no questions, containing:

| Section | Content |
|---|---|
| Rows closed | every Ledger id, and whether it shipped (with its measurement + the gate that locks it) or closed as a measured decision (with the evidence) |
| Tests | what was added per row, observed failing first; final `run_tests` counts per tier |
| Gates | `analyze` (info) / `format` / `cleanup` / `get_diagnostics` / `build` / `cleanup verify=true fix=all` / both `dotnet format` verdicts |
| Review | `code-review-gate` verdict, CRITICALs and WARNINGs found and fixed, rounds taken, the R2a reviewer's terse-sharp tool list |
| Docs | which of README / NUGET_README / SKILL.md / CHANGELOG / CLAUDE.md changed, per row |
| CI | every run URL, every failure and its fix, number of iterations to green |
| Release | tag, release run URL, GitHub release URL, the NuGet version and the time it took to appear |
| Tool-usage review | the five answers, measured, and the new `## Open` rows they created |
| DEGRADED | every phase that could not run in full, the substitute that ran, and why |

Then, and only then, the run is done.
