---
description: Cut a release — derive the version, close the review, rewrite CHANGELOG, commit, push, wait for CI, tag, wait for the Release workflow and the GitHub release, then wait until the version is live on NuGet.
argument-hint: "[optional: explicit version, e.g. 0.24.0 — otherwise derived from [Unreleased]]"
---

# 🚫 HARD GATE — a release is public the moment it is pushed. Every phase runs, in order.

`nuget delete` only **unlists**; a wrong answer shipped in a package costs every agent that installs
it. `$ARGUMENTS` — if it parses as `X.Y.Z`, that is the version; otherwise the version is **derived**
in R2. Nothing else takes input from the user.

**Banned for the whole run:**

- `AskUserQuestion`, `ExitPlanMode`, "shall I tag now?", handing work back. Autonomous R0 → R9.
- **Tagging while a review is open, or with an unaddressed CRITICAL/WARNING.** R3 is not optional and
  not satisfied by "a reviewer was spawned" — its report must exist and have been read.
- **Tagging on a red anything**: a red build, a red suite, a red `dotnet format` verify, a red CI run
  on the commit being tagged. "It passed on rerun" is not a diagnosis.
- **Re-tagging, moving or deleting a pushed tag.** The tag is the identity of the build. A bad release
  is fixed **forward** with a new patch version, and the bad one is unlisted on nuget.org.
- Implementing a feature, fixing an unrelated bug, or refactoring. This command **releases what is on
  `main`**. Only two content edits are in scope: `CHANGELOG.md` (R4) and a fix demanded by a red gate
  or a review finding (R3/R6). Anything else is scope creep — log it, do not do it.
- `Read` / `Grep` / `Glob` / `Edit` / `Write`, and `Bash: git status`, `git diff`, `grep`, `rg`,
  `cat`, `head`, `tail`, `sed`, `awk`, `ls`, `find`, `dotnet build`, `dotnet test`. All terse-sharp
  calls. Only git **history**, index/history mutation (`add`, `commit`, `tag`, `push`), `gh`,
  `dotnet tool install/update`, the two `dotnet format` verify commands (R2.5) and the NuGet probe
  stay on `Bash`.
- A blind wait. Every wait detects the run **dying**, not only the artifact appearing.
- `git add -A` / `git commit -am`. Stage by path — this tree is shared with other sessions.
- A `Co-Authored-By:` trailer.

---

## R0 — Preflight

1. Invoke the `terse-sharp` skill; `workspace_status` / `load_workspace TerseSharp.slnx`; pass
   `workspace: "TerseSharp"` on every call.
2. `Bash: git fetch origin --tags && git rev-parse --abbrev-ref HEAD`. Must be `main`; if not,
   `git switch main`. `git pull --ff-only`.
3. `changed_files`. **Uncommitted work is NOT released** — the tag is cut on `HEAD`, and CI checks out
   the tag into a clean tree. List every dirty path in the R9 report as "not in this release", and
   never stage a path this run did not itself produce.
4. `Bash: git log --oneline $(git describe --tags --abbrev=0)..HEAD` — the commits this release
   contains. If the range is **empty**, there is nothing to release: say so and stop after R9's
   report. That is the one legitimate early exit, and it is reported, not silent.
5. `TaskCreate` one task per phase.

---

## R1 — What is actually being released

1. `read_text CHANGELOG.md section="## [Unreleased]"`. Every entry here is a claim about the range in
   R0.4 — cross-check them: an entry with no commit behind it is deleted, a commit whose behaviour
   change has no entry gets one (`diff_symbols baseRef=<last tag>` maps the range's hunks to the
   declarations that changed, which is the cheap way to see what moved).
2. Classify every entry: **contract-affecting** (a tool removed or renamed, a parameter made required,
   a default changed, a response format changed) / **additive** (new tool, new optional parameter, new
   response field) / **fix**. This classification is the version, so it is written down.

---

## R2 — Local gates, in this order, each read before the next

1. **`build`** — read it. A red build followed by a test run reports the **previous** binary's green,
   which has been reported here more than once. Never `--no-build` locally.
2. **`run_tests`** over the whole solution — unit and E2E. A mass E2E collapse with
   `The pipe is being closed` means a stale `terse`/`testhost` holds the binary: `Bash: tasklist`
   (or `pgrep`), kill, rebuild, re-run. That is a known false-green cause, not a flake.
3. **`analyze`** down to `info` on anything this run touches, `get_diagnostics` for the solution-wide
   sweep.
4. **`cleanup verify=true fix=all`** and **`format verify=true`**. `cleanup` is a **superset** of the
   CI step — a `VERIFY_FAILED` on an untouched file is a prompt to look, not proof CI is red.
