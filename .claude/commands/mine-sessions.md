---
description: Mine every Claude Code session across all projects for token, speed and productivity waste, log the findings as open rows in IMPROVEMENTS.md, then commit and push.
argument-hint: "[weeks to scan, default 1]"
---

# 🚫 HARD GATE — findings are **measured**, never impressionistic, and never leak other projects' content.

`$ARGUMENTS` — a number of **weeks** to scan. Absent or unparseable → **1 week**. Nothing else takes
input from the user; do not ask, do not confirm the window.

**Two gates that outrank everything else in this command:**

1. **A finding without a number is not a finding.** "Reads feel wasteful" is banned. "36 `Read` calls
   in one session, 214 KB of tool results, ~53 500 tokens, against 10 `search_text` calls" is a
   finding. Every row carries a count, a byte/token figure and the sessions it came from.
2. **Never copy content out of another project's transcript.** These sessions are from a private
   employer codebase (`cTraderDev`, `ctd-worktrees-*`, `cTraderAutomateApi`, …) and `IMPROVEMENTS.md`
   is pushed to a **public** repository. A row may name: tool names, call counts, byte sizes, error
   codes, timings, and a project's **directory slug**. A row may **never** contain: source code, file
   paths inside those repos, type or member names, ticket text, user prompts, or a quoted tool result.
   When in doubt, state the shape (`a 40 KB whole-file read of a test file`) not the value.

**Also banned:** `AskUserQuestion`, `ExitPlanMode`, spawning subagents (this run reads logs and edits
one markdown file — a reviewer adds nothing and doubles the private-content exposure), editing any
file other than `IMPROVEMENTS.md`, `git add -A`, a `Co-Authored-By:` trailer, and writing the analysis
script anywhere inside the repository.

---

## M0 — Preflight

1. Invoke the `terse-sharp` skill. `workspace_status` / `load_workspace TerseSharp.slnx`; pass
   `workspace: "TerseSharp"` on terse-sharp calls.
2. `Bash: git fetch origin && git pull --ff-only` on `main`. `changed_files` — record pre-existing
   dirt; only `IMPROVEMENTS.md` is ever staged by this run.
3. Resolve the window: `WEEKS` = `$ARGUMENTS` if it parses as a positive number, else `1`.
4. Locate the corpus. Session transcripts live **outside every workspace**, so `Bash` and built-ins
   are legal here and terse-sharp is not:
   - `~/.claude/projects/<project-slug>/<session-uuid>.jsonl` — one file per session;
   - `~/.claude/projects/<project-slug>/<session-uuid>/subagents/agent-*.jsonl` — one per subagent,
     and these are where delegation cost hides;
   - if `CLAUDE_CONFIG_DIR` is set and its `projects/` exists and differs, scan that too.
5. `TaskCreate` one task per phase.

---

## M1 — No activity is a legitimate, reported outcome

```bash
find ~/.claude/projects -name '*.jsonl' -mtime -$((WEEKS*7)) | wc -l
```

**Zero files in the window → say so and stop.** Report the window, the roots searched, and the date of
the most recent transcript found outside it. Make **no** edit to `IMPROVEMENTS.md`, no commit, no push.
That is the whole run; it is a success, not a failure.

---

## M2 — Measure, with one deterministic script

Write the script to a **temporary directory** (`$TMPDIR` / `%TEMP%`), never into the repository, and
run it with `python`. One script, one pass, comparable across runs — an eyeballed sample is not this
phase.

