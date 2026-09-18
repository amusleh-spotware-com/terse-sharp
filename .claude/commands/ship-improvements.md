---
description: Implement every open row in IMPROVEMENTS.md, close them, update the docs, verify, commit each one, push, wait for CI green, cut a release, and wait until the version is live on NuGet.
argument-hint: "[optional: explicit version, e.g. 0.24.0 — otherwise derived]"
---

# 🚫 HARD GATE — every open row leaves `## Open` in THIS run

`$ARGUMENTS` — if it parses as `X.Y.Z`, that is the release version; otherwise it is ignored and the
version is **derived** in P6. Nothing else in this command takes input from the user.

**Banned for the whole run, without exception:**

- `AskUserQuestion`, `ExitPlanMode`, "shall I continue?", a summary that hands work back.
- Stopping at the first failure. A red build, a red test, a red CI leg, a rejected edit, a failed
  release job is **an input to the next step**, not an exit. Diagnose, fix, re-verify, continue.
- Declaring a phase done that was not run. If a phase genuinely cannot execute, run the strongest
  substitute, **say so in the final report as DEGRADED with the reason**, and continue.
- Skipping a row because it looks hard, vague or unmeasurable.
- `Read` / `Grep` / `Glob` / `Edit` / `Write` on anything under this repo, and `Bash: git status`,
  `git diff`, `git log`, `grep`, `rg`, `cat`, `head`, `tail`, `sed`, `awk`, `ls`, `find`,
  `dotnet build`, `dotnet test`, `dotnet clean`, `dotnet format` (one carve-out, P4.3).
  Every one of those is a `terse-sharp` call. Only `git blame`, index/history mutation (`add`,
  `commit`, `tag`, `push`), `gh`, and the NuGet probe stay on `Bash`.
- A blind wait. Every wait on an external process detects the process **dying**, not only the
  artifact appearing (P8, P10, P11 give the exact shape).
- `git add -A` / `git commit -am`. Stage by path.
- A `Co-Authored-By:` trailer in any commit message.
- Weakening, skipping or deleting a test to make a suite go green.

**Banned reasoning — these were the measured causes of a partial run, and each is false:**

> "this cannot be completed to standard in one pass" · "I'll ship what's done and open a follow-up" ·
> "the remaining rows are large" · "context is filling up" · "let me read this file first to
> understand the area" · "one row at a time is safer" · "I'll report honestly instead of finishing".

A previous run closed **13 of 29 rows in ~150 tool calls — 11.5 calls per row — because it worked
depth-first and sequentially**, reading whole files to "understand" each area before editing. The
rows are almost entirely independent and touch disjoint files. P1 below removes that failure
mechanically: the investigation fans out in parallel, the applying stays serial, and **no row is
investigated by the main thread**. There is no capacity argument left to make.

---

# 🚫 HARD GATE — commit EVERY row as you close it; gate only the RELEASE on `## Open`

The previous run's second failure was structural: the old version of this command forbade `git add`
until `## Open` was empty, so a run that finished 45% of the rows delivered **0%** — the work sat
uncommitted and a second session had to start from nothing.

So the gate is now split, and the split is not negotiable:

| Action | Gate |
|---|---|
| `git commit` of one finished row | **none** — commit it the moment P1c finishes it, green |
| `git push` | after each wave, once its rows are committed and the suite is green |
| `code-review-gate` + the R2a reviewer | **once**, over the whole change set, after the last row |
| `git tag`, the release workflow, the NuGet wait | **only when `## Open` holds no row this run did not create in P5** |

If the run is interrupted, every finished row is already on `main`. Re-running this command resumes:
P0 re-reads `## Open`, which now holds only what is left.

**A row leaves `## Open` in exactly one of two ways.** Shipped (P1c), or closed as a **measured
decision** with the evidence that closes it (P1c.6b). "I ran out of time" is not a measured decision
and must never be written into the archive. If a row genuinely cannot ship, the refutation IS the
deliverable — and refutations are cheap, so a row you cannot build is a row you can close in one
investigation, not a row you leave open.

---

## P0 — Preflight

1. Invoke, via the Skill tool, in this order: `terse-sharp`, `csharp-standards`,
   `csharp-feature-implementation`, `csharp-bug-fixing`, `code-review-gate`.
2. `workspace_status`. If this repo is not loaded, `load_workspace` on `TerseSharp.slnx`. From here
   on **pass `workspace: "TerseSharp"` on every terse-sharp call**.
3. `changed_files`. Record every path already dirty **before** this run — another session's, never
   staged.
4. `Bash: git fetch origin && git rev-parse --abbrev-ref HEAD && git pull --ff-only`. If not on
   `main`, `git switch main` then pull.
