---
description: Mine every Claude Code session across all projects for token, character, latency, memory and productivity waste down to the individual character, deep-research the state of the art in agent accuracy, log every measured finding as an open row in IMPROVEMENTS.md, then commit and push.
argument-hint: "[weeks to scan, default 1]"
---

# 🚫 HARD GATE — findings are **measured to the character**, never impressionistic, and never leak another project's content.

`$ARGUMENTS` — a number of **weeks** to scan. Absent or unparseable → **1 week**. Nothing else takes
input from the user; do not ask, do not confirm the window.

**Four gates that outrank everything else in this command:**

1. **A finding without a number is not a finding.** "Reads feel wasteful" is banned. "36 `Read` calls
   in one session, 214 KB of tool results, ~53 500 tokens, against 10 `search_text` calls" is a
   finding. Every row carries a count, a byte/token/millisecond figure and the corpus it came from.
2. **Nothing is too small to count.** This command's whole reason to exist is the long tail: a
   trailing space on every line of every response, a two-character column separator, a repeated
   `(truncated=false)`, a folded absolute path prefix, one redundant word in a tool `[Description]`
   that is re-sent on every single request of every single session. A saving of 8 characters per line
   over 40 000 lines is **80 000 characters ≈ 20 000 tokens** and outranks most new tools. "Too small
   to log" is a banned phrase here — small findings are **aggregated into one row per family**, never
   dropped (M8.4).
3. **Never copy content out of another project's transcript.** These sessions are from a private
   employer codebase (`cTraderDev`, `ctd-worktrees-*`, `cTraderAutomateApi`, …) and `IMPROVEMENTS.md`
   is pushed to a **public** repository. A row may name: tool names, call counts, byte sizes, token
   estimates, millisecond timings, error codes, and a project's **directory slug**. A row may **never**
   contain: source code, file paths inside those repos, type or member names, ticket text, user
   prompts, or a quoted tool result. When in doubt, state the shape (`a 40 KB whole-file read of a
   test file`) not the value. The measurement script enforces a mechanical version of this rule and
   you enforce the rest by hand in M9.2.
4. **Character economy is a claim about tokens, so state both.** Chars are what the script can count;
   tokens are what is paid. Convert at ~4 chars/token for prose and ~3 for dense punctuation, say
   which you used, and prefer trims that delete **whole tokens** (a word, a separator, a repeated
   line prefix, a column) over trims that only shorten one — deleting 3 characters from the middle of
   a word usually saves nothing at all.

**Also banned:** `AskUserQuestion`, `ExitPlanMode`, editing any file other than `IMPROVEMENTS.md`,
`git add -A`, a `Co-Authored-By:` trailer, and writing any script anywhere inside the repository.

**No review.** `code-review-gate`, `/code-review`, `caveman:cavecrew-reviewer` and every other review
path are explicitly waived for this command by standing user instruction. Do not spawn one and do not
report the phase as degraded.

**Subagents:** banned for every phase that touches a transcript (M2–M5) — a reviewer adds nothing to a
log scan and doubles the private-content exposure. **Permitted, and expected, in M6 only** (the
research fan-out), which reads the public web and is handed **no transcript content whatsoever** —
not a path, not a slug, not a quoted result. A research subagent prompt that contains anything mined
from a session is a breach of gate 3.

---

## M0 — Preflight

1. Invoke the `terse-sharp` skill. `workspace_status` / `load_workspace TerseSharp.slnx`; pass
   `workspace: "TerseSharp"` on terse-sharp calls.
2. `Bash: git fetch origin && git pull --ff-only` on `main` (history/index mutation — the one legal
   `Bash` class here). `changed_files` — record pre-existing dirt; only `IMPROVEMENTS.md` is ever
   staged by this run.
3. Resolve the window: `WEEKS` = `$ARGUMENTS` if it parses as a positive number, else `1`.
4. Locate the corpus. Session transcripts live **outside every workspace**, so `Bash` and built-ins
   are legal here and terse-sharp is not:
   - `~/.claude/projects/<project-slug>/<session-uuid>.jsonl` — one file per session;
   - `~/.claude/projects/<project-slug>/<session-uuid>/subagents/agent-*.jsonl` — one per subagent,
     and these are where delegation cost hides;
   - if `CLAUDE_CONFIG_DIR` is set and its `projects/` exists and differs, scan that too.