```python
import json, os, re, sys, time, collections

weeks = float(sys.argv[1]) if len(sys.argv) > 1 else 1.0
cutoff = time.time() - weeks * 7 * 86400
roots, seen_roots = [], set()
for candidate in (os.path.expanduser('~/.claude/projects'),
                  os.path.join(os.environ.get('CLAUDE_CONFIG_DIR', ''), 'projects')):
    if not candidate or not os.path.isdir(candidate):
        continue
    real = os.path.realpath(candidate).lower()
    if real in seen_roots:
        continue
    seen_roots.add(real)
    roots.append(candidate)

BUILTIN = {'Read', 'Write', 'Edit', 'NotebookEdit', 'Grep', 'Glob', 'Bash',
           'WebFetch', 'WebSearch', 'Agent', 'Task', 'TodoWrite', 'Skill', 'ToolSearch'}
SHELL_TEXT = re.compile(r'\b(grep|rg|cat|head|tail|sed|awk|ls|find|type)\b')
SHELL_GIT = re.compile(r'\bgit\s+(status|diff)\b')
SHELL_DOTNET = re.compile(r'\bdotnet\s+(build|test|clean|format)\b')

calls = collections.Counter()
result_chars = collections.Counter()
errors = collections.Counter()
per_project = collections.defaultdict(collections.Counter)
bash_kind = collections.Counter()
dupes = collections.Counter()
steers = collections.Counter()
nonevent = collections.Counter()
payloads = []
usage = collections.Counter()
sessions = set()

def walk():
    seen_files = set()
    for root in roots:
        for folder, _, names in os.walk(root):
            for name in names:
                if not name.endswith('.jsonl'):
                    continue
                path = os.path.join(folder, name)
                key = os.path.realpath(path).lower()
                if key in seen_files:
                    continue
                try:
                    if os.path.getmtime(path) >= cutoff:
                        seen_files.add(key)
                        rel = os.path.relpath(path, root)
                        yield path, rel.split(os.sep)[0]
                except OSError:
                    pass

for path, project in walk():
    sessions.add(path)
    pending, seen = {}, collections.Counter()
    with open(path, encoding='utf-8', errors='replace') as handle:
        for line in handle:
            try:
                record = json.loads(line)
            except ValueError:
                continue
            message = record.get('message') or {}
            stats = message.get('usage')
            if isinstance(stats, dict):
                for key in ('input_tokens', 'output_tokens',
                            'cache_creation_input_tokens', 'cache_read_input_tokens'):
                    usage[key] += stats.get(key) or 0
            blocks = message.get('content')
            if not isinstance(blocks, list):
                continue
            for block in blocks:
                if not isinstance(block, dict):
                    continue
                if block.get('type') == 'tool_use':
                    tool = block.get('name') or '?'
                    calls[tool] += 1
                    per_project[project][tool] += 1
                    pending[block.get('id')] = (tool, project)
                    payload = json.dumps(block.get('input') or {}, sort_keys=True)[:4000]
                    seen[(tool, payload)] += 1
                    if tool == 'Bash':
                        command = (block.get('input') or {}).get('command', '')
                        kind = ('git status/diff' if SHELL_GIT.search(command) else
                                'dotnet build/test/format' if SHELL_DOTNET.search(command) else
                                'shell text tool' if SHELL_TEXT.search(command) else
                                'sleep/wait' if re.search(r'\bsleep\b', command) else 'other')
                        bash_kind[kind] += 1
                elif block.get('type') == 'tool_result':
                    tool, proj = pending.pop(block.get('tool_use_id'), ('?', project))
                    content = block.get('content')
                    text = content if isinstance(content, str) else json.dumps(content or '')
                    size = len(text)
                    result_chars[tool] += size
                    payloads.append((size, tool, proj))
                    if block.get('is_error') or text.lstrip().startswith('ERROR '):
                        errors[tool] += 1
                    if 'narrow with' in text or '(truncated=true' in text:
                        steers[tool] += 1
                    if '(truncated=false' in text:
                        nonevent[tool] += 1
    for (tool, _payload), count in seen.items():
        if count > 1:
            dupes[tool] += count - 1

payloads.sort(reverse=True)
total_calls = sum(calls.values())
builtin_calls = sum(count for tool, count in calls.items() if tool in BUILTIN)
mcp_calls = sum(count for tool, count in calls.items() if tool.startswith('mcp__'))

def line(label, value):
    print(f'{label:<34}{value}')

print(f'== window {weeks} week(s), {len(sessions)} transcripts, roots: {len(roots)}')
line('tool calls', total_calls)
line('built-in calls', f'{builtin_calls} ({builtin_calls * 100 // max(total_calls, 1)}%)')
line('mcp calls', f'{mcp_calls} ({mcp_calls * 100 // max(total_calls, 1)}%)')
line('tool-result chars', f'{sum(result_chars.values()):,} (~{sum(result_chars.values()) // 4:,} tok)')
for key, value in usage.most_common():
    line(key, f'{value:,}')
print('\n== top tools by result payload')
for tool, chars in result_chars.most_common(20):
    print(f'{tool:<34}{calls[tool]:>5} calls  {chars:>10,} ch  ~{chars // 4:>8,} tok  '
          f'{chars // max(calls[tool], 1):>7,} ch/call  err={errors[tool]}  '
          f'steers={steers[tool]}  truncated=false noise={nonevent[tool]}')
print('\n== Bash breakdown')
for kind, count in bash_kind.most_common():
    print(f'{kind:<34}{count}')
print('\n== repeated identical calls within a session')
for tool, count in dupes.most_common(15):
    print(f'{tool:<34}{count}')
print('\n== 15 largest single tool results')
for size, tool, project in payloads[:15]:
    print(f'{size:>9,} ch  {tool:<32}{project}')
print('\n== per project')
for project, counter in sorted(per_project.items(), key=lambda item: -sum(item[1].values())):
    total = sum(counter.values())
    top = ', '.join(f'{tool}x{count}' for tool, count in counter.most_common(6))
    print(f'{total:>6}  {project[:44]:<46}{top}')
```