5. `Bash: gh auth status`. If unauthenticated, report it and still run P1–P7; P8–P11 become DEGRADED.
6. `read_text IMPROVEMENTS.md section="## Open"`. Enumerate **every** row id. This is the **Ledger**.
7. `TaskCreate` one task per Ledger row plus one per phase. `TaskUpdate` as each completes.

**Exit criteria:** skills loaded, workspace loaded, branch `main` clean and current, Ledger written.

---

## P1 — Close every row: triage, fan out, apply, commit

### P1a — Triage and wave plan (main thread, one pass, ≤10 calls total)

Read the whole `## Open` table once. For **every** row, in one pass, record in the Ledger:

```
<id> | SMALL | MEDIUM | LARGE | files it will touch | the tool(s) it changes | verdict: BUILD | CLOSE
```

- **files it will touch** is the point of this phase. Derive it from the row's own `Tool` column with
  `search_symbols` / `find_files` — **not** by reading files. One `search_symbols` per named tool,
  batched: `search_symbols` accepts one query, so send several in ONE message, not one per message.
- **CLOSE** means the row's evidence already refutes its proposal, or the behaviour does not
  reproduce. Mark it now; P1c.6b closes it without an investigation.
- Two rows sharing a file belong to the same wave slot and are handed to the **same** planner.

Then partition the Ledger into **waves of disjoint file sets**, at most 6 rows per wave. Order waves
so the LARGE rows are in wave 1 — they are the long pole and must start first.

### P1b — Fan out the investigation, ALWAYS (never investigate a row on the main thread)

For each wave, spawn **one `general-purpose` agent per row (or per file-sharing group), all in ONE
message** so they run concurrently. They are **planners**: they read, they may `dryRun`, and they
return a ready-to-apply plan. They do **not** write, build or run tests — every agent shares one
terse server and one loaded workspace, so a concurrent write or build is the collision `I573`
describes.

Every spawn prompt carries, in this order:

1. The verbatim terse-sharp preamble from the user-scoped `CLAUDE.md` (mandate + ban list + tool
   table + `Workspace: TerseSharp`).
2. **The row, pasted verbatim** — finding, tool, proposed change, expected saving, rejected
   approaches. Never a summary of it.
3. The file list P1a derived, and the instruction to open only those.
4. This contract:

```
You are a PLANNER. Read-only plus dryRun. You may call: workspace_status, get_file_outline,
get_symbol_source, get_symbol, get_type_outline, search_symbols, find_usages, find_implementations,
find_files, search_text, search_regex, read_text, list_projects, and any edit tool ONLY with
dryRun=true. You may NOT write, build, run tests, format, cleanup or gate.
Call ceiling: 25. If you exceed it, return the best plan you have and say so.

Return EXACTLY this, and nothing else:

  ROW: <id>
  VERDICT: BUILD | CLOSE
  CAUSE: <what you verified, with the file:line you opened>
  EDITS: an ordered list, each one of
     replace_symbol   symbolId=<id>            declaration=<the complete new declaration>
     replace_symbol_body symbolId=<id>         body=<the complete new body>
     add_member       typeSymbolId=<id>        declaration=<the complete new declaration>
     edit_text        path=<p> oldText=<exact> newText=<exact>
     write_text       path=<p> content=<whole file>
   Callees BEFORE callers. Every declaration complete, attributes included.
  DRYRUN: the verdict of the dryRun you ran on the riskiest edit, or why none was possible
  TESTS: the test file, the test name, and the complete test body, per requirement
  OBSERVE-RED: the exact call that proves the behaviour is wrong BEFORE the fix
  DOCS: the exact CHANGELOG / SKILL.md / README / NUGET_README lines to add or change
  ARCHIVE: the finished 4-cell archive row - Finding | Tool | Change | Outcome
  If VERDICT is CLOSE: the evidence that refutes the row, and the condition to reopen it.
```

A planner that returns prose instead of that shape is a failed run: re-spawn it once with the shape
repeated. Do not investigate the row yourself instead — that is the failure this phase removes.

**While a wave's planners run, the main thread applies the PREVIOUS wave's plans.** The two overlap;
never idle, never poll, never `sleep`.

### P1c — Apply, serially, on the main thread

Per plan, in order:

1. **Observe red.** Run the plan's `OBSERVE-RED` call. For a behaviour change, the pre-change binary
   is the connected server — a refusal from it is the strongest "failing first" evidence available.
   Record what it answered.
2. **Apply the EDITS in order.** Callee before caller. Batch with `replace_symbol symbolIds=[…]`,
   `add_member declarations=[…]`, `edit_text edits=[…]`, `write_text files=[…]` — a run of three
   single-target writes is a batch you did not send.