5. The arbiter of CI's ubuntu-only step, and the one legal shell-out here, stated at the call:
   ```bash
   dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info
   dotnet format style     TerseSharp.slnx --verify-no-changes --severity info
   ```
   `IDE0022` and `IDE0060` are **CI-breaking** in this repo and invisible to the build, because
   `.editorconfig` carries them at `suggestion` and `TreatWarningsAsErrors` escalates warnings only.
6. Any red → fix (minimally, under `csharp-bug-fixing` if it is a behaviour failure), then restart at
   step 1. There is no "known failure" exit and no release on a red gate.

---

## R3 — The review must be closed before the tag exists

1. Run the **`code-review-gate`** skill over the whole range in R0.4 — `changed_files baseRef=<last
   tag>` then `diff_symbols baseRef=<last tag>`, never a raw `git diff`.
2. **Spawn the R2a fresh-context reviewer** (`general-purpose`, read-only) with, in this order: the
   verbatim terse-sharp preamble naming `Workspace: TerseSharp`; the review mapping (`changed_files`
   → `diff_symbols` → `get_symbol_source`; `find_usages` for consumers; `read_text`/`search_text` for
   non-`.cs`); the one carve-out (git **history** only — `git log`, `git blame`,
   `git show <base>:<path>`); read-only discipline; and the requirement that the report ends with the
   terse-sharp tools it called. A report citing `Read`/`Grep` on solution source is a **failed round**
   — re-spawn with the preamble rather than trusting its `file:line`.
3. **Wait for the report and read it.** "A review was started" is not this gate.
4. Fix every CRITICAL and WARNING, re-run **R2**, and re-review the fix round. A finding may stay open
   only if it is written down as a deliberate decision — in the report **and** in the release notes.
   "I disagree" and "it is a NIT to me" are not justifications.
5. If the fixes changed behaviour, R1's classification is re-derived — a fix round can turn a PATCH
   into a MINOR.

---

## R4 — Version and CHANGELOG

1. **Version** = `$ARGUMENTS` if it parsed, else derived from R1.2, taking the **highest** class
   present, applied to `Bash: git tag --list --sort=-v:refname | head -1`:
   **MAJOR** contract-affecting · **MINOR** additive · **PATCH** fix-only.
   Confirm `vX.Y.Z` does **not** already exist (`git tag --list vX.Y.Z` must be empty) — versions are
   never re-cut.
2. `edit_text CHANGELOG.md`: rename `## [Unreleased]` to `## [X.Y.Z] - <today, ISO>` and open a fresh
   empty `## [Unreleased]` above it. Use `section=`, never an `oldText` remembered from an earlier
   read — a parallel session rewriting that section between the read and the edit is a recorded trap.
3. Add the link definition at the bottom:
   `[X.Y.Z]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/vX.Y.Z`
4. Repoint `[Unreleased]` to
   `https://github.com/amusleh-spotware-com/terse-sharp/compare/vX.Y.Z...HEAD`.
5. **Verify:** every `## [` heading except `[Unreleased]` has a link definition, and every link
   definition names a tag that exists or is the one about to be created (`search_text` over
   `CHANGELOG.md` + `Bash: git tag --list`). A heading without its link is a dead reference on
   nuget.org and on the GitHub release page.
6. **Docs freshness, cheap check:** the release ships `SKILL.md` inside the package. R2.2's full run
   covers `DocsCoverageE2ETests` (every advertised tool named in README, NUGET_README, SKILL.md), so a
   green suite is the proof. If any doc claims a tool count, confirm it matches
   `search_regex "\[McpServerTool\(Name = \"[a-z_]+\"" maxResults=1` — one call, the count line is the
   answer. A mismatch is fixed here **only** if it is a number; anything larger is `/sync-docs`' job
   and blocks the release until run.

---

## R5 — Commit and push the release content

1. `changed_files`; stage **by path** — `CHANGELOG.md`, plus any file R3's fixes or R4.6 touched.
   Never a path dirty before this run.
2. `Bash: git commit -m "Release X.Y.Z"` (body: the one-line summary of what the version contains).
   **No `Co-Authored-By`.**
3. `Bash: git show --stat HEAD` — confirm the commit is what is claimed. This repo has shipped a
   commit that did not contain the edit claimed for it because a parallel session landed in between.
4. `Bash: git push origin main`.

---

## R6 — CI must be green on the commit that will carry the tag

1. `Bash: git rev-parse HEAD` → `SHA`;
   `gh run list --workflow=ci.yml --branch main --limit 5 --json databaseId,headSha,status,conclusion,url`.
   Poll for the run carrying `SHA` for up to 3 minutes if it has not registered yet.