Run it as `python <script> <WEEKS>`. Read the whole output. **Do not** re-derive any number by hand
afterwards — the script is the measurement of record, and the next run must be comparable to this one.

---

## M3 — Read the evidence behind the top offenders

The aggregate says *what* is expensive; only the transcript says *why*. For the **top five** cost
centres from M2 — usually a tool with a huge chars/call, a `Bash` class, a duplicated call, or a
subagent file — open the specific `.jsonl` and read enough of the surrounding turns to state the
cause. Use `python` one-liners or `Read` on the transcript (it is outside every workspace, so
built-ins are legal); never paste what you find.

Look for these classes, each of which has produced a real shipped improvement in this repo before:

| Class | Signature in the data |
|---|---|
| **Fallback** | a built-in used where an MCP tool existed — `Grep`/`Read` on `.cs`, `Bash: git status`/`git diff`, `dotnet build`/`test`/`format` in a shell, `ls`/`cat`/`sed`. Count them; each is a product or discoverability defect, never "just a habit" |
| **Round trip** | the same answer costing ≥2 calls — outline → source → usages, read → read the same file wider, a truncation steer immediately followed by the same call with a bigger cap |
| **Payload** | tokens returned and never used — whole-file reads where a section was wanted, a listing whose columns nobody read, echoed arguments, a diff on a success response |
| **Truncation near-miss** | a steer whose overflow was a few percent of the cap, so the caller always spends a second call |
| **Duplicate work** | identical tool input repeated inside one session — a re-read after an edit, a re-search with the same query, a status re-check |
| **Error round trip** | `ERROR`/`is_error` results, especially ones needing a retry with different arguments: `AmbiguousWorkspace`, `SymbolNotFound` on a hand-written id, `oldText matched 0 times`, `InvalidArgument` |
| **Speed** | blind `sleep`, a poll loop that only watches for an artifact, a 10-minute timeout on a 10-second command, a serial sequence that had no dependency, a cold-start cost paid per call |
| **Delegation** | subagent transcripts: what the fan-out cost in tokens versus what its report was worth, and whether the subagent used built-ins on code its parent had an MCP for |
| **Context** | `cache_read` vs `cache_creation` ratio, a session that re-primed context repeatedly, an enormous system/skill payload loaded for a one-call task |
| **Productivity** | permission denials and the retry that followed, a plan re-derived because an earlier answer was not written down, a gate the agent skipped and had to redo |

---

## M4 — Deduplicate against what is already logged

`read_text IMPROVEMENTS.md headings=true`, then read `## Open`, the shipped table, and
`## Known limitations`. A candidate that matches an existing row is **not** a new row:

- already **open** → do not duplicate; if this scan measured it again, **strengthen the existing row**
  with the new number and the new corpus (`edit_text` on that row), and say so in the report;
- already **shipped** → the waste persisting means either the fix does not cover this case (a new row,
  explicitly referencing the shipped id) or the agent is not using the shipped tool (a
  **discoverability** row against `SKILL.md`/`README.md`/`CLAUDE.md`, which is the class this repo
  ranks highest — `I103` was exactly this);
- already **rejected / known limitation** (`I91`, `I92`, the trigram index, the size-aware LRU) → do
  not re-propose it. Add the new measurement to that limitation's evidence only if it materially
  changes the ratio the decision was made on.

---

## M5 — Write the rows

1. Next id = highest existing `I<number>` + 1, continuing the sequence — never reuse one.
2. Append to the `## Open` table (`edit_text`, anchored on text read in this run) in the file's own
   four-column format:
   `| **I<n>** <the finding, with its number> | <tool> | <proposed change> | <expected saving> |`
   - **Finding** — what was measured, in how many sessions, over which window. Bold the headline
     number.
   - **Tool** — the terse-sharp tool, or the document (`SKILL.md`, `README.md`, `CLAUDE.md`) when the
     lever is discoverability rather than capability.
   - **Proposed change** — one concrete change. "Make it better" is not a proposal.
   - **Expected saving** — derived from the measurement, stated as calls and tokens.
3. A finding whose lever is **not** TerseSharp (a harness setting, a skill's wording, a hook, a prompt
   habit) goes under a `## Agent workflow — open` heading, created once, immediately after `## Open`,
   with the same four columns. Do not silently drop it into the product backlog: the ranking rules in
   `CLAUDE.md` are about the tool surface and would mis-prioritise it.
4. Rank the new rows the way this repo ranks: **fixing a fallback outranks a new capability**;
   **improving an existing tool or response format outranks adding a tool**; a saving that cannot be
   measured is not accepted. Put the highest-cost row first.
5. Record the scan itself at the end of the run's block, in one line, so successive runs are
   comparable: window, transcript count, total tool calls, built-in share, total tool-result tokens.
6. Cap the run at the **top 10** rows by measured cost. More than that is a list nobody acts on; say
   in the report how many candidates were measured and dropped, and their combined cost — a silent
   cap is the one thing this repo forbids.

---

## M6 — Verify, commit, push

1. `read_text IMPROVEMENTS.md section="## Open"` (and the new workflow section) — the table still
   parses, ids are unique and sequential, every row has four columns and a number.
2. **Privacy re-read:** every new row, checked once more against gate 2 at the top. No path inside
   another repo, no type name, no prompt text, no quoted result. This is the last chance before the
   file is public.
3. `changed_files` — `IMPROVEMENTS.md` must be the **only** path this run touched. No build, no test
   run: nothing else changed, and `IMPROVEMENTS.md` is not shipped in the package.
4. `Bash: git add IMPROVEMENTS.md && git commit -m "Log I<n>–I<m> from the <N>-week session scan"`
   (body: the corpus line from M5.5). **No `Co-Authored-By`.**
5. `Bash: git show --stat HEAD` then `git push origin main`.

---

## M7 — Report

| Section | Content |
|---|---|
| Corpus | window in weeks, transcripts scanned, projects covered (slugs only), total tool calls, built-in vs MCP share, total tool-result tokens, output/cache token totals |
| Top waste | the five cost centres from M2, each with its number |
| New rows | every id written, with its one-line finding and expected saving, highest cost first |
| Strengthened | existing rows given a new measurement instead of a duplicate |
| Dropped | candidates measured but not logged (over the cap, already rejected, unmeasurable), with their combined cost — never silent |
| Privacy | confirmation that no row names a path, type, prompt or result from another project |
| Commit | SHA and the single path staged |
| Trend | this run's corpus line against the previous run's, when one exists |

If M1 found nothing, the report is just: window, roots, most recent transcript outside the window, and
the statement that nothing was changed.