3. **Add the TESTS.** Assert values, never survival. If the plan's test cannot fail on the fixture,
   put the case in the fixture first — and check the fixture addition does not move a count another
   test asserts (a new `CA1822` in the fixture widened `gate` past its budget once).
4. `analyze` the touched files at `severity=info`. Fix what this row introduced. Ignore the
   repo-wide `.terse.json` policy rows it did not author.
5. **DOCS, same row, all four**: `CHANGELOG.md` under `## [Unreleased]` naming the covering tests
   verbatim, `src/TerseSharp.Server/Assets/SKILL.md`, `README.md`, `NUGET_README.md`. A renamed test
   breaks `ChangelogReferenceTests` against the *previous* release's section too — follow the rename
   there as well.
6. **Move the row out of `## Open`** with `edit_text rows=[…] toPath="IMPROVEMENTS-ARCHIVE.md"`,
   which moves up to 25 rows in one call. Five open columns collapse to four closed ones.
   - a. **shipped** → `Change` is what shipped, `Outcome` names the gate that locks it and the
     **measured** saving. "Improved" without a number is not closure.
   - b. **closed as a measured decision** → `Outcome` carries the evidence, the refutation and the
     reopen condition.
7. **Commit the row, by path**: `Bash: git commit -m "<type>: <what> (<row id>)"`. No
   `Co-Authored-By`. This is the checkpoint that makes an interrupted run worth something.

**Exit criteria of P1:** `read_text IMPROVEMENTS.md section="## Open"` shows no row table; every
Ledger task is `completed`; every shipped row has a test observed failing first and a commit.

---

## P2 — Diagnostics gates (once per wave, scoped)

1. `analyze paths=[every file the wave touched] severity=info` — **one call**, not one per file.
2. `format` then `cleanup` on those paths; re-`analyze`.
3. At the END of the last wave only: `gate` unscoped, then `get_diagnostics` for the solution-wide
   sweep.
4. A truncated response is not a pass. A file that reported nothing because its project failed to
   load was not analyzed.

## P3 — Build and test (scoped per wave, whole suite ONCE)

1. `build`. **Read it** before any test result.
2. Per wave: `run_tests changed=true`, or the affected projects.
3. **The whole-solution `run_tests` runs ONCE, after the last wave.** The previous run spent ~40
   minutes on four full-suite runs; that is the single largest avoidable cost in this command.
4. On `The pipe is being closed`, a mass E2E collapse or a locked binary: a stale `terse`/`testhost`
   holds it. Kill, rebuild, re-run. That is a known false green, not a flake.
5. Any red → fix at cause, never by weakening a test. A test that encodes the behaviour a row
   deliberately changed is **replaced** by one pinning the new contract *and* the case that must
   still fail. Then back to **P2**.

## P4 — The CI-equivalent format gate

1. `cleanup verify=true fix=ci` — both ubuntu CI commands in ONE call, byte-equivalent. This is the
   arbiter.
2. `format verify=true` and `cleanup verify=true fix=all` are the wider sweep; a file only they name
   is not a red CI leg.
3. **The one legal shell-out**, stated at the call, only if you suspect the in-server result and CI
   genuinely disagree — and then report the disagreement as a finding:
   `dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info` and the `style` twin.

## P5 — Tool-usage review (this run's own calls)

Answer all five in writing, with counts and response sizes: **round trips** · **payload** ·
**fallbacks** (every built-in reach, and which missing tool caused it) · **failures** (every `ERROR`,
every retry) · **unanswerable**. Report the run's **tool-call count and calls-per-row**; a run above
~6 calls per row did not fan out properly and that itself is a finding.

Each finding becomes one row in `## Open`. **Record the ids you added** — they are the only rows
allowed to be open at P7, and P7 checks against that list.

## P6 — Changelog and version

1. Derive the version (or take `$ARGUMENTS` when it parsed as `X.Y.Z`), from every entry now under
   `## [Unreleased]`, per `RELEASING.md`: **MAJOR** — a tool removed or renamed, a parameter made
   required, a default or response format changed; **MINOR** — a new tool, optional parameter or
   response field; **PATCH** — a fix that changes no contract. Take the **highest** class any entry
   qualifies for, based on the newest tag. Never re-use a tag.
2. Rename `## [Unreleased]` to `## [X.Y.Z] - <today, ISO>`; open a fresh empty `## [Unreleased]`.
3. Add `[X.Y.Z]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/vX.Y.Z`.
4. Repoint `[Unreleased]` to `…/compare/vX.Y.Z...HEAD`.
5. Verify every `## [` heading has a link definition and every definition names a tag that exists or
   is about to.