5. `TaskCreate` one task per phase, M1 through M10.

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
phase. It measures four axes at once: **tokens**, **characters** (the trim ledger), **latency**, and
**failure**.

```python
import collections, datetime, json, os, re, sys, time

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
SLEEP = re.compile(r'\bsleep\s+(\d+)')
ABSPATH = re.compile(r'[A-Za-z]:\\[\w.\-\\]+|/(?:Users|home|mnt|var|opt)/[\w.\-/]+')
ERRCODE = re.compile(r'\bERROR\s+([A-Za-z][A-Za-z0-9_]*)')
NOISE = ('(truncated=false', 'truncated=false)', 'remedy:', 'EXACT', 'HEURISTIC')

calls = collections.Counter()
input_chars = collections.Counter()
result_chars = collections.Counter()
errors = collections.Counter()
error_codes = collections.Counter()
per_project = collections.defaultdict(collections.Counter)
bash_kind = collections.Counter()
dupes = collections.Counter()
steers = collections.Counter()
noise = collections.Counter()
trim = collections.Counter()
line_freq = collections.Counter()
line_projects = collections.defaultdict(set)
durations = collections.defaultdict(list)
payloads = []
usage = collections.Counter()
sessions = set()
slept = 0
fat_timeouts = 0

def stamp(record):
    raw = record.get('timestamp')
    if not isinstance(raw, str):
        return None
    try:
        return datetime.datetime.fromisoformat(raw.replace('Z', '+00:00')).timestamp()
    except ValueError:
        return None

def prune():
    if len(line_freq) <= 300_000:
        return
    for text, count in list(line_freq.items()):
        if count < 2:
            del line_freq[text]
            line_projects.pop(text, None)

def audit(text, project):
    blank = indent = trail = 0
    local = collections.Counter()
    for raw in text.split('\n'):
        body = raw.rstrip()
        trail += len(raw) - len(body)
        if not body:
            blank += len(raw) + 1
            continue
        indent += len(body) - len(body.lstrip())
        local[body] += 1
        if len(body) >= 12:
            line_freq[body] += 1
            seen = line_projects[body]
            if len(seen) < 6:
                seen.add(project)
    trim['trailing whitespace'] += trail
    trim['blank lines'] += blank
    trim['leading indent'] += indent
    trim['repeated lines in one payload'] += sum((c - 1) * (len(s) + 1)
                                                 for s, c in local.items() if c > 1)
    trim['absolute path text'] += sum(len(m) for m in ABSPATH.findall(text))
    for token in NOISE:
        trim[f'literal {token!r}'] += text.count(token) * len(token)
    prune()

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
            at = stamp(record)
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
                    arguments = block.get('input') or {}
                    calls[tool] += 1
                    per_project[project][tool] += 1
                    pending[block.get('id')] = (tool, project, at)
                    payload = json.dumps(arguments, sort_keys=True)
                    input_chars[tool] += len(payload)
                    seen[(tool, payload[:4000])] += 1
                    if tool == 'Bash':
                        command = arguments.get('command', '') or ''
                        kind = ('git status/diff' if SHELL_GIT.search(command) else
                                'dotnet build/test/format' if SHELL_DOTNET.search(command) else
                                'shell text tool' if SHELL_TEXT.search(command) else
                                'sleep/wait' if SLEEP.search(command) else 'other')
                        bash_kind[kind] += 1
                        slept += sum(int(v) for v in SLEEP.findall(command))
                elif block.get('type') == 'tool_result':
                    tool, proj, started = pending.pop(block.get('tool_use_id'),
                                                      ('?', project, None))
                    content = block.get('content')
                    text = content if isinstance(content, str) else json.dumps(content or '')
                    size = len(text)
                    result_chars[tool] += size
                    payloads.append((size, tool, proj))
                    audit(text, proj)
                    if started and at and at >= started:
                        elapsed = at - started
                        durations[tool].append(elapsed)
                        if tool == 'Bash' and elapsed < 30:
                            fat_timeouts += 1
                    if block.get('is_error') or text.lstrip().startswith('ERROR '):
                        errors[tool] += 1
                        found = ERRCODE.search(text)
                        if found:
                            error_codes[found.group(1)] += 1
                    if 'narrow with' in text or '(truncated=true' in text:
                        steers[tool] += 1
                    if '(truncated=false' in text:
                        noise[tool] += 1
    for (tool, _payload), count in seen.items():
        if count > 1:
            dupes[tool] += count - 1

payloads.sort(reverse=True)
total_calls = sum(calls.values())
total_chars = sum(result_chars.values())
builtin_calls = sum(count for tool, count in calls.items() if tool in BUILTIN)
mcp_calls = sum(count for tool, count in calls.items() if tool.startswith('mcp__'))

def quantile(values, fraction):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]

def line(label, value):
    print(f'{label:<34}{value}')

print(f'== window {weeks} week(s), {len(sessions)} transcripts, roots: {len(roots)}')
line('tool calls', total_calls)
line('built-in calls', f'{builtin_calls} ({builtin_calls * 100 // max(total_calls, 1)}%)')
line('mcp calls', f'{mcp_calls} ({mcp_calls * 100 // max(total_calls, 1)}%)')
line('tool-result chars', f'{total_chars:,} (~{total_chars // 4:,} tok)')
line('tool-input chars', f'{sum(input_chars.values()):,}')
line('measured tool wall time', f'{sum(sum(v) for v in durations.values()) / 3600:.2f} h')
line('seconds slept in Bash', slept)
for key, value in usage.most_common():
    line(key, f'{value:,}')

print('\n== top tools by result payload')
for tool, chars in result_chars.most_common(25):
    spent = durations[tool]
    print(f'{tool:<34}{calls[tool]:>5} calls  {chars:>10,} ch  ~{chars // 4:>8,} tok  '
          f'{chars // max(calls[tool], 1):>7,} ch/call  in={input_chars[tool]:>8,} ch  '
          f'p50={quantile(spent, 0.5):>6.1f}s  p95={quantile(spent, 0.95):>7.1f}s  '
          f'tot={sum(spent) / 60:>7.1f}m  err={errors[tool]}  steers={steers[tool]}  '
          f'noise={noise[tool]}')

print('\n== trim ledger (characters that carried no information)')
for label, chars in trim.most_common():
    share = chars * 100 / max(total_chars, 1)
    print(f'{label:<34}{chars:>12,} ch  ~{chars // 4:>9,} tok  {share:>5.2f}% of all output')

print('\n== boilerplate lines (>=5 uses, >=3 projects, no path text)')
shown = 0
for text, count in line_freq.most_common(400):
    if count < 5 or len(line_projects[text]) < 3 or ABSPATH.search(text):
        continue
    print(f'{count:>6}x  {len(text) * count:>9,} ch  {text[:60]!r}')
    shown += 1
    if shown == 20:
        break

print('\n== Bash breakdown')
for kind, count in bash_kind.most_common():
    print(f'{kind:<34}{count}')
line('bash calls under 30 s', fat_timeouts)

print('\n== repeated identical calls within a session')
for tool, count in dupes.most_common(15):
    print(f'{tool:<34}{count}')

print('\n== error codes')
for code, count in error_codes.most_common(20):
    print(f'{code:<34}{count}')

print('\n== 15 largest single tool results')
for size, tool, project in payloads[:15]:
    print(f'{size:>9,} ch  {tool:<32}{project}')

print('\n== per project')
for project, counter in sorted(per_project.items(), key=lambda item: -sum(item[1].values())):
    total = sum(counter.values())
    top = ', '.join(f'{tool}x{count}' for tool, count in counter.most_common(6))
    print(f'{total:>6}  {project[:44]:<46}{top}')

print(f'\n== corpus  weeks={weeks} transcripts={len(sessions)} calls={total_calls} '
      f'builtin={builtin_calls * 100 // max(total_calls, 1)}% '
      f'resulttok={total_chars // 4} trimtok={sum(trim.values()) // 4}')
```