2. Poll `gh run view "$RUN_ID" --json status,conclusion,url` every 20 s, capped at 30 minutes. Stop
   the moment `status` is `completed`. Never idle between polls, never a blind `sleep`.
3. `success` → R7.
4. Anything else → `gh run view "$RUN_ID" --log-failed`, name **which OS leg** failed (ubuntu-only is
   almost always the `dotnet format … --severity info` step), fix, re-run **R2**, commit by path,
   push, and return to R6.1. A one-runner red is not automatically a flake: real macOS-only and
   Windows-only failures have shipped here. **The tag is not created until this is green.**

---

## R7 — Tag, and watch the Release workflow

1. `Bash: git tag vX.Y.Z && git push origin vX.Y.Z`.
2. `gh run list --workflow=release.yml --limit 5 --json databaseId,headBranch,status,conclusion,url`
   → the run for this tag. Poll it with the same live-checked loop as R6.2, capped at 30 minutes.
   The job: checks out the tag with full history (MinVer needs it), builds, tests, packs, **installs
   the packed tool globally and runs `terse doctor`** — so a broken package cannot be published —
   exchanges the GitHub OIDC token for a short-lived NuGet key via trusted publishing, pushes, and
   creates the GitHub release with the `.nupkg` attached and generated notes.
3. A failure at the **publish** step is worth naming precisely: trusted publishing validates package
   owner `AlgoDeveloper`, repository owner `amusleh-spotware-com`, repository `terse-sharp`, workflow
   `release.yml` and environment `production`. A renamed workflow, repo or environment breaks it until
   the policy is updated on nuget.org, and a newly created policy can lapse after a **7-day probation
   window** with nothing published.
4. Any failure → diagnose from `--log-failed`, fix on `main`, re-run R2, push, wait for CI (R6), then
   tag the **next patch** version and return to R7.2. Never delete or move the failed tag; if it
   published a bad package, unlist it on nuget.org and say so.

---

## R8 — Wait until the version is live on NuGet

1. Poll the flat container — authoritative and first to update:
   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/tersesharp/index.json
   ```
   PowerShell fallback:
   `powershell -NoProfile -Command "Invoke-RestMethod https://api.nuget.org/v3-flatcontainer/tersesharp/index.json | ConvertTo-Json -Compress"`.
2. Every 30 s, capped at 30 minutes, stopping the moment `X.Y.Z` appears. **Each poll also re-checks
   the release run is still `completed/success`** — a wait that only watches for the artifact cannot
   tell a slow publish from a dead one.
3. The **registration** endpoint lags the flat container by about a minute, so
   `dotnet tool install -g TerseSharp --version X.Y.Z` can still 404 briefly after the version lists,
   and a cached index can make the first `dotnet tool update` no-op. Report that plainly.
4. `gh release view vX.Y.Z --json tagName,isDraft,assets,url` — the release exists, is not a draft,
   and carries the `.nupkg`. A tag containing `-` is a prerelease by design.

---

## R9 — Install, and report honestly

1. `Bash: dotnet tool update -g TerseSharp` — allowed here (no tool serves it). **Expect it to fail or
   to report a success it cannot deliver**: the running MCP server holds file locks on `terse.dll`,
   so the new binary does not take effect until Claude Code restarts. Say exactly what happened.
2. **Do not claim the connected `terse` server is now this version.** It is whatever was installed
   when the session started. Any behavioural claim about the new release must be sourced from the
   Release workflow's own smoke test (`terse --version`, `terse doctor`), not from this session's MCP.
3. **Report**, one message:

| Section | Content |
|---|---|
| Version | `X.Y.Z`, the class (MAJOR/MINOR/PATCH) and the entries that decided it |
| Contents | the commit range released, and anything dirty in the tree that was **not** released |
| Gates | `build`, `run_tests` counts, `analyze`, `cleanup verify`, `format verify`, both `dotnet format` verdicts |
| Review | the `code-review-gate` verdict, CRITICALs/WARNINGs found and fixed, rounds, any finding left open **with its written justification**, and the R2a reviewer's terse-sharp tool list |
| CHANGELOG | the new heading, its link definition, and the repointed `[Unreleased]` compare link |
| CI | run URL and conclusion, plus every red iteration and its fix |
| Release | tag, Release-workflow run URL, GitHub release URL, the smoke test's `terse --version` output |
| NuGet | the version listed, how long it took to appear, and the registration-lag caveat |
| Local install | what `dotnet tool update -g` actually did, and that a restart is required |
| DEGRADED | any phase that could not run in full, the substitute, and why |