## P7 — Review the whole change set, then push

0. **The `## Open` gate.** `read_text IMPROVEMENTS.md section="## Open"`. Every id it still lists
   must be on the list P5 recorded. Any other id → return to **P1**, finish it, re-run P2→P4, and
   re-enter here. Re-check after every P8 fix round.
1. `code-review-gate` over the full change set, R0→R8.
2. **Spawn the R2a fresh-context reviewer** (`general-purpose`, read-only), its prompt carrying the
   verbatim terse-sharp preamble, `Workspace: TerseSharp`, the `changed_files` output and the
   `diff_symbols` ids **inline** as its scope, a call ceiling, the git-history-only carve-out, and
   the requirement that its report ends with the terse-sharp tools it called.
3. Fix every CRITICAL and WARNING, or justify it in writing. Re-run P2→P4, re-review the fixes only.
   Converge in ≤3 rounds.
4. `changed_files`; stage by path — never a path P0.4 recorded as already dirty, never `-A`.
5. `Bash: git commit -m "…"` for anything not already committed per row, then
   `Bash: git show --stat HEAD` and `Bash: git push origin main`.

## P8 — Wait for CI, fix, wait again — until green

1. `Bash: git rev-parse HEAD` → `SHA`.
2. `gh run list --workflow=ci.yml --branch main --limit 5 --json databaseId,headSha,status,conclusion,url`.
   If no run carries `SHA`, poll every 10 s for up to 3 minutes, then re-query.
3. Poll every 20 s, capped at 30 minutes:
   `gh run view "$RUN_ID" --json status,conclusion,url -q '.status + " " + (.conclusion // "-")'`.
   Between polls do useful work — never idle, never `sleep`.
4. `success` → P9.
5. Anything else → `gh run view "$RUN_ID" --log-failed`, name **which OS leg** failed (a ubuntu-only
   red is almost always the `--severity info` format step), fix at cause, re-run P2→P4, commit by
   path, push, return to step 1. No cap on iterations; report each.

## P9 — Tag and release

1. `Bash: git tag vX.Y.Z && git push origin vX.Y.Z`. Never re-tag; fix forward with a patch tag.
2. `gh run list --workflow=release.yml --limit 5 --json databaseId,headBranch,status,conclusion,url`.
3. Wait with the P8.3 loop, capped at 30 minutes.
4. Failure → `gh run view --log-failed`, fix, re-run P2–P4, commit, push, CI green, then tag the
   **next** patch version. Never delete or move a pushed tag.

## P10 — Wait until the version is live on NuGet

1. Poll the flat container — authoritative, updates first:
   `curl -s https://api.nuget.org/v3-flatcontainer/tersesharp/index.json`
   (PowerShell fallback: `powershell -NoProfile -Command "Invoke-RestMethod https://api.nuget.org/v3-flatcontainer/tersesharp/index.json | ConvertTo-Json -Compress"`).
2. Every 30 s, capped at 30 minutes, stopping when `X.Y.Z` appears. Each poll re-checks that the
   release run is still `completed/success` — a wait that watches only for the artifact cannot tell a
   slow publish from a dead one.
3. The registration endpoint lags the flat container by ~a minute, so
   `dotnet tool install -g TerseSharp --version X.Y.Z` can 404 briefly. Say so plainly.
4. `gh release view vX.Y.Z --json tagName,isDraft,assets`.
5. Do **not** claim the connected `terse` MCP server is now this version — it is whatever
   `dotnet tool install/update` last put on PATH, it holds locks on `terse.dll` while running, and it
   does not pick this up until Claude Code restarts.

## P11 — Final report

| Section | Content |
|---|---|
| Rows closed | every Ledger id: shipped (measurement + the gate that locks it) or closed as a measured decision (with the evidence). **The count must equal the Ledger's** |
| Waves | how many, how many rows each, how many planners ran concurrently |
| Tests | what was added per row, observed failing first, and how; final `run_tests` counts |
| Gates | `analyze` / `format` / `cleanup` / `get_diagnostics` / `build` / `cleanup verify=true fix=ci` |
| Review | verdict, CRITICALs and WARNINGs found and fixed, rounds, the R2a reviewer's tool list |
| Docs | which of README / NUGET_README / SKILL.md / CHANGELOG / CLAUDE.md changed, per row |
| CI | every run URL, every failure and its fix, iterations to green |
| Release | tag, release run URL, GitHub release URL, NuGet version and time to appear |
| Tool-usage review | the five answers, the run's tool-call count, calls per row, and the new `## Open` rows |
| DEGRADED | every phase that could not run in full, the substitute, and why |

Then, and only then, the run is done.