Run it as `python <script> <WEEKS>`. Read the **whole** output. **Do not** re-derive any number by hand
afterwards — the script is the measurement of record, and the next run must be comparable to this one.

Two things the script deliberately does, and you must not undo:

- **The boilerplate section is privacy-filtered**, not privacy-free. It prints a line only when it
  appears in ≥3 different projects and contains no path-shaped text, which is what makes it *tool
  framing* rather than *user content*. Never widen that filter, never print a line it suppressed, and
  never paste one of these lines into `IMPROVEMENTS.md` unless it is unmistakably TerseSharp's own
  response framing.
- **`line_freq` is pruned at 300 000 entries.** A multi-month window over a large corpus otherwise
  costs gigabytes. If you widen the window past ~8 weeks, say in the report that the boilerplate
  section is a floor, not a total.

---

## M3 — The micro-trim pass: every character in TerseSharp's own responses

M2's trim ledger says how many characters carried no information **across all tools**. This phase
attributes them to **TerseSharp's own response format**, which is the only surface this repo can
change, and hunts the ones a whole-corpus counter cannot see.

Take the widest real response of each `mcp__terse-sharp__*` tool that appears in the corpus — largest
by chars, from the M2 payload list — and read it character by character against this checklist. Each
line is a class that has already produced a shipped saving in this repo, and each one is worth a row
on its own.

| Class | What to look for | How to price it |
|---|---|---|
| **Framing** | a header restating the request, an echoed argument, a tool name at the start of its own answer, a trailing summary that repeats the first line | chars × calls of that tool in the corpus |
| **Separators** | `  ` double spaces used as columns, ` \| ` pipes, `---` rules, a `:` after a label that a space would carry, `, ` where `,` reads the same | (chars saved per line) × (lines per response) × (calls) |
| **Column value** | a column whose value is constant across every record, derivable from the request, or never read by the agent afterwards — check the *next* tool call to see whether it was used | full column width × record count |
| **Path shape** | a repeated directory prefix, a redundant `./`, an extension the tool already implies, an absolute path where relative round-trips | measured from the ledger's `absolute path text` line |
| **Constant tags** | `EXACT` on a tool that can only ever answer `EXACT`, `(truncated=false)`, `errors=0 warnings=0` on a tool that fails loudly, `remedy:` on a success | occurrences × length, straight from the ledger |
| **Number format** | thousands separators, trailing `.0`, padded columns, a millisecond figure with 6 significant digits, a byte count where KB would do | usually 2–6 chars × every record |
| **Blank lines** | a blank line between sections that a single newline separates just as well, a trailing newline run | ledger `blank lines` |
| **Indentation** | leading spaces that survive `TextCompressor.Source`, alignment padding | ledger `leading indent` |
| **Plural prose** | `N results found for` where `N` alone is unambiguous, `no matches were found` where `0` is the answer, an English sentence where a token would do | chars × calls |
| **Repeated line prefix** | every record starting with the same file, symbol or project — a grouped form pays the prefix once | (prefix length + 1) × (records − groups) |
| **Verbose-only leakage** | anything the success path emits that only `verbose=true` should — the HARD GATE in `CLAUDE.md` calls this out and it is still the highest-yield class | full removed body × success calls |

Then audit **the surface itself**, which is paid on every request of every session whether a tool is
called or not:

1. `search_regex` over `src/TerseSharp.Server/Tools/` for `\[Description\(` with a high `maxResults`,
   and sum the returned line lengths. That is the floor of what the 86-tool advertised surface costs
   **per request**. Multiply by the number of assistant turns in the corpus to price it.
2. Flag every description that: repeats the tool's own name, explains *how* instead of *what it
   returns and which built-in it replaces*, lists parameters the schema already declares, or spends
   words on a case the `remedy:` already teaches. A word cut there is a word cut from every request
   forever — this is the single highest-leverage character in the product.
3. Do the same for `src/TerseSharp.Server/Assets/SKILL.md`: `read_text headings=true` for the map,
   then price each section by its byte size. The skill is loaded whole into an agent's context.
4. **Do not** propose deleting a `Replaces Bash …` prefix — `ToolCensusE2ETests` enrols the guard from
   it. Cutting it silently un-enrols the tool. Record that constraint in the row's `Rejected` cell.

Every candidate from this phase becomes a row, or is folded into the family aggregate (M8.4). A trim
you priced and then dropped without saying so is the one outcome this command forbids.

---

## M4 — The performance and memory pass

Tokens are half the prime directive; the other half is speed, and a server that answers cheaply but
slowly loses the session anyway.

**Latency, from M2's `p50`/`p95`/`tot` columns.** For every `mcp__terse-sharp__*` tool in the top 25:

- a `p95` more than 5× its `p50` is a **cold path** — first call after a load, an eviction, an
  analyzer assembly load, a lazily built compilation. Name the trigger.
- a `tot` in the tens of minutes on a read tool is a **budget** problem regardless of `p50`.
- compare against the built-in it replaces where the corpus has both. **A terse tool slower than the
  `Grep` it replaces is a product defect even when it returns fewer tokens** — the prime directive is
  "save tokens, increase speed", conjunction not disjunction.
- `run_tests` / `build`: total wall time and how much of the session it blocked. A long serial wait
  with no per-project breakdown is already `I125`; strengthen it rather than duplicating.

**Memory.** The server is a long-lived stdio process holding MSBuild workspaces:

1. `doctor` — it prints every live `terse`/`testhost` pid with resident megabytes (shipped as `I100`).
   Record RSS now, after a `load_workspace`, and after an `unload_workspace`.
2. Divide by `documents=` from `workspace_status` for a cost-per-document figure, and compare across
   the workspaces the corpus shows being loaded. The retainer is Roslyn's compilation tracker on the
   live `Solution`; a number quoted without forcing a gen2 collection first is not a leak measurement.
3. Look for the LRU behaving badly in the corpus: repeated `load_workspace` of the same solution
   inside one session (an eviction that cost a full reload), or `AmbiguousWorkspace` errors implying
   several roots resident at once.
4. On the source side, price the allocation gate in `CLAUDE.md` against reality: pick the two hottest
   per-file/per-line paths the corpus exercises most (usually `TextSearchService`, `FileService`,
   `ResponseBuilder`, `OutlineService`) and `analyze` them down to `info`. `CA1859`, `CA1822`,
   `CA1865` and friends are real per-call costs at solution scale. Do **not** fix anything here — this
   command edits one markdown file. Log it.

Anything measured in this phase is a row with a **millisecond or megabyte** figure in its
`Expected saving` cell, not a token figure. Mixing the two units in one cell is how a saving stops
being comparable across runs.

---

## M5 — Read the evidence behind the top offenders

The aggregate says *what* is expensive; only the transcript says *why*. For the **top eight** cost
centres from M2–M4 — a tool with a huge chars/call, a `Bash` class, a duplicated call, a `p95` spike,
a subagent file — open the specific `.jsonl` and read enough of the surrounding turns to state the
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
| **Error round trip** | `ERROR`/`is_error` results, especially ones needing a retry with different arguments: `AmbiguousWorkspace`, `SymbolNotFound` on a hand-written id, `oldText matched 0 times`, `InvalidArgument`. The M2 error-code histogram ranks these for you |
| **Speed** | blind `sleep`, a poll loop that only watches for an artifact, a 10-minute timeout on a 10-second command, a serial sequence that had no dependency, a cold-start cost paid per call |
| **Delegation** | subagent transcripts: what the fan-out cost in tokens versus what its report was worth, and whether the subagent used built-ins on code its parent had an MCP for |
| **Context** | `cache_read` vs `cache_creation` ratio, a session that re-primed context repeatedly, an enormous system/skill payload loaded for a one-call task |
| **Productivity** | permission denials and the retry that followed, a plan re-derived because an earlier answer was not written down, a gate the agent skipped and had to redo |
| **Accuracy** | an answer the agent acted on and later had to undo — a confident wrong result, a stale read, a claim the tool could not prove. This class is scored in M6, because a wrong answer costs more than any payload |

---

## M6 — Deep research: what the field knows about agent accuracy and productivity

The corpus says what *this* agent wasted. It cannot say what a better-designed tool surface would have
avoided in the first place. This phase is **mandatory**, it is not a literature review for its own
sake, and it ends in rows.

**Scope — search for methods, then test them against the measured corpus:**

- tool and schema design for LLM agents: naming, description wording, parameter count and ordering,
  required-vs-optional, enum-vs-free-text, error-message design, worked examples in the schema, how
  tool-choice accuracy degrades with surface size;
- context engineering: what to put in a system prompt versus a tool response, progressive disclosure,
  just-in-time retrieval versus preloading, prompt-cache-friendly ordering, context rot and the
  accuracy curve against context length, compaction and note-taking;
- response format: structured versus prose returns, how truncation should be signalled, confidence
  and provenance markers, refusal-to-guess as an accuracy device;
- verification: adversarial self-check, execution as arbiter, judge panels, when a second opinion pays
  for itself;
- multi-agent economics: measured token multipliers of fan-out, when delegation is negative-value;
- benchmark evidence: token/latency/accuracy numbers from published agent evaluations of MCP servers
  and semantic-code-navigation tools; failure modes measured in competing servers.

**Method — a fan-out, adversarially verified:**

1. Spawn **at least eight** parallel research subagents, one per scope area above, each briefed to
   return claims with sources and dates. Give them **zero** transcript content — they get the topic
   only (gate 3). A subagent that cannot cite is returning an opinion.
2. Prefer primary and dated sources: vendor engineering documentation, published evaluations, papers
   with numbers. Reject undated blog assertions and anything whose only support is another agent's
   summary.
3. **Adversarially verify every claim you intend to act on**: a second agent whose instruction is to
   *refute* it, and a check against the measured corpus. A claim that contradicts what M2–M5 measured
   in this codebase loses to the measurement — the corpus is the arbiter, not the source.
4. Discard anything already true of TerseSharp. The output of this phase is only the delta.

**Conversion — a research finding becomes a row only if it is actionable here.** Name the concrete
change: a tool description reworded, a parameter added or removed, a response format changed, a
`remedy:` that teaches the retry, a `SKILL.md` section rewritten, a default flipped. State the
expected accuracy or productivity gain and how the next run would *observe* it — a row whose success
can only be felt is not accepted. A method that is real but has no lever in this repo goes in the
report's research section, not in the backlog.

---

## M7 — Deduplicate against what is already logged

`read_text IMPROVEMENTS.md headings=true`, then read `## Open` and `## Closed` in full. **The file is
exactly two tables and nothing else** — there is no `Known limitations` section, no per-task narrative,
no third heading, and adding one fails `BacklogShapeTests`. A candidate that matches an existing row is
**not** a new row:

- already **open** → do not duplicate; if this scan measured it again, **strengthen the existing row**
  with the new number and the new corpus (`edit_text` on that row), and say so in the report;
- already **closed as shipped** → the waste persisting means either the fix does not cover this case
  (a new row, explicitly referencing the shipped id) or the agent is not using the shipped tool (a
  **discoverability** row whose `Tool` cell names `SKILL.md`/`README.md`/`CLAUDE.md`, which is the
  class this repo ranks highest — `I103` was exactly this);
- already **closed as refuted / not soundly implementable** (`I91`, `I92`, `I104`, the trigram index,
  the size-aware LRU) → do not re-propose it. Add the new measurement to that row's `Outcome` only if
  it materially changes the ratio the decision was made on;
- a candidate whose approach appears in an open row's **`Rejected`** cell is already refuted for that
  row. Do not propose it again; propose a different mechanism or strengthen the row.

---

## M8 — Write the rows

1. Next id = highest existing `I<number>` + 1, continuing the sequence — never reuse one.
2. Append to the `## Open` table with `edit_text`, anchored on text read in this run, in the file's own
   **five**-column format — the fifth column is `Rejected` and a row missing it fails
   `BacklogShapeTests`, because GitHub Flavored Markdown pads a short row silently:

   `| **I<n>** <the finding> | <tool> | <proposed change> | <expected saving> | <approaches already refuted for this row, or —> |`

   - **Finding** — what was measured, in how many sessions, over which window. Bold the headline
     number. One row, one line: not a paragraph, not three.
   - **Tool** — the terse-sharp tool, or the document (`SKILL.md`, `README.md`, `CLAUDE.md`,
     `.claude/commands/…`) when the lever is discoverability or workflow rather than capability.
     There is no separate workflow table; the `Tool` cell carries that distinction.
   - **Proposed change** — one concrete change. "Make it better" is not a proposal.
   - **Expected saving** — derived from the measurement, in calls and tokens, or in milliseconds and
     megabytes for an M4 row. Never both units in one cell.
   - **Rejected** — every approach this run already refuted for that row, so it is never re-attempted;
     `—` when there is none. A constraint that blocks the obvious fix belongs here (for example: the
     `Replaces Bash …` prefix cannot be shortened without un-enrolling the guard census).
3. Rank the way this repo ranks: **fixing a fallback outranks a new capability**; **improving an
   existing tool or response format outranks adding a tool**; a saving that cannot be measured is not
   accepted. Highest measured cost first.
4. **No cap, and no silent drop — the floor and the aggregation rule are the discipline.**
   - A candidate clears the floor on its own if it is worth **≥200 tokens per session**, **≥1 call per
     session**, **≥500 ms per call**, or a named accuracy gain.
   - Everything below the floor is **aggregated by family** into one row — one `trailing whitespace
     and alignment padding across N tools` row, one `constant tags on success responses` row, one
     `tool-description wording` row — carrying the **summed** measurement from the M2 ledger and the
     list of tools it covers. Aggregation is how gate 2 is satisfied without producing a list nobody
     acts on.
   - Anything genuinely dropped is named in the report with its measured cost and the reason.
5. Record the scan itself in the report — window, transcript count, total tool calls, built-in share,
   total tool-result tokens, trim-ledger tokens, measured wall time — so successive runs are
   comparable. **It does not go in the file:** prose in `IMPROVEMENTS.md` fails the shape gate.

---

## M9 — Verify, commit, push

1. `read_text IMPROVEMENTS.md section="## Open"` — the table still parses, ids are unique and
   sequential, and **every row has exactly five cells**. Then `read_text … headings=true`: exactly
   three headings, `# Improvements backlog` / `## Open` / `## Closed`, in that order, nothing else.
2. **Privacy re-read:** every new row, checked once more against gate 3. No path inside another repo,
   no type name, no prompt text, no quoted result, no boilerplate line the script suppressed. This is
   the last chance before the file is public.
3. `changed_files` — `IMPROVEMENTS.md` must be the **only** path this run touched. No build, no test
   run: nothing else changed, and `IMPROVEMENTS.md` is not shipped in the package.
4. `Bash: git add IMPROVEMENTS.md && git commit -m "Log I<n>–I<m> from the <N>-week session scan"`
   (body: the corpus line from M8.5). **No `Co-Authored-By`.**
5. `Bash: git show --stat HEAD` then `git push origin main`. No review, by standing instruction.

---

## M10 — Report

| Section | Content |
|---|---|
| Corpus | window in weeks, transcripts scanned, projects covered (slugs only), total tool calls, built-in vs MCP share, total tool-result tokens, tool-input chars, measured wall time, seconds slept, output/cache token totals |
| Top waste | the eight cost centres from M2–M4, each with its number |
| Trim ledger | the full M2 ledger — chars and tokens per class, and the share of all output it represents |
| Surface cost | total `[Description]` bytes and `SKILL.md` bytes, and what they cost per request |
| Latency & memory | slowest tools by `p95` and by total minutes, RSS per workspace and per document, any cold-path trigger named |
| Research | the methods M6 found worth adopting, each with a source and the refutation attempt it survived; and the ones dropped as already-true or not actionable here |
| New rows | every id written, with its one-line finding and expected saving, highest cost first |
| Strengthened | existing rows given a new measurement instead of a duplicate |
| Dropped | candidates measured but not logged, with their combined cost and the reason — never silent |
| Privacy | confirmation that no row names a path, type, prompt or result from another project |
| Commit | SHA and the single path staged |
| Trend | this run's corpus line against the previous run's, when one exists |

If M1 found nothing, the report is just: window, roots, most recent transcript outside the window, and
the statement that nothing was changed.
