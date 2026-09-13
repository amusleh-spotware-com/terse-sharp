---
description: Mine every Claude Code session across all projects for the two numbers that decide a task's cost - how many tool calls it took and how many wall-clock minutes the user waited - with ONE single-pass script that measures payload, speed and emitted code together; score the corpus for unspent parallelism, unused batch parameters and redundant calls against a published target; mine the user's own turns for every intervention and trap that cost an extra prompt; deep-research the state of the art in agent token efficiency, parallel tool calling and speculative execution IN PARALLEL with the scan; log every measured finding as an open row in IMPROVEMENTS.md, then commit and push.
argument-hint: "[weeks to scan, default 1]"
---

# 🚫 HARD GATE — findings are **measured to the character**, never impressionistic, and never leak another project's content.

`$ARGUMENTS` — a number of **weeks** to scan. Absent or unparseable → **1 week**. Nothing else takes
input from the user; do not ask, do not confirm the window.

**The five goals this command exists to serve.** Every phase feeds at least one; every row logged in
M9 is tagged with the one it serves; and M11 proves all five were mined or states what was checked and
why that goal came back clean. They are not ranked by taste — they are ranked by what a failure costs:

| Tag | Goal | What a finding for it looks like |
|---|---|---|
| `[accuracy]` | **the agent stops falling into traps that force the user to intervene** | an extra user prompt, a rejected edit, a retry with different arguments, a confident wrong answer acted on — measured in section C |
| `[modern]` | **the agent emits modern .NET — latest C# syntax, current APIs** | a legacy construct in text the agent *wrote*, counted per 1000 emitted lines and confirmed against the workspace — section C, confirmed in M5 |
| `[perf]` | **the agent emits code that is not slow** | sync-over-async, sync file I/O, an allocation on a per-file/per-line/per-symbol path in emitted text — section C, confirmed in M5 |
| `[speed]` | **the agent finishes the task sooner, in fewer calls** | wall time per task, tool calls per task, a serial round trip that had no dependency, a batch parameter that existed and was not used, a blind wait, a re-run of work whose inputs never moved — section B |
| `[cost]` | **the agent pays fewer tokens for the same answer** | payload, framing, round trips, surface, cache churn — sections A and B, priced in M3 |

**`[accuracy]` and `[speed]` are co-primary, and both outrank `[cost]`.** An intervention costs a whole
turn at the corpus turn `p50`, plus the tokens of the re-issued work, plus the context already spent on
the wrong path — so a payload row of equal token size is worth less than an intervention row. And a
round trip costs the corpus **model gap** (`tool_result` → next `tool_use`, measured **p50 6 097 ms**
on 35 967 gaps in the 1-week window of 2026-08-26) **plus** the tool's own `p50` — so a row that
deletes one call per task is worth more than a row that shortens one response, because the wall clock
the user waits through is dominated by *how many times the loop turns*, not by how wide each answer
is. Rank accordingly in M9.

**The four `[speed]` metrics, and they are the run's headline numbers.** Every run reports all four in
M11 against the previous run, and a run that reports none of them has not measured `[speed]` and says
so as a degraded run:

| Metric | Where it comes from | Baseline, 1 week to 2026-08-26 |
|---|---|---|
| **tool calls per task cycle** | section B's cycle table — user turn to the next user turn | mean **25.6**, p90 **84** |
| **wall time per task cycle** | the same table | p50 **1.3 min**, p90 **51.9 min**, mean **44.8 min** |
| **round-trip latency** | model gap p50 + the called tool's p50 | **6 097 ms** of model gap, before the tool runs at all |
| **parallelism score** | section B — calls per assistant message, multi-call share, unspent-parallelism count, unused-batch count, redundant-call share | **1.165 calls/msg**, **14.3%** multi, redundant **8.3%** (3 008 of 36 075) |

**Nine gates that outrank everything else in this command:**

1. **A finding without a number is not a finding.** "Reads feel wasteful" is banned. "36 `Read` calls
   in one session, 214 KB of tool results, ~53 500 tokens, against 10 `search_text` calls" is a
   finding. Every row carries a count, a byte/token/millisecond figure and the corpus it came from.
2. **Nothing is too small to count — but a trim is only real once a tokenizer says so.** This
   command's reason to exist is the long tail: a two-space column separator, a repeated
   `(truncated=false)`, an absolute path, one redundant word in a tool `[Description]` re-sent on
   every request of every session. A multi-space run costs exactly **1 token** wherever it appears,
   so a 4-column table pays **3 tokens per row** for alignment nobody reads — over 40 000 rows that
   is 120 000 tokens, and it outranks most new tools. "Too small to log" is a banned phrase here;
   small findings are **aggregated into one row per family**, never dropped (M9.4).
   **The mirror rule is equally binding: a trim that measures zero is not a finding either.** Blank
   lines, trailing whitespace and abbreviation each measured **≤0.1% and 19-of-20 exactly zero** on
   real payloads (M3.2). Proposing one of those is the same defect as failing to notice the padding —
   both spend the reader's attention on a number that is not there.
3. **Never copy content out of another project's transcript.** These sessions are from a private
   employer codebase (`cTraderDev`, `ctd-worktrees-*`, `cTraderAutomateApi`, …) and `IMPROVEMENTS.md`
   is pushed to a **public** repository. A row may name: tool names, call counts, byte sizes, token
   estimates, millisecond timings, error codes, and a project's **directory slug**. A row may **never**
   contain: source code, file paths inside those repos, type or member names, ticket text, user
   prompts, or a quoted tool result. When in doubt, state the shape (`a 40 KB whole-file read of a
   test file`) not the value. The measurement script enforces a mechanical version of this rule and
   you enforce the rest by hand in M10.2.
4. **Character economy is a claim about tokens, so state both.** Chars are what the script can count;
   tokens are what is paid. The measured ratio over real TerseSharp responses is **4.18 chars/token**,
   which is why section A estimates at `chars // 4` — and why an estimate is all it is. Prefer trims
   that delete **whole tokens** (a separator, a padded column, a repeated line prefix, a whole field)
   over trims that shorten one: deleting characters from inside a word usually saves nothing at all,
   because a common word is already a single token.
5. **Every claim names the instrument that verified it.** Four are admissible, in this order:
   (a) **the corpus** — a count from the single-pass scan over the real transcripts; (b) **a tokenizer
   experiment** — an encode-before/encode-after on a real payload, in both `o200k_base` and
   `cl100k_base`, labelled directional because neither is Claude's; (c) **a primary source** — a
   vendor document or a paper, fetched and quoted, never a summary of a summary; (d) **the
   workspace** — `analyze`, `search_regex`, `get_symbol_source`, `build` or `run_tests` over
   TerseSharp's own source, which is the only instrument that can confirm a pattern the transcripts
   show being *written* rather than being *read*. Where an outside claim contradicts the corpus,
   **the corpus wins and the row says so**. A claim that survived none of the four is written
   `UNVERIFIED` in the report and is not allowed into `IMPROVEMENTS.md` at all. This gate exists
   because a previous run of this command shipped a trim ledger whose three headline classes were each
   worth ~0.1%, sourced from plausible reasoning that nobody encoded.
6. **The agent's own output is corpus too, and a run that skipped it is incomplete.** Section A sees
   what the agent *read*; section C sees what it *wrote* (`tool_use.input` on the edit family) and the
   turns the user spent steering it (`user` records carrying text rather than a `tool_result`). All
   three sections come out of **one** pass now, so there is no longer a phase that can be quietly
   dropped when the run is long — a section that prints zeros is a defect in the script, not an
   absence of data, and M11 says which it was.
7. **A pattern counted in emitted text is HEURISTIC until instrument (d) confirms it.** The
   emitted-code section is a regex over the characters the agent sent, with no compilation behind it.
   It ranks candidates; it never proves one. A `[modern]` or `[perf]` row states which `analyze` /
   `search_regex` / `get_symbol_source` call confirmed the class on real source, or it is not written.
   And a class this repository **deliberately** leaves off — an `.editorconfig` severity, the app-wide
   conventions clause in `CLAUDE.md` — is not a finding; check before logging, because proposing that
   the codebase fight its own configuration is the same defect as a number nobody encoded.
8. **A call deleted beats a response shortened, and the run must say which it did.** The prime
   directive is "save tokens, **increase speed**" — conjunction, not disjunction — and M4.1's baseline
   says the second half is where the hours are. A format trim that saves 2 000 tokens and changes no
   call count saves **zero seconds**. So every `[speed]` row states its saving in **hours or
   milliseconds per window**, and names which of the five levers it pulls:

   | Lever | What it means | Why it ranks there |
   |---|---|---|
   | **(a) delete the call** | fuse the pair, batch the run, or answer it in the first response | deletes a tool call **and** a 6 097 ms model gap |
   | **(b) parallelise the call** | the agent issues it in the same assistant message as an independent sibling | deletes the gap, costs nothing, needs no server change — and is the **least spent** lever in the corpus |
   | **(d) never make the call** | a rule, a default or a guard stops a run whose answer provably could not have changed | deletes both, and is the largest measured class |
   | **(e) teach the harness** | a hook, a guard `updatedInput`, a `SKILL.md` swap row or a `remedy:` that makes (a), (b) or (d) happen without the agent choosing to | the only lever that applies to **every future session**, including sessions of other agents |
   | **(c) shorten the call** | the tool itself gets faster or answers smaller | worth a fraction of the other four |

   (a), (b), (d) and (e) are worth a multiple of (c), because each also deletes a **6 097 ms** model
   gap that no server change can touch.
9. **Parallelism is a score with a target, not a vibe — and raising it is a first-class row.** The
   corpus measured **1.165 tool calls per assistant message, with only 14.3% carrying two or more**
   (grouped by API `message.id` — never per transcript record, which answers 1.0 by construction).
   The published bar is **MCP-Bench's**: top marks require **<10% redundant calls** and **90–100% of
   parallelisable work actually executed in parallel**; the corpus is at **8.3% redundant** and far
   under the parallel bar. Section B measures three separate deficits — **unspent parallelism**,
   **unused batch parameters**, **redundant calls** — each defined in M2's correction 6 and each
   convertible into a row on its own. A run that reports a parallelism score without naming which of
   the three dominates has not done this gate.

**Also banned:** `AskUserQuestion`, `ExitPlanMode`, editing any file other than `IMPROVEMENTS.md` and
`IMPROVEMENTS-ARCHIVE.md`, `git add -A`, a `Co-Authored-By:` trailer, and writing any script anywhere
inside the repository.

**No review.** `code-review-gate`, `/code-review`, `caveman:cavecrew-reviewer` and every other review
path are explicitly waived for this command by standing user instruction. Do not spawn one and do not
report the phase as degraded.

**Subagents:** banned for every phase that touches a transcript (M2, M3, M5, M6 — they read prompt
text and emitted code, which is the most sensitive material in the corpus) — a reviewer adds nothing
to a log scan and doubles the private-content exposure. **Permitted, and expected, in M7 only** (the
research fan-out), which reads the public web and is handed **no transcript content whatsoever** —
not a path, not a slug, not a quoted result. A research subagent prompt that contains anything mined
from a session is a breach of gate 3.

---

## M0 — Preflight, and the parallel launch this command is scored on

**This command is itself measured by the metrics it collects.** M11 reports this run's own calls per
assistant message, and a run that issued its phases one call per message has failed gate 9 in the one
session it fully controls. The phase graph below exists so that never happens:

| Phase | Depends on | Therefore |
|---|---|---|
| M2 (the single-pass scan) | nothing but the window | **launch first, in the background** |
| M7 (research fan-out) | nothing — it is handed no corpus content at all | **launch in the SAME message as M2** |
| M8 (dedup read of both backlog files) | nothing | **same message again** — one `read_text paths=[…]` |
| M3, M4, M5, M6 | M2's output | after the scan lands |
| M9, M10, M11 | everything | last |

So **M0 ends with one assistant message carrying, concurrently:** the backgrounded `python` scan, the
research subagent spawns, and the `read_text paths=["IMPROVEMENTS.md", "IMPROVEMENTS-ARCHIVE.md"]`
of M8. Background work re-invokes you when it finishes — **do not poll it, do not `sleep`, do not
`TaskOutput` it while it runs**; `TaskOutput` polling cost a measured **14.08 h in one week, 13.0% of
all tool wall time**, and every one of those seconds bought nothing the completion notification did
not already deliver. If everything you can do is waiting on the scan, end the turn.

1. Invoke the `terse-sharp` skill. `workspace_status` / `load_workspace TerseSharp.slnx`; pass
   `workspace: "TerseSharp"` on terse-sharp calls.
2. `Bash: git fetch origin && git pull --ff-only` on `main` (history/index mutation — the one legal
   `Bash` class here). `changed_files` — record pre-existing dirt; only `IMPROVEMENTS.md` and
   `IMPROVEMENTS-ARCHIVE.md` are ever staged by this run.
3. Resolve the window: `WEEKS` = `$ARGUMENTS` if it parses as a positive number, else `1`.
4. Locate the corpus. Session transcripts live **outside every workspace**, so `Bash` and built-ins
   are legal here and terse-sharp is not:
   - `~/.claude/projects/<project-slug>/<session-uuid>.jsonl` — one file per session;
   - `~/.claude/projects/<project-slug>/<session-uuid>/subagents/agent-*.jsonl` — one per subagent,
     and these are where delegation cost hides;
   - if `CLAUDE_CONFIG_DIR` is set and its `projects/` exists and differs, scan that too.
5. `TaskCreate` one task per phase: M1, M2, M3, M4, M5, M6, M7, M8, M9, M10, M11. **Eleven, not
   fifteen** — the payload, speed and emitted-code passes are one script now, so they are one task.
6. **Refresh the batch map before running the scan.** `BATCH` in the script names the tools that
   already accept a list. It is checked against the live surface once per run — `ToolSearch
   select:mcp__terse-sharp__read_text` or the advertised schema — because a tool that *gained* a batch
   parameter since the last run and is still being called one-at-a-time is the single highest-value
   row this command produces, and a stale map hides it.

---

## M1 — No activity is a legitimate, reported outcome

```bash
find ~/.claude/projects -name '*.jsonl' -mtime -$((WEEKS*7)) | wc -l
```

**Zero files in the window → say so and stop.** Report the window, the roots searched, and the date of
the most recent transcript found outside it. Make **no** edit to `IMPROVEMENTS.md`, no commit, no push.
That is the whole run; it is a success, not a failure.

---

## M2 — Measure: ONE deterministic script, ONE pass, three sections

Write the script to a **temporary directory** (`$TMPDIR` / `%TEMP%`), never into the repository, and
run it with `python` **in the background**. One script, one walk of the corpus, comparable across runs
— an eyeballed sample is not this phase.

**Why one script and not three.** The payload, speed and emitted-code passes used to be three programs
that each opened, decoded and JSON-parsed **every transcript in the window** — three full walks for one
answer, and three copies of `walk()` / `stamp()` / `target()` in the prompt. Merging them deletes two
corpus walks. The print labels of each section are **unchanged**, so the trend comparison against
previous runs survives the merge — the one property the merge was not allowed to break.

**Two shell traps this command has already paid for.** On Windows, bash's `/tmp` and the Windows
`%TEMP%` are the same directory but not the same *name*: write the file from bash with a POSIX path
and hand `python` the **Windows** path, or it answers `can't open file`. And a heredoc longer than
about 120 lines, or one containing a `'"` sequence, gets truncated mid-string and dies with
`unexpected EOF while looking for matching '`. Append the script in chunks and `ast.parse` it before
running it.

**A third trap, found by reading this file's own history: escape depth.** The previous emitted-code
script was written with **doubled** backslashes (`re.compile(r'\\b(?:namespace|…)')`,
`body.count('\\n')`, `print('\\n== …')`). Inside an `r'…'` literal that matches a *literal backslash
followed by b* — so every legacy and every slow-construct pattern matched nothing, `emitted_lines`
counted no newlines, and the phase reported zeros that were read as "the agent writes modern code".
**Single-escape every regex, and prove it before trusting a zero:** after `ast.parse`, run the script
over one known-dirty transcript, or assert `emitted_lines > edits_scanned`. A section that prints
zeros is a defect until a positive control says otherwise.

**The schema below was verified against 282 061 records on 2026-08-08**; every field named here was
counted, not assumed.

```python
import collections, datetime, json, os, re, sys

WEEKS = float(sys.argv[1]) if len(sys.argv) > 1 else 1.0
CUTOFF = datetime.datetime.now().timestamp() - WEEKS * 7 * 86400

BUILTIN = {'Read', 'Write', 'Edit', 'NotebookEdit', 'Grep', 'Glob', 'Bash',
           'WebFetch', 'WebSearch', 'Agent', 'Task', 'TodoWrite', 'Skill', 'ToolSearch'}
MUTATE = {'replace_symbol', 'replace_symbol_body', 'add_member', 'delete_symbol', 'edit_text',
          'write_text', 'rename_symbol', 'move_type_to_file', 'change_signature', 'Edit', 'Write'}
VERIFY = {'run_tests', 'rerun_failed', 'build', 'analyze', 'cleanup', 'format', 'get_diagnostics',
          'gate', 'list_tests', 'clean'}
EDIT = MUTATE | {'NotebookEdit', 'extract_interface', 'move_type_to_namespace'}
BATCH = {'read_text': 'paths', 'write_text': 'files', 'edit_text': 'edits',
         'replace_symbol': 'symbolIds'}
NARROW = ('project', 'filter', 'projects', 'test', 'path')
CODE_KEYS = ('newText', 'new_string', 'content', 'body', 'source', 'code', 'members', 'declaration')
PATH_KEYS = ('path', 'file_path', 'filePath', 'file', 'symbolId', 'symbol')
TARGET_KEYS = ('path', 'file', 'filePath', 'symbolId', 'symbol', 'query', 'pattern', 'name', 'command')

SHELL_TEXT = re.compile(r'\b(grep|rg|cat|head|tail|sed|awk|ls|find|type)\b')
SHELL_GIT = re.compile(r'\bgit\s+(status|diff)\b')
SHELL_DOTNET = re.compile(r'\bdotnet\s+(build|test|clean|format)\b')
SLEEP = re.compile(r'\bsleep\s+(\d+)')
WAITLOOP = re.compile(r'\b(while|until|for)\b')
ABSPATH = re.compile(r'[A-Za-z]:\\[\w.\-\\]+|/(?:Users|home|mnt|var|opt)/[\w.\-/]+')
PADRUN = re.compile(r'(?<=\S) {2,}')
INDENT = re.compile(r'^ {2,}')
ERRCODE = re.compile(r'\bERROR\s+([A-Za-z][A-Za-z0-9_]*)')
CONSTANT = ('(truncated=false', 'errors=0 warnings=0', 'EXACT', 'remedy:')
CSHARP = re.compile(r'\b(?:namespace|class|record|struct|interface|readonly|static|public|private|'
                    r'internal|sealed|var|await)\b')
AWAITED = re.compile(r'\bawait\s')
CONFIGURED = re.compile(r'ConfigureAwait\(')
RULE = re.compile(r'\b((?:CA|IDE|CS|SYSLIB|RS)\d{4})\b')
NOOP = re.compile(r'\b0 files changed\b|changedLines=0')

LEGACY = (
    ('collection expression [] (IDE0300/IDE0301)',
     re.compile(r'new\s+(?:List|Dictionary|HashSet|Queue|Stack|Collection)\s*<[^>\n]{0,80}>\s*\(\s*\)'
                r'|\b(?:Array|Enumerable)\.Empty\s*<')),
    ('is null / is not null (IDE0041)', re.compile(r'[!=]=\s*null\b')),
    ('file-scoped namespace (IDE0161)', re.compile(r'(?m)^namespace\s+[\w.]+\s*\r?\n\{')),
    ('field keyword / primary ctor (IDE0032)',
     re.compile(r'(?m)^\s*(?:private|protected)\s+(?:readonly\s+)?[\w<>,.\[\]?]+\s+_\w+\s*;')),
    ('target-typed new (IDE0090)',
     re.compile(r'\b([A-Z][\w<>,.\[\]?]*)\s+\w+\s*=\s*new\s+\1\s*[(<]')),
    ('switch expression over else-if (IDE0066)', re.compile(r'(?m)^\s*else\s+if\b')),
    ('expression body (IDE0022, CI-breaking here)',
     re.compile(r'(?m)^\s*(?:public|private|internal|protected)[^\n{;]*\)\s*\r?\n\s*\{\s*\r?\n\s*return\b')),
    ('explicit IFormatProvider (CA1305)', re.compile(r'\.ToString\(\s*\)|\bstring\.Format\s*\(')),
    ('interpolation as a value converter', re.compile(r'\$"\{\s*[\w.]+\s*\}"')),
)

SLOW = (
    ('sync over async (.Result/.Wait)',
     re.compile(r'\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)')),
    ('sync file I/O on the request path',
     re.compile(r'\bFile\.(?:ReadAll|WriteAll|AppendAll|ReadLines|Open)\w*\s*\('
                r'|\bXDocument\.Load\s*\(|new\s+StreamReader\s*\(')),
    ('materializing LINQ (.ToList/.ToArray)', re.compile(r'\.To(?:List|Array)\(\)')),
    ('Substring/Split where a span slices', re.compile(r'\.Substring\s*\(|\.Split\s*\(')),
    ('ToLower/ToUpper to compare (CA1862)', re.compile(r'\.To(?:Lower|Upper)(?:Invariant)?\(\)')),
    ('string += in a loop', re.compile(r'(?m)^\s*\w+\s*\+=\s*[$"]')),
    ('interpreted Regex (SYSLIB1045)',
     re.compile(r'new\s+Regex\s*\(|\bRegex\.(?:Match|Matches|Replace|IsMatch)\s*\(')),
    ('LINQ chain per element',
     re.compile(r'\.Where\([^\n)]{0,80}\)\s*\.\s*(?:Select|First|FirstOrDefault|Any|Count)\s*\(')),
)

TRAPS = (('compile-gate rollback', 'CompileRegression'),
         ('hand-written documentation id', 'SymbolNotFound'),
         ('anchor matched nothing', 'matched 0 times'),
         ('anchor not unique', 'is not unique'),
         ('more than one member', 'not exactly one member'),
         ('workspace ambiguous', 'AmbiguousWorkspace'),
         ('symbol ambiguous', 'AmbiguousSymbol'),
         ('server not built', 'build TerseSharp.Server first'),
         ('format gate red', 'VERIFY_FAILED'),
         ('edit landed nothing', '0 files changed'))

CUES = (('correction',
         re.compile(r'\b(?:no,|nope|wrong|incorrect|not what i|revert|undo|you broke|regress)', re.I)),
        ('gate reminder',
         re.compile(r'\b(?:hard gate|use terse|terse-sharp|you (?:used|ran) (?:grep|read|bash|glob)'
                    r'|built-?in|forbidden)', re.I)),
        ('redo', re.compile(r'\b(?:try again|re-?run|retry|still (?:red|failing|broken))', re.I)),
        ('re-scope', re.compile(r"\b(?:don'?t|do not|stop|only|instead)\b", re.I)),
        ('unblock', re.compile(r'\b(?:continue|proceed|keep going|go ahead)\b', re.I)))


def short(name):
    return name.split('__')[-1] if name.startswith('mcp__') else name


def pick(arguments, keys):
    for key in keys:
        value = arguments.get(key)
        if isinstance(value, str) and value:
            return value[:200]
    return ''


def stamp(record):
    raw = record.get('timestamp')
    if not isinstance(raw, str):
        return None
    try:
        return datetime.datetime.fromisoformat(raw.replace('Z', '+00:00')).timestamp()
    except ValueError:
        return None


def q(values, fraction):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]


def strings(node, out, cap=40):
    if len(out) >= cap:
        return
    if isinstance(node, dict):
        for value in node.values():
            strings(value, out, cap)
    elif isinstance(node, list):
        for value in node:
            strings(value, out, cap)
    elif isinstance(node, str) and len(node) >= 4:
        out.append(node[:200])


def harvest(node, out):
    if isinstance(node, dict):
        for key, value in node.items():
            if key in CODE_KEYS and isinstance(value, str):
                out.append(value)
            else:
                harvest(value, out)
    elif isinstance(node, list):
        for item in node:
            harvest(item, out)


roots, seen = [], set()
for candidate in (os.path.expanduser('~/.claude/projects'),
                  os.path.join(os.environ.get('CLAUDE_CONFIG_DIR', ''), 'projects')):
    if candidate and os.path.isdir(candidate):
        real = os.path.realpath(candidate).lower()
        if real not in seen:
            seen.add(real)
            roots.append(candidate)


def walk():
    files = set()
    for root in roots:
        for folder, _, names in os.walk(root):
            if os.path.basename(folder) == 'tool-results':
                yield folder, None, names
                continue
            for name in names:
                if not name.endswith('.jsonl'):
                    continue
                path = os.path.join(folder, name)
                key = os.path.realpath(path).lower()
                if key in files:
                    continue
                try:
                    if os.path.getmtime(path) >= CUTOFF:
                        files.add(key)
                        yield path, os.path.relpath(path, root).split(os.sep)[0], None
                except OSError:
                    pass


types = collections.Counter()
calls = collections.Counter(); errors = collections.Counter(); error_codes = collections.Counter()
result_chars = collections.Counter(); input_chars = collections.Counter()
steers = collections.Counter(); structured_trunc = collections.Counter()
durations = collections.defaultdict(list); dupes = collections.Counter()
per_project = collections.defaultdict(collections.Counter)
bash_kind = collections.Counter()
attach = collections.Counter(); attach_n = collections.Counter()
attribution = collections.Counter(); attributed_tokens = collections.Counter()
denials = collections.Counter(); queue = collections.Counter(); stops = collections.Counter()
by_model = collections.defaultdict(collections.Counter)
trim = collections.Counter(); placebo = collections.Counter()
line_freq = collections.Counter(); line_projects = collections.defaultdict(set)
chains = collections.Counter(); same_target = collections.Counter(); refetch = collections.Counter()
runs = collections.Counter(); longest = collections.Counter(); parallel = collections.Counter()
unspent = collections.Counter(); unspent_gap = []; batch_missed = collections.Counter()
tools_per_session = []
verify_ms = collections.Counter(); verify_n = collections.Counter(); scoped = collections.Counter()
repeat_gap = []; agent_ms = []; gaps = []; first_call = []
cycles_s = []; cycles_c = []; tail_s = []; tail_c = []; body_s = []; body_c = []
legacy = collections.Counter(); slow = collections.Counter()
legacy_tools = collections.defaultdict(collections.Counter)
legacy_sessions = collections.defaultdict(set); slow_sessions = collections.defaultdict(set)
edit_calls = collections.Counter(); edit_rejects = collections.Counter()
wasted_input = collections.Counter(); traps = collections.Counter(); trap_input = collections.Counter()
rules = collections.Counter(); gate_red = collections.Counter(); after_edit = collections.Counter()
cues = collections.Counter(); before = collections.Counter(); rework = collections.Counter()
per_session = []; payloads = []; turns = []; compactions = []; api_errors = collections.Counter()
sessions = set(); slept = interrupted = think_chars = think_n = think_sealed = 0
sleep_bare = sleep_bare_s = 0; sleep_ms = 0.0; serial_ms = 0.0
persisted_n = persisted_bytes = spill_files = spill_bytes = 0
delegations = delegated_tokens = 0; delegated_ms = 0
mcp_structured = mcp_structured_chars = 0
records = with_message = cycles = 0
emitted_lines = emitted_chars = edits_scanned = 0
prompts = interventions = 0
cache_read_total = cache_write_total = cache_churn = 0


def prune():
    if len(line_freq) > 300_000:
        for text, count in list(line_freq.items()):
            if count < 2:
                del line_freq[text]
                line_projects.pop(text, None)


def audit(text, project):
    local = collections.Counter()
    blank = trail = 0
    for raw in text.split('\n'):
        body = raw.rstrip()
        trail += len(raw) - len(body)
        if not body:
            blank += len(raw) + 1
            continue
        if INDENT.match(body):
            trim['indent (1 token/line)'] += 1
        trim['column padding (1 token/run)'] += len(PADRUN.findall(body))
        local[body] += 1
        if len(body) >= 12:
            line_freq[body] += 1
            group = line_projects[body]
            if len(group) < 6:
                group.add(project)
    placebo['blank lines'] += blank
    placebo['trailing whitespace'] += trail
    trim['repeated lines'] += sum((c - 1) * (len(s) + 1) // 4 for s, c in local.items() if c > 1)
    trim['absolute path text'] += sum(len(m) for m in ABSPATH.findall(text)) * 61 // 400
    for token in CONSTANT:
        trim['constant tags'] += text.count(token) * len(token) // 4
    prune()


for path, project, spilled in walk():
    if spilled is not None:
        for name in spilled:
            try:
                if os.path.getmtime(os.path.join(path, name)) >= CUTOFF:
                    spill_files += 1
                    spill_bytes += os.path.getsize(os.path.join(path, name))
            except OSError:
                pass
        continue
    sessions.add(path)
    pending, seen_calls, order, events, marks = {}, collections.Counter(), [], [], []
    by_message = collections.Counter(); ms_by_message = collections.defaultdict(float)
    msg_order = []; msg_calls = {}; arg_order = []; result_text = {}; session_tools = set()
    edited = collections.Counter(); last_tool = None
    user_turns = assistant_turns = session_edits = session_gates = session_steers = 0
    last_result_at = None
    seq = 0
    for line in open(path, encoding='utf-8', errors='replace'):
        try:
            record = json.loads(line)
        except ValueError:
            continue
        records += 1
        seq += 1
        kind = record.get('type', 'none')
        types[kind] += 1
        at = stamp(record)
        if record.get('interruptedMessageId'):
            interrupted += 1
        if record.get('toolDenialKind'):
            denials[record['toolDenialKind']] += 1
        for key in ('attributionSkill', 'attributionMcpServer', 'attributionAgent', 'attributionPlugin'):
            if record.get(key):
                attribution[(key, record[key])] += 1
        if kind == 'system':
            subtype = record.get('subtype', 'none')
            if subtype == 'turn_duration' and isinstance(record.get('durationMs'), (int, float)):
                turns.append(record['durationMs'])
            elif subtype == 'compact_boundary':
                meta = record.get('compactMetadata') or {}
                compactions.append((meta.get('preTokens') or 0, meta.get('postTokens') or 0,
                                    meta.get('trigger'), meta.get('durationMs') or 0))
            elif subtype == 'api_error':
                api_errors[str((record.get('error') or {}).get('status'))] += 1
        elif kind == 'queue-operation':
            queue[record.get('operation', 'none')] += 1
        elif kind == 'attachment':
            body = record.get('attachment') or {}
            label = body.get('type', 'none')
            attach_n[label] += 1
            attach[label] += len(json.dumps(body))
        meta = record.get('mcpMeta')
        if isinstance(meta, dict) and meta.get('structuredContent') is not None:
            mcp_structured += 1
            mcp_structured_chars += len(json.dumps(meta['structuredContent']))
        outcome = record.get('toolUseResult')
        if isinstance(outcome, dict):
            if outcome.get('persistedOutputPath'):
                persisted_n += 1
                persisted_bytes += outcome.get('persistedOutputSize') or 0
            if outcome.get('truncated') or (outcome.get('file') or {}).get('truncatedByTokenCap'):
                structured_trunc['harness'] += 1
            if outcome.get('toolStats'):
                delegations += 1
                delegated_tokens += outcome.get('totalTokens') or 0
                delegated_ms += outcome.get('totalDurationMs') or 0
        message = record.get('message')
        if not isinstance(message, dict):
            continue
        with_message += 1
        model = message.get('model') or 'unknown'
        if message.get('stop_reason'):
            stops[message['stop_reason']] += 1
        stats = message.get('usage')
        if isinstance(stats, dict):
            for key in ('input_tokens', 'output_tokens', 'cache_read_input_tokens'):
                by_model[model][key] += stats.get(key) or 0
            cache_read_total += stats.get('cache_read_input_tokens') or 0
            written = 0
            split = stats.get('cache_creation')
            if isinstance(split, dict):
                for key, value in split.items():
                    if isinstance(value, (int, float)):
                        by_model[model][key] += value
                        written += value
            else:
                written = stats.get('cache_creation_input_tokens') or 0
                by_model[model]['cache_creation_input_tokens'] += written
            cache_write_total += written
            if written and seq > 6:
                cache_churn += written
            for key in ('attributionSkill', 'attributionMcpServer', 'attributionAgent'):
                if record.get(key):
                    attributed_tokens[(key, record[key])] += (stats.get('output_tokens') or 0)
        blocks = message.get('content')
        if isinstance(blocks, str):
            blocks = [{'type': 'text', 'text': blocks}]
        if not isinstance(blocks, list):
            continue
        if record.get('type') == 'assistant':
            assistant_turns += 1
        spoken = ''.join(b.get('text', '') for b in blocks
                         if isinstance(b, dict) and b.get('type') == 'text')
        if record.get('type') == 'user' and at:
            is_result = any(isinstance(b, dict) and b.get('type') == 'tool_result' for b in blocks)
            if spoken.strip() and not is_result:
                marks.append(at)
                last_result_at = at
                if not record.get('isMeta') and assistant_turns:
                    user_turns += 1
                    prompts += 1
                    steered = False
                    for label, pattern in CUES:
                        if pattern.search(spoken):
                            cues[label] += 1
                            steered = True
                    if steered:
                        interventions += 1
                        session_steers += 1
                        before[last_tool or 'none'] += 1
        mid = message.get('id')
        fanout = sum(1 for b in blocks if isinstance(b, dict) and b.get('type') == 'tool_use')
        if fanout:
            by_message[mid] += fanout
            if mid not in msg_calls:
                msg_calls[mid] = [at, []]
                msg_order.append(mid)
        for block in blocks:
            if not isinstance(block, dict):
                continue
            shape = block.get('type')
            if shape == 'thinking':
                think_n += 1
                body = block.get('thinking') or ''
                think_chars += len(body)
                if not body:
                    think_sealed += 1
            elif shape == 'tool_use':
                tool = short(block.get('name') or 'none')
                arguments = block.get('input') or {}
                last_tool = tool
                calls[tool] += 1
                session_tools.add(tool)
                per_project[project][tool] += 1
                pending[block.get('id')] = (tool, at, mid, arguments)
                encoded = json.dumps(arguments, sort_keys=True)
                input_chars[tool] += len(encoded)
                seen_calls[(tool, encoded[:4000])] += 1
                order.append((tool, pick(arguments, TARGET_KEYS)))
                arg_order.append((tool, arguments))
                if mid in msg_calls:
                    msg_calls[mid][1].append((tool, arguments, block.get('id')))
                if last_result_at and at and at >= last_result_at:
                    gaps.append((at - last_result_at) * 1000)
                if tool == 'Bash':
                    command = arguments.get('command', '') or ''
                    found = SLEEP.findall(command)
                    if found:
                        total = sum(int(v) for v in found)
                        slept += total
                        if not WAITLOOP.search(command):
                            sleep_bare += 1
                            sleep_bare_s += total
                        bash_kind['sleep/wait'] += 1
                    elif SHELL_GIT.search(command):
                        bash_kind['git status/diff'] += 1
                    elif SHELL_DOTNET.search(command):
                        bash_kind['dotnet build/test/format'] += 1
                    elif SHELL_TEXT.search(command):
                        bash_kind['shell text tool'] += 1
                    else:
                        bash_kind['other'] += 1
                if tool in EDIT:
                    session_edits += 1
                    spot = pick(arguments, PATH_KEYS)
                    if spot:
                        edited[spot] += 1
                    chunks = []
                    harvest(arguments, chunks)
                    emitted = '\n'.join(chunks)
                    if emitted and (CSHARP.search(emitted) or spot.endswith('.cs')):
                        edits_scanned += 1
                        emitted_lines += emitted.count('\n') + 1
                        emitted_chars += len(emitted)
                        for label, pattern in LEGACY:
                            hits = len(pattern.findall(emitted))
                            if hits:
                                legacy[label] += hits
                                legacy_tools[label][tool] += hits
                                legacy_sessions[label].add(path)
                        for label, pattern in SLOW:
                            hits = len(pattern.findall(emitted))
                            if hits:
                                slow[label] += hits
                                slow_sessions[label].add(path)
                        gap = len(AWAITED.findall(emitted)) - len(CONFIGURED.findall(emitted))
                        if gap > 0:
                            slow['await without ConfigureAwait(false)'] += gap
                            slow_sessions['await without ConfigureAwait(false)'].add(path)
                if tool in VERIFY:
                    session_gates += 1
            elif shape == 'tool_result':
                tool, started, mid_of, arguments = pending.pop(block.get('tool_use_id'),
                                                               (None, None, None, {}))
                content = block.get('content')
                text = content if isinstance(content, str) else json.dumps(content or '')
                if at:
                    last_result_at = at
                if not tool:
                    continue
                result_chars[tool] += len(text)
                payloads.append((len(text), tool, project))
                audit(text, project)
                result_text[block.get('tool_use_id')] = text[:20000]
                bad = bool(block.get('is_error')) or text.lstrip().startswith('ERROR ')
                if bad:
                    errors[tool] += 1
                    found = ERRCODE.search(text)
                    if found:
                        error_codes[found.group(1)] += 1
                if 'narrow with' in text or '(truncated=true' in text:
                    steers[tool] += 1
                    structured_trunc['tool steer'] += 1
                if tool in EDIT:
                    edit_calls[tool] += 1
                    if bad or NOOP.search(text):
                        edit_rejects[tool] += 1
                        wasted_input[tool] += len(json.dumps(arguments, sort_keys=True))
                        for label, needle in TRAPS:
                            if needle in text:
                                traps[label] += 1
                                trap_input[label] += len(json.dumps(arguments, sort_keys=True))
                if tool in VERIFY:
                    for rule in RULE.findall(text):
                        rules[rule] += 1
                    if 'FAILED' in text or 'error' in text[:400].lower():
                        gate_red[tool] += 1
                        if session_edits:
                            after_edit[tool] += 1
                if not started or not at or at < started:
                    continue
                ms = (at - started) * 1000
                durations[tool].append(ms)
                events.append((started, tool, tool in MUTATE, ms))
                ms_by_message[mid_of] += ms
                if tool in VERIFY:
                    verify_ms[tool] += ms
                    verify_n[tool] += 1
                    scoped[(tool, 'scoped' if any(arguments.get(k) for k in NARROW) else 'whole')] += 1
                if tool == 'Agent':
                    agent_ms.append(ms)
                if tool == 'Bash' and SLEEP.search(arguments.get('command', '') or ''):
                    sleep_ms += ms
    tools_per_session.append(len(session_tools))
    for count in by_message.values():
        parallel[min(count, 8)] += 1
    for message_id, spent in ms_by_message.items():
        if by_message.get(message_id, 0) == 1:
            serial_ms += spent
    for (tool, _), count in seen_calls.items():
        if count > 1:
            dupes[tool] += count - 1
    for i in range(len(msg_order) - 1):
        first, second = msg_calls[msg_order[i]], msg_calls[msg_order[i + 1]]
        if len(first[1]) != 1 or len(second[1]) != 1:
            continue
        produced = result_text.get(first[1][0][2], '')
        wanted = []
        strings(second[1][0][1], wanted)
        if not wanted or not produced or any(value in produced for value in wanted):
            continue
        unspent[(first[1][0][0], second[1][0][0])] += 1
        if first[0] and second[0] and second[0] >= first[0]:
            unspent_gap.append((second[0] - first[0]) * 1000)
    for i in range(len(order) - 1):
        pair = (order[i][0], order[i + 1][0])
        if pair[0] != pair[1]:
            chains[pair] += 1
            if order[i][1] and order[i][1] == order[i + 1][1]:
                same_target[pair] += 1
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and order[j + 1][0] == order[i][0]:
            j += 1
        span = j - i + 1
        if span >= 3:
            runs[order[i][0]] += span
            longest[order[i][0]] = max(longest[order[i][0]], span)
        i = j + 1
    i = 0
    while i < len(arg_order):
        j = i
        while j + 1 < len(arg_order) and arg_order[j + 1][0] == arg_order[i][0]:
            j += 1
        key = BATCH.get(arg_order[i][0])
        if key and j > i and not any(arg_order[k][1].get(key) for k in range(i, j + 1)):
            batch_missed[arg_order[i][0]] += j - i + 1
        i = j + 1
    touched = collections.defaultdict(set)
    for tool, spot in order:
        if spot:
            touched[spot].add(tool)
    for group in touched.values():
        if 2 <= len(group) <= 4:
            refetch[tuple(sorted(group))] += 1
    events.sort()
    last = {}
    for when, tool, _, _ in events:
        if tool in VERIFY:
            if tool in last:
                repeat_gap.append(when - last[tool])
            last[tool] = when
    for index, start in enumerate(marks):
        end = marks[index + 1] if index + 1 < len(marks) else (events[-1][0] if events else start)
        span = [e for e in events if start <= e[0] <= end]
        if len(span) < 4:
            continue
        cycles += 1
        first_call.append((span[0][0] - start) * 1000)
        cycles_s.append((span[-1][0] + span[-1][3] / 1000) - span[0][0])
        cycles_c.append(len(span))
        cut = None
        for k in range(len(span) - 1, -1, -1):
            if span[k][2]:
                cut = k
                break
        if cut is None:
            continue
        tail, body = span[cut + 1:], span[:cut + 1]
        if tail:
            tail_s.append((tail[-1][0] + tail[-1][3] / 1000) - tail[0][0]); tail_c.append(len(tail))
        if body:
            body_s.append((body[-1][0] + body[-1][3] / 1000) - body[0][0]); body_c.append(len(body))
    for count in edited.values():
        if count > 1:
            rework[min(count, 6)] += 1
    per_session.append((session_steers, user_turns, assistant_turns,
                        session_edits, session_gates, str(project)))

payloads.sort(reverse=True)
total_calls = sum(calls.values())
total_chars = sum(result_chars.values())
builtin = sum(c for t, c in calls.items() if t in BUILTIN)
tool_ms = sum(sum(v) for v in durations.values())
turn_ms = sum(turns); gap_ms = sum(gaps); gp50 = q(gaps, .5)
msgs = sum(parallel.values())
multi = sum(v for k, v in parallel.items() if k > 1)


def show(label, value):
    print(f'{label:<32}{value}')


print(f'== SECTION A  PAYLOAD  window {WEEKS} week(s)  transcripts={len(sessions)}  roots={len(roots)}')
show('records', f'{records:,} ({with_message:,} carry a message, '
                f'{(records - with_message) * 100 // max(records, 1)}% do not)')
show('record types', dict(types.most_common(8)))
show('tool calls', f'{total_calls:,}  builtin={builtin * 100 // max(total_calls, 1)}%')
show('tool-result chars', f'{total_chars:,} (~{total_chars // 4:,} tok)')
show('tool-input chars', f'{sum(input_chars.values()):,}')
show('spilled tool results', f'{persisted_n} records / {persisted_bytes:,} B  '
                            f'(sidecar dir: {spill_files} files / {spill_bytes:,} B)')
show('attachment context', f'{sum(attach_n.values()):,} records / {sum(attach.values()):,} B '
                           f'= {sum(attach.values()) * 100 // max(total_chars, 1)}% of tool-result volume')
show('thinking', f'{think_n:,} blocks, {think_sealed:,} sealed; {think_chars:,} chars readable '
                 f'- the cost is already inside output_tokens')
show('mcp structuredContent', f'{mcp_structured:,} records / {mcp_structured_chars:,} chars')
show('cache', f'read={cache_read_total:,} write={cache_write_total:,} '
              f'hit={cache_read_total * 100 // max(cache_read_total + cache_write_total, 1)}%  '
              f'mid-session re-prime={cache_churn:,} tok')
show('distinct tools per session', f'p50={q(tools_per_session, .5):.0f} p90={q(tools_per_session, .9):.0f} '
                                   f'max={max(tools_per_session or [0])}')

print('\n== token ledger per model (base-input-equivalent: read 0.1x, 5m write 1.25x, 1h write 2.0x)')
for model, counter in sorted(by_model.items(), key=lambda kv: -sum(kv[1].values())):
    bie = (counter['input_tokens']
           + counter['cache_read_input_tokens'] * 0.1
           + counter['ephemeral_5m_input_tokens'] * 1.25
           + counter['ephemeral_1h_input_tokens'] * 2.0
           + counter['cache_creation_input_tokens'] * 1.25)
    print(f'  {model:<30}in={counter["input_tokens"]:>12,}  read={counter["cache_read_input_tokens"]:>14,}  '
          f'1h={counter["ephemeral_1h_input_tokens"]:>12,}  5m={counter["ephemeral_5m_input_tokens"]:>11,}  '
          f'out={counter["output_tokens"]:>11,}  BIE={bie:>15,.0f}')

print('\n== tools: payload, latency, failure rate')
for tool, chars in result_chars.most_common(25):
    spent = durations[tool]
    n = max(calls[tool], 1)
    print(f'  {tool:<40}{calls[tool]:>5}x  {chars:>10,} ch  {chars // n:>7,} ch/call  '
          f'in={input_chars[tool] // n:>5} ch/call  p50={q(spent, .5):>7.0f}ms  '
          f'p90={q(spent, .9):>8.0f}ms  p99={q(spent, .99):>9.0f}ms  '
          f'tot={sum(spent) / 60000:>6.1f}m  err={errors[tool] * 100 / n:>5.1f}%  '
          f'steer={steers[tool]}  dup={dupes[tool]}')

print('\n== trim ledger: tokens that carried no information (estimates, see M3 for the tokenizer)')
for label, tokens in trim.most_common():
    print(f'  {label:<34}~{tokens:>9,} tok  {tokens * 100 / max(total_chars // 4, 1):>5.2f}% of output')
print('  -- measured placebo, DO NOT log as a saving (<=0.1% of tokens on real payloads) --')
for label, chars in placebo.most_common():
    print(f'  {label:<34} {chars:>9,} chars, ~0 tokens')

print('\n== boilerplate lines (>=5 uses, >=3 projects, no path text)')
shown = 0
for text, count in line_freq.most_common(400):
    if count >= 5 and len(line_projects[text]) >= 3 and not ABSPATH.search(text):
        print(f'  {count:>6}x  ~{len(text) * count // 4:>8,} tok  {text[:60]!r}')
        shown += 1
        if shown == 20:
            break

print('\n== attachments (harness-injected context nobody calls for)')
for label, size in attach.most_common(10):
    print(f'  {label:<34}{attach_n[label]:>6} records  {size:>12,} B  ~{size // 4:>9,} tok')

print('\n== attribution: who spent it')
for (key, name), count in attribution.most_common(15):
    print(f'  {key[11:]:<14}{name[:34]:<36}{count:>7} records  '
          f'~{attributed_tokens[(key, name)]:>9,} output tok')
print(f'  delegations={delegations}  self-reported tokens={delegated_tokens:,}  '
      f'wall={delegated_ms / 3.6e6:.1f} h')

print('\n== friction')
show('error codes', dict(error_codes.most_common(10)))
show('api errors', dict(api_errors.most_common(6)))
show('permission denials', dict(denials))
show('queue enqueue/remove', f'{queue.get("enqueue", 0)}/{queue.get("remove", 0)}')
show('interruptions', interrupted)
show('truncated (max_tokens)', stops.get('max_tokens', 0))
show('truncation signals', dict(structured_trunc))
show('bash breakdown', dict(bash_kind.most_common()))
for pre, post, trigger, ms in compactions:
    print(f'  compaction {trigger}: {pre:,} -> {post:,} tokens in {ms / 1000:.0f}s')

print('\n== 15 largest single tool results')
for size, tool, project in payloads[:15]:
    print(f'  {size:>9,} ch  {tool:<34}{project}')

print('\n== per project')
for project, counter in sorted(per_project.items(), key=lambda kv: -sum(kv[1].values())):
    top = ', '.join(f'{t}x{c}' for t, c in counter.most_common(6))
    print(f'  {sum(counter.values()):>6}  {str(project)[:44]:<46}{top}')

print(f'\n== SECTION B  SPEED  cycles={cycles}')
show('turn wall time', f'{turn_ms / 3.6e6:.1f} h over {len(turns):,} turns  p50={q(turns, .5) / 1000:.0f}s')
show('tool wall time', f'{tool_ms / 3.6e6:.1f} h  serial={serial_ms * 100 / max(tool_ms, 1):.0f}%')
show('model gap (result->next call)', f'{gap_ms / 3.6e6:.1f} h  p50={gp50:.0f}ms '
                                      f'p90={q(gaps, .9):.0f}ms  n={len(gaps):,}')
show('ROUND-TRIP COST', f'{gp50:.0f}ms of model gap before any tool runs')
show('gap share of active time', f'{gap_ms * 100 / max(gap_ms + tool_ms, 1):.0f}% '
                                 f'(PASTE breakeven for speculative execution is >5%)')
show('time to first call in cycle', f'p50={q(first_call, .5) / 1000:.1f}s p90={q(first_call, .9) / 1000:.1f}s')
show('PARALLELISM calls/message', f'{sum(k * v for k, v in parallel.items()) / max(msgs, 1):.3f}  '
                                  f'multi={multi:,} of {msgs:,} = {multi * 100 / max(msgs, 1):.1f}%  '
                                  f'round trips already deleted={sum((k - 1) * v for k, v in parallel.items() if k > 1):,}')
show('  histogram', '  '.join(f'{n}:{c}' for n, c in sorted(parallel.items())))
show('REDUNDANT call share', f'{sum(dupes.values()) * 100 / max(total_calls, 1):.1f}% '
                             f'({sum(dupes.values()):,} of {total_calls:,}; MCP-Bench top mark is <10%)')
show('TASK CYCLE wall', f'p50={q(cycles_s, .5) / 60:.1f} min  p90={q(cycles_s, .9) / 60:.1f} min  '
                        f'mean={sum(cycles_s) / max(len(cycles_s), 1) / 60:.1f} min')
show('TASK CYCLE calls', f'p50={q(cycles_c, .5):.0f}  p90={q(cycles_c, .9):.0f}  '
                         f'mean={sum(cycles_c) / max(len(cycles_c), 1):.1f}')
show('  body (to last edit)', f'{sum(body_s) / 3600:.1f} h  calls mean={sum(body_c) / max(len(body_c), 1):.1f}')
show('  TAIL (after last edit)', f'{sum(tail_s) / 3600:.1f} h = '
                                 f'{sum(tail_s) * 100 / max(sum(tail_s) + sum(body_s), 1):.0f}%  '
                                 f'calls mean={sum(tail_c) / max(len(tail_c), 1):.1f}')

print('\n== UNSPENT PARALLELISM: adjacent single-call messages, second call provably independent')
print(f'  {sum(unspent.values()):,} pairs  ~{sum(unspent_gap) / 3.6e6:.2f} h of gap  '
      f'(HEURISTIC: a shared literal counts as a dependency, so this UNDER-reports)')
for (first, second), count in unspent.most_common(12):
    print(f'  {count:>6}x  {first} + {second}')

print('\n== UNUSED BATCH PARAMETER: consecutive calls of a tool that already takes a list')
for tool, count in batch_missed.most_common():
    key = BATCH.get(tool, '?')
    print(f'  {tool:<34}{count:>6} calls  {key}=  unused  ~{count * gp50 / 3.6e6:>5.2f} h of gap')

print('\n== tools ranked by TOTAL WALL TIME (the speed lever, not the token lever)')
for tool, spent in sorted(durations.items(), key=lambda kv: -sum(kv[1]))[:25]:
    n = max(calls[tool], 1)
    print(f'  {tool:<34}{calls[tool]:>6}x  tot={sum(spent) / 3.6e6:>6.2f}h  '
          f'{sum(spent) * 100 / max(tool_ms, 1):>5.1f}%  p50={q(spent, .5):>7.0f}  '
          f'p90={q(spent, .9):>8.0f}  p99={q(spent, .99):>9.0f}  '
          f'err={errors[tool] * 100 / n:>4.1f}%  dup={dupes[tool]}')

print('\n== verification: the gate tax')
verify_total = sum(verify_ms.values())
for tool, ms in verify_ms.most_common():
    print(f'  {tool:<18}{verify_n[tool]:>6}x  {ms / 3.6e6:>6.2f}h  {ms * 100 / max(verify_total, 1):>5.1f}%  '
          f'mean={ms / max(verify_n[tool], 1) / 1000:>7.1f}s')
print(f'  TOTAL {sum(verify_n.values())}x {verify_total / 3.6e6:.2f}h = '
      f'{verify_total * 100 / max(tool_ms, 1):.0f}% of tool time, '
      f'{sum(verify_n.values()) / max(cycles, 1):.1f} verification calls per task cycle')
print(f'  scoped vs whole: {dict(sorted(scoped.items()))}')
print(f'  identical verification re-run in one session: n={len(repeat_gap):,}  '
      f'median gap={q(repeat_gap, .5) / 60:.1f} min  under 5 min apart={sum(1 for g in repeat_gap if g < 300):,}')

print('\n== duplicate calls (identical args, one session) = pure wasted round trips')
dup_calls = sum(dupes.values())
dup_ms = sum(dupes[t] * q(durations[t], .5) for t in dupes)
print(f'  {dup_calls:,} calls  {dup_ms / 3.6e6:.2f}h of tool time + {dup_calls * gp50 / 3.6e6:.2f}h of model gap')
for tool, count in dupes.most_common(10):
    print(f'  {tool:<34}{count:>6} dup  p50={q(durations[tool], .5):>7.0f}ms  '
          f'~{count * (q(durations[tool], .5) + gp50) / 3.6e6:>5.2f}h')

print('\n== blind waits and delegation')
print(f'  sleep-bearing Bash declared {slept:,}s = {slept / 3600:.1f}h, actual wall {sleep_ms / 3.6e6:.1f}h')
print(f'  BARE sleeps (no while/until/for guard): {sleep_bare} calls, {sleep_bare_s:,}s = {sleep_bare_s / 3600:.1f}h')
if agent_ms:
    print(f'  Agent (subagent): {len(agent_ms)}x  {sum(agent_ms) / 3.6e6:.2f}h  '
          f'p90={q(agent_ms, .9) / 1000:.0f}s p99={q(agent_ms, .99) / 1000:.0f}s max={max(agent_ms) / 1000:.0f}s')

print('\n== chains: each fused pair deletes ONE round trip = one model gap + one tool call')
for (first, second), count in chains.most_common(24):
    if count < 8:
        break
    bridge = result_chars[first] // max(calls[first], 1)
    print(f'  {count:>6}x  {first} -> {second}  same-target={same_target[(first, second)] * 100 // max(count, 1)}%  '
          f'intermediate={bridge // 4:>7,} tok/call  '
          f'~{count * (gp50 + q(durations[first], .5)) / 3.6e6:>5.2f}h if fused')

print('\n== same-tool runs >=3 consecutive (batch or parallelise: each deletes a model gap)')
for tool, count in runs.most_common(12):
    framing = input_chars[tool] // max(calls[tool], 1)
    print(f'  {tool:<34}{count:>6} calls in runs  longest={longest[tool]:>3}  in={framing:>5} ch/call  '
          f'~{count * gp50 / 3.6e6:>5.2f}h of gap  ~{count * framing // 4:>8,} tok of repeated framing')

print('\n== one target touched by several tools in one session (re-fetch / fusion candidates)')
for group, count in refetch.most_common(12):
    print(f'  {count:>6}x  {" + ".join(group)}')

print(f'\n== SECTION C  EMITTED CODE AND INTERVENTIONS')
show('edit calls scanned', f'{sum(edit_calls.values()):,} ({edits_scanned:,} carried C#-shaped text)')
show('emitted C# volume', f'{emitted_lines:,} lines / {emitted_chars:,} chars')
show('POSITIVE CONTROL', f'lines>scanned={emitted_lines > edits_scanned} '
                         f'- False means the regexes are dead, NOT that the code is clean')
show('user turns after turn 1', f'{prompts:,} interventions={interventions:,} '
                                f'({interventions * 100 // max(prompts, 1)}% of prompts, '
                                f'{interventions / max(len(sessions), 1):.2f}/session)')
show('edits per user turn', f'{sum(edit_calls.values()) / max(prompts, 1):.2f}')

print('\n== legacy syntax the agent WROTE (per 1000 emitted lines, HEURISTIC until confirmed)')
for label, hits in legacy.most_common():
    rate = hits * 1000 / max(emitted_lines, 1)
    top = ', '.join(f'{t}x{c}' for t, c in legacy_tools[label].most_common(3))
    print(f'  {label:<46}{hits:>6} {rate:>7.2f}/kloc sessions={len(legacy_sessions[label]):>3} {top}')

print('\n== slow constructs the agent WROTE (same units, same caveat)')
for label, hits in slow.most_common():
    rate = hits * 1000 / max(emitted_lines, 1)
    print(f'  {label:<46}{hits:>6} {rate:>7.2f}/kloc sessions={len(slow_sessions[label]):>3}')

print('\n== edit rejection ledger (input re-paid on every retry)')
for tool, count in edit_calls.most_common():
    bad = edit_rejects[tool]
    print(f'  {tool:<44}{count:>5}x rejected={bad:>4} = {bad * 100 / max(count, 1):>5.1f}% '
          f'wasted-in={wasted_input[tool]:>9,} ch ~{wasted_input[tool] // 4:>7,} tok')

print('\n== trap ledger (each one is an extra call nobody asked for)')
for label, count in traps.most_common():
    print(f'  {label:<40}{count:>5}x re-paid input {trap_input[label]:>9,} ch ~{trap_input[label] // 4:>7,} tok')

print('\n== intervention cues (labels only - no prompt text ever leaves this script)')
for label, count in cues.most_common():
    print(f'  {label:<24}{count:>6}')
print('  -- tool called immediately before an intervention --')
for tool, count in before.most_common(12):
    print(f'  {tool:<44}{count:>6}')

print('\n== gates red AFTER an edit in the same session (rework the agent caused)')
for tool, count in after_edit.most_common():
    print(f'  {tool:<44}{count:>6} red of {gate_red[tool]} red overall')
print('  -- rule ids named in gate output --')
print('  ' + ', '.join(f'{r}x{c}' for r, c in rules.most_common(15)))

print('\n== same target edited N times in one session (rework distribution)')
print('  ' + '  '.join(f'{n}:{c}' for n, c in sorted(rework.items())))

print('\n== sessions ranked by interventions')
for row in sorted(per_session, reverse=True)[:10]:
    steers, user_turns, assistant_turns, edits, gates, project = row
    print(f'  {steers:>3} interventions user={user_turns:>3} asst={assistant_turns:>4} '
          f'edits={edits:>4} gates={gates:>3} {project[:44]}')

print(f'\n== CORPUS weeks={WEEKS} transcripts={len(sessions)} records={records} calls={total_calls} '
      f'builtin={builtin * 100 // max(total_calls, 1)}% resulttok={total_chars // 4} '
      f'attachtok={sum(attach.values()) // 4} trimtok={sum(trim.values())} '
      f'chains={sum(chains.values())} runcalls={sum(runs.values())}')
print(f'== SPEEDLINE cycles={cycles} callspercycle={sum(cycles_c) / max(len(cycles_c), 1):.1f} '
      f'cyclemin={sum(cycles_s) / max(len(cycles_s), 1) / 60:.1f} turnh={turn_ms / 3.6e6:.1f} '
      f'toolh={tool_ms / 3.6e6:.1f} gaph={gap_ms / 3.6e6:.1f} gapp50={gp50:.0f} '
      f'callspermsg={sum(k * v for k, v in parallel.items()) / max(msgs, 1):.3f} '
      f'multipct={multi * 100 / max(msgs, 1):.1f} unspent={sum(unspent.values())} '
      f'batchmissed={sum(batch_missed.values())} redundantpct={dup_calls * 100 / max(total_calls, 1):.1f} '
      f'verifyh={verify_total / 3.6e6:.1f} duph={(dup_ms + dup_calls * gp50) / 3.6e6:.1f} '
      f'baresleeps={sleep_bare}')
print(f'== EMITTED editcalls={sum(edit_calls.values())} rejects={sum(edit_rejects.values())} '
      f'lines={emitted_lines} legacy={sum(legacy.values())} slow={sum(slow.values())} '
      f'prompts={prompts} interventions={interventions} traps={sum(traps.values())}')
```

Run it as `python <script> <WEEKS>` **in the background**, and read the **whole** output when it
lands. **Do not** re-derive any number by hand afterwards — the script is the measurement of record,
and the next run must be comparable.

**Six corrections this script encodes, each one a measured under-report in a version before it:**

1. **It reads every record class, not just the ones with a `message.content` list.** That guard alone
   dropped **73 915 of 282 061 records (26.2%)** — every `turn_duration` (348.0 h of wall clock),
   every `compact_boundary`, every `api_error`, every permission denial, and 70.0 MB of `attachment`
   context.
2. **It has a time axis.** Pairing `tool_use.id` to `tool_result.tool_use_id` and subtracting the two
   records' `timestamp` values resolves **99.6%** of calls (73 186 of 73 448) and yields
   p50 367 ms / p90 12 437 ms / p99 182 810 ms over 190.6 h. Without it the whole `[speed]` goal is
   unmeasurable.
3. **It keys tokens by `message.model` and splits `cache_creation` five ways.** The corpus spans
   **8 models**, and cache writes are **97.1% one-hour** (797 152 487 vs 24 013 041), which bills at
   2.0× base input against 1.25× for the five-minute tier — while cache *reads* are **98.8%** of all
   input-side volume (33.30 B vs 387.9 M). Summing four counters unweighted is not a cost model. It
   reports a **base-input-equivalent** total rather than dollars, because a price it cannot prove is
   exactly the confident wrong answer this repo bans. It also reports the **hit rate** and the
   **mid-session re-prime** volume, because a cache write after the sixth message of a session means
   the stable prefix was broken — tool-schema churn, a reordered block, a rewritten history — and the
   invalidation is asymmetric: the broken prefix loses the 10% read rate *and* re-pays the 25% write
   premium over the whole prefix.
4. **It counts the payload that leaves the transcript.** `toolUseResult.persistedOutputSize` (52
   records, 13.1 MB), the `tool-results/` sidecar directory (257 files, 71.7 MB), 70.0 MB of
   attachments — `skill_listing` alone is **39.0 MB** — and 4 685 `mcpMeta.structuredContent`
   payloads. `len(tool_result.content)` sees none of it.
   **Do not price `thinking` from its text.** Recent transcripts persist a *sealed* block: `thinking`
   is the empty string and only `signature` survives (475 of 475 in a spot-checked window), so a
   character count silently reports zero. Thinking bills as output and is already inside
   `output_tokens` — count the blocks, report how many are sealed, take the cost from the ledger.
5. **It divides errors by calls and attributes cost to a skill, a server and an agent.** A raw error
   count ranks the busiest tool first; a *rate* found the real defects — `Write` **12.4%**
   (397/3 198) against `read_text` **0.0%** (0/2 766), and inside TerseSharp itself `find_usages`
   **7.7%** (17/222) and `find_files` **7.6%** (16/211), two tools whose arguments an agent evidently
   guesses wrong.
6. **It scores parallelism three ways, and that is the whole input to gate 9.** A histogram cannot see
   a composite, a batch or an unspent parallel pair: all three are properties of *call order inside
   one session*.
   - **chains** — A immediately followed by B, with the same-target share and the intermediate payload
     a fused call would never return;
   - **runs** — ≥3 consecutive calls of one tool, the batch signature;
   - **fan-out** — `tool_use` blocks per API `message.id`, the parallelism the agent already achieved
     unaided, which any batch parameter has to beat rather than duplicate;
   - **unspent parallelism** — two adjacent messages each carrying exactly one call, where **none of
     the second call's string arguments appears anywhere in the first call's result**. That is a
     computable proof of independence, so the pair could have been one message and its whole model gap
     was paid for nothing. A shared literal — a workspace name, a common path — counts as a dependency,
     so the detector **under-reports by construction**, which is the safe direction. It groups calls by
     API `message.id` **before** pairing them: the JSONL splits one message of N `tool_use` blocks into
     N records, so a detector that walks records would compare a message's own parallel siblings —
     which are independent by definition — and report the corpus's best behaviour as its worst;
   - **unused batch parameter** — consecutive calls of a tool that *already* accepts a list
     (`read_text paths=`, `write_text files=`, `edit_text edits=`, `replace_symbol symbolIds=`) where
     no call in the run passed it. This is the highest-value class the script produces: the capability
     shipped, the agent did not use it, so the lever is `(e)` teaching and costs no server change at
     all;
   - **redundant share** — identical arguments inside one session, against MCP-Bench's `<10%` bar.

**Two refinements, so the script does not over-claim either:**

- **`toolUseResult` is on 76% of tool results, not all of them** (55 602 of 73 426). Read it when it
  is there; never assume it.
- **Structured truncation and the substring steer measure different things** — `toolUseResult
  .truncated` fired **20** times, the `narrow with` / `(truncated=true` sniff **1 530**. The boolean
  is harness-level truncation, the sniff is TerseSharp's own steer. Keep both; neither subsumes the
  other.

**Privacy, mechanically enforced:** the boilerplate section prints a line only when it appears in ≥3
different projects and contains no path-shaped text, which is what makes it *tool framing* rather
than user content. Never widen that filter. `line_freq` is pruned at 300 000 entries, so past ~8
weeks that section is a floor, not a total — say so in the report. The unspent-parallelism detector
holds result text in memory **capped at 20 000 characters per call** and prints only tool-name pairs;
the intervention cues print labels and counts and never a matched string.

## M3 — The micro-trim pass: every character in TerseSharp's own responses

Section A's trim ledger estimates. This phase **measures with a tokenizer**, because characters are
what a counter can see and tokens are what is paid, and the two disagree so violently that three of
the four "obvious" trims are worth nothing at all.

### M3.1 — Run the tokenizer experiment first, every time

`pip install tiktoken`, then measure the **real** widest response of each `mcp__terse-sharp__*` tool
in the corpus — take them from section A's largest-payload list, or from a live call. Never reason
about tokens from character counts.

```python
import tiktoken
o200k = tiktoken.get_encoding('o200k_base')
cl100k = tiktoken.get_encoding('cl100k_base')
def n(text, enc=o200k): return len(enc.encode(text))
```

For each candidate trim, encode the payload before and after, in **both** encodings. A trim is real
only when the saving survives both — that is the only defence available against the fact that neither
encoding is Claude's.

> **The tokenizer caveat is not a footnote, it is the calibration.** Anthropic's own documentation:
> *"Claude Fable 5 and Claude Mythos 5 use the tokenizer introduced with Claude Opus 4.7, which
> produces roughly 30 percent more tokens than models before Claude Opus 4.7 for the same text"*, and
> *"don't reuse token counts measured on a model before Claude Opus 4.7 to estimate costs"*. So a
> tiktoken figure is **directional, never absolute**: use it to rank two formats, never to assert a
> budget. Where an absolute number is needed, count it with the `/v1/messages/count_tokens` endpoint
> against the model that will actually bill. Measured against real TerseSharp responses, the
> character-to-token ratio is **4.18**, which is what makes section A's `chars // 4` a safe estimator
> and nothing more.

### M3.2 — The measured verdict table

Measured 2026-08-08 on `o200k_base`, cross-checked on `cl100k_base`, over 20-row synthetic tables
with **varied** rows (identical rows let BPE merge across lines and understate every separator cost)
and over 15 real captured TerseSharp responses, 13 591 tokens total.

| Class | Measured | Verdict |
|---|---|---|
| **Column separator: 2 spaces → 1 space** | **+3.00 tokens per row** on a 4-column table; 2-space and tab cost the same | **The single biggest format lever.** A multi-space run costs exactly **1 token**, whatever its width. One space costs zero |
| Alignment padding (`f'{x:<34}'`) | **1.00 token per padded line** | Same mechanism. Pad for a human, pay a token per line |
| Markdown pipes `\| a \| b \|` | **+4.05 tokens/row** over single-space, +0.35/cell over TSV | Never render a machine-read table as markdown |
| Absolute → workspace-relative path | **61% fewer tokens** on a path-only payload | Highest-value single change on any path-heavy response |
| JSON: minified vs `json.dumps` default | **+31%** for the spaces after `:` and `,`; `indent=2` is **+68%** | If a response is JSON, minify it |
| Header-once rows (TSV) vs minified JSON | **26% fewer** | Real, but a third of what the separator fix gives, and it carries an accuracy caveat (below) |
| Indentation | 1 space or a tab = **0 tokens**; 2+ spaces = **exactly 1 token/line**, at any depth | Depth is free. *Existence* is not |
| Thousands separators | **+1 token per group** (`1234567` 3→5) | Drop them. Digit runs already chunk at ≤3, and modern Claude tokenizes numbers right-to-left, so the commas buy no arithmetic accuracy either |
| Newline per record vs space-separated | **+0.95 tokens/record** | Usually worth paying — it is the record boundary |
| **Abbreviation** (`errors`→`err`, 20 pairs) | **19 of 20 save exactly zero**; `changed`→`chg` **costs +1** | **Banned as a proposal.** A common word is already one token |
| Identifier casing | `FileService`, `file_service`, `fileservice`, `FILESERVICE` all tie | No lever. Do not churn names for tokens |
| **Blank lines** | 8 tokens across 13 591 = **0.1%** | **Placebo. Banned as a proposal** |
| **Trailing whitespace** | 9 tokens across 13 591 = **0.1%** | **Placebo. Banned as a proposal** — but never let a payload *end* on whitespace, which is a prompt-boundary defect, not a cost one |
| Dedenting real responses | 65 tokens = **0.5%** | Marginal |
| **Collapsing internal multi-space runs** | **343 tokens = 2.5%** of all real captured output | The real long tail, and it is the same mechanism as row 1 |

A trim not in that table needs its own before/after measurement in this run. A trim contradicted by
that table is not proposed at all — and if a past run logged one, close the row as refuted.

### M3.3 — Read the widest response of every tool against the framing checklist

The verdict table prices *how* text is written. This checklist finds text that should not be there:

| Class | What to look for | How to price it |
|---|---|---|
| **Framing** | a header restating the request, an echoed argument, the tool's own name opening its answer, a closing line repeating the first | tokens × calls of that tool in the corpus |
| **Column value** | a column that is constant across records, derivable from the request, or never referenced by the *next* tool call | column tokens × record count |
| **Constant tags** | `EXACT` on a tool that can only answer `EXACT`, `(truncated=false)`, `errors=0 warnings=0` on a tool that fails loudly, `remedy:` on a success | straight from section A's ledger |
| **Verbose-only leakage** | anything the success path emits that only `verbose=true` should — still the highest-yield class in this repo | removed tokens × success calls |
| **Repeated line prefix** | every record opening with the same file, symbol or project | grouped form pays the prefix once — but see the safety rule below |
| **Plural prose** | `N results found for …` where `N` alone is unambiguous; `no matches were found` where `0` is the answer | tokens × calls |

**Safety rule, learned the hard way and non-negotiable: drop framing, never edit payload.** Folding a
shared directory prefix or hoisting a shared confidence tag by *rewriting record text* corrupts any
payload that legitimately contains that string — the exact bug that made a `get_symbol_source` of the
constant defining `EXACT` come back silently altered. Prefix hoisting is also worth only ~3.8
tokens/record against ~10 for absolute→relative, so do the safe one first.

### M3.4 — Audit the surface itself, which is paid whether a tool is called or not

1. `search_regex` over `src/TerseSharp.Server/Tools/` for `\[Description\(` with a high `maxResults`;
   sum the returned line lengths. That is the floor of what the 88-tool advertised surface costs on
   **every request**. Multiply by the assistant-turn count from section A to price it.
2. **Cross it with `distinct tools per session` from section A.** That number is the case for
   *deferred* loading, and it is the 2026 industry default: a session that touches 12 of 88 tools pays
   the other 76 on every request for nothing. Anthropic's Tool Search Tool measured **~85% fewer
   tokens** on a 50+ tool surface (≈77 K → ≈8.7 K, the search tool itself ~500 tokens); progressive
   loading reference numbers run to 98.7% on very large surfaces. TerseSharp already ships the
   mechanism — `--tools` profiles, `ToolGroups`, `.terse.json` — so the row is almost never "build
   deferral", it is **"the default profile advertises more than the p90 session uses"**, priced as
   `surface tokens × (1 − p90 distinct/88) × assistant turns`.
3. Flag every description that repeats the tool's own name, explains *how* rather than what it
   returns and which built-in it replaces, restates parameters the schema already declares, or spends
   words on a case the `remedy:` already teaches. **But do not confuse this with "shorter is better"**
   — Anthropic's guidance is to write *detailed* tool descriptions, and the measured accuracy lever
   points the other way: embedded tool-use examples took complex parameter handling from **72% to
   90%**. Cut redundancy, not information; a worked example that prevents one malformed call has paid
   for itself several times over.
4. `read_text src/TerseSharp.Server/Assets/SKILL.md headings=true`, then price each section by byte
   size. The skill is loaded whole into an agent's context.
5. **Never propose deleting a `Replaces Bash …` prefix.** `ToolCensusE2ETests` enrols the guard from
   it, so cutting it silently un-enrols the tool. Record that constraint in the row's `Rejected` cell.
6. **Schema churn is a cache row, not a wording row.** Tool definitions sit in the cached prefix; a
   reordered or dynamically-assembled schema block re-pays the whole prefix at the 1.25× write rate.
   If section A's `mid-session re-prime` is large, check that the advertised order is deterministic
   before proposing anything about description length.

### M3.5 — The counter-evidence, which caps how far this phase may go

Compression that changes *semantics* buys tokens with accuracy, and this repo would rather pay the
tokens:

- The two shape-changing classes are settled in M7.1 and cap this phase: **TOON −18% at −9pp**,
  **TRON −27% at −14pp**, **LLMLingua 71% reduction on code → 0% QA accuracy**. All three are far
  worse trades than the 26–61% available from minification, relative paths and de-framing **at zero
  semantic change**, and anything that can delete a character inside a path, an error code or an
  identifier is unsafe for tool output.
- Header hoisting is not free either: repeated per-row keys let a model attend to each row
  independently, and JSON has been measured to *beat* markdown on per-row lookup despite costing more
  tokens. Prefer it for uniform bulk records; do not apply it to a payload the agent looks things up
  in.
- The reason to trim anyway: fewer tokens is itself an accuracy win. Performance degrades
  continuously with input length well before the context limit ("context rot"), and a single
  distractor record measurably lowers accuracy — so framing removed is haystack removed, not just
  cost removed.

So the ranking for this phase: **de-frame → relativise paths → collapse multi-space runs → minify →
only then consider changing the shape of the data.**

## M4 — The speed pass: read section B against the five levers

Tokens are half the prime directive; **speed is the other half, and it is the half the user actually
waits through.** A server that answers cheaply but turns the loop 26 times to finish one task has lost
the session.

### M4.1 — The baseline every threshold is calibrated on

The 1-week window of **2026-08-26** — 305 transcripts, 36 075 calls, 663 task cycles:

| Measured | Figure | Lever |
|---|---|---|
| **fan-out** | **1.165 calls per assistant message; 14.3% of messages carry two or more** (52 292 messages, 60 912 calls, histogram 1:44 810 2:6 768 3:470 4:170 5+:74), so **8 620 round trips were already deleted = 14.7 h of gap not paid** | **(b)** — free, and the largest remaining `[speed]` headroom. Measured A/B in-loop: 8 outlines one-per-message = **151.4 s** wall (2.9 s tool, **148.5 s gap**) against **10.2 s** as one `paths=[…]` call — **14.8×, 98% of it gap** |
| **model gap** | **102.9 h**, p50 **6 097 ms**, p90 21 648 ms, over 35 967 gaps | the floor under every round trip. It is why **(a)/(b)/(d)/(e) beat (c)** |
| **verification** | **64.5 h** over **4 856** calls = **41% of all tool wall time**, **7.3 verification calls per task cycle** | **(d)** — `run_tests` alone is **58.0 h**, 2 454 calls, mean **85 s**, p99 **965 s** |
| **duplicates** | **3 008** identical calls in-session = **15.7 h**; `run_tests` **1 183** of 2 454 (48%), `build` **787** of 1 053 (75%) | **(a)** — 1 154 identical verification calls re-issued **under 5 minutes** apart |
| **shell text tools** | 2 369 `Bash` calls = **18.1 h** = 51.5% of all `Bash` wall time | **(d)** — the fallback class the guard exists to delete |
| **bare `sleep`** | **156** calls, **25 307 s = 7.0 h**, largest single **580 s** — later measured down to 15 calls / 175 s while `TaskOutput` polling took its place at **14.08 h, 193 calls, p90 at the 600 s ceiling** | **(d)** — the waiting moved, it did not stop. Any new wait primitive is the same row |
| **subagent** | 184 `Agent` calls, **6.4 h**, p99 **2 303 s**, max **6 589 s** (110 min for one) | **(c)/(d)** — scope the delegation or do not delegate |
| **the tail** | after the last edit: **15%** of post-first-edit wall, mean 6.8 calls, p90 **11.1 min** | the end-of-task gate is real but is **not** where the hours are — the interleaved `edit → run_tests → edit` loop is |

Judge every `mcp__terse-sharp__*` tool against that distribution, not against a feeling:

- a `p90` above the corpus `p90` on a *read* tool is a defect: a read is supposed to be the cheap end.
- a `p99` more than 5× its `p50` is a **cold path** — first call after a load, an eviction, an
  analyzer assembly load, a lazily built compilation. Name the trigger.
- a `tot` in the tens of minutes on a read tool is a **budget** problem regardless of `p50`.
- compare against the built-in it replaces where the corpus has both. **A terse tool slower than the
  `Grep` it replaces is a product defect even when it returns fewer tokens.**
- **a tool whose `dup` count is a double-digit share of its calls is a caching row, and on a
  `VERIFY` tool it is the single most valuable row this phase can produce** — the answer could not
  have changed, and the server is the only thing that can prove it.

### M4.2 — The parallelism deficit, ranked before anything else

Gate 9's three counters convert in this order, because that is their cost order:

1. **unused batch parameter** — the capability exists and was not used. Lever `(e)`: a `SKILL.md`
   swap row, a `[Description]` opening line, a `remedy:` on the second consecutive call, or a guard
   that answers the run with the batch form. **Zero server work, applies to every future session.**
   Price it as `calls in runs × (gap p50 + tool p50)`.
2. **unspent parallelism** — two provably independent calls issued in two messages. Lever `(b)`.
   The published evidence says prompting alone is not sufficient — an LLM-Tool-Compiler result found
   that even detailed instructions to batch do not guarantee multi-tool emission, and the W&D study
   (ICLR 2026) got reliable width only by **injecting the request immediately before the call**, not
   by stating it once in the system prompt. So a row here whose only mechanism is "add a line to
   `CLAUDE.md`" is weak; a row that puts the reminder **at the moment of the call** — a `PostToolUse`
   or `PostToolBatch` hook, a `remedy:` line, a deny message from `ToolGuard` — is the strong form.
3. **redundant share** — identical arguments, one session. Lever `(d)`: the server can *prove* the
   answer did not change (no write in between) and say so instead of recomputing.

**Then check whether speculation is even on the table.** Section B prints the gap share of active
time. PASTE (Microsoft Research, 2026) reports **48.5% lower task completion time and 1.8× tool
throughput** from executing predicted tool calls while the model is still thinking — but it reports
the breakeven too: where tool-idle time is under ~5%, speculation *loses*, and one published run went
from 128 s to 162 s. Only propose a prefetch/speculation row when the corpus gap share clears that
bar and the tool is side-effect-free; a state-changing tool needs the graded form (dry-run, staging),
which in this server means `dryRun=true`, and that is a much bigger row than it looks.

### M4.3 — The three questions this phase must answer in writing

1. **How many calls did one task take, and which of them were not needed?** Duplicates, error retries,
   verification re-runs, unspent-parallel pairs, unused-batch runs, and the second half of every
   same-target chain are all "not needed" — sum them and state the share of `calls per task cycle`
   they represent.
2. **Where did the wall clock go?** Tool time, model gap, and the unaccounted remainder, each as a
   share of turn wall time. A phase that reports tool time only has hidden the larger half.
3. **What would have made the agent finish sooner without changing a single tool?** Parallel
   `tool_use` blocks, a batch parameter it already has, a verification it did not have to re-run, a
   wait it did not have to serve. These are `SKILL.md` / `CLAUDE.md` / guard / hook rows, they cost no
   surface, and the corpus says they are the biggest ones available.

### M4.4 — Memory

The server is a long-lived stdio process holding MSBuild workspaces:

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
   command edits two markdown files. Log it.

Anything measured in this phase is a row with an **hour, millisecond or megabyte** figure in its
`Expected saving` cell, plus the lever letter from gate 8 — never a token figure. Mixing the two units
in one cell is how a saving stops being comparable across runs.

## M5 — Read the evidence behind the top offenders, and confirm what the regexes only ranked

The aggregate says *what* is expensive; only the transcript says *why*. For the **top eight** cost
centres from M2–M4 — a tool with a huge chars/call, a `Bash` class, a duplicated call, a `p99` spike,
an unspent-parallel pair, a subagent file — open the specific `.jsonl` and read enough of the
surrounding turns to state the cause. Use `python` one-liners or `Read` on the transcript (it is
outside every workspace, so built-ins are legal); never paste what you find.

| Class | Signature in the data |
|---|---|
| **Fallback** | a built-in used where an MCP tool existed — `Grep`/`Read` on `.cs`, `Bash: git status`/`git diff`, `dotnet build`/`test`/`format` in a shell, `ls`/`cat`/`sed`. Count them; each is a product or discoverability defect, never "just a habit" |
| **Round trip** | the same answer costing ≥2 calls — outline → source → usages, read → read the same file wider, a truncation steer immediately followed by the same call with a bigger cap |
| **Payload** | tokens returned and never used — whole-file reads where a section was wanted, a listing whose columns nobody read, echoed arguments, a diff on a success response |
| **Truncation near-miss** | a steer whose overflow was a few percent of the cap, so the caller always spends a second call |
| **Duplicate work** | identical tool input repeated inside one session — a re-read after an edit, a re-search with the same query, a status re-check |
| **Error round trip** | `ERROR`/`is_error` results, especially ones needing a retry with different arguments: `AmbiguousWorkspace`, `SymbolNotFound` on a hand-written id, `oldText matched 0 times`, `InvalidArgument` |
| **Speed** | a blind wait, a poll loop that only watches for an artifact, a 10-minute timeout on a 10-second command, a serial sequence that had no dependency, a cold-start cost paid per call |
| **Delegation** | subagent transcripts: what the fan-out cost in tokens versus what its report was worth, and whether the subagent used built-ins on code its parent had an MCP for |
| **Context** | `cache_read` vs `cache_creation` ratio, a session that re-primed context repeatedly, an enormous system/skill payload loaded for a one-call task |
| **Productivity** | permission denials and the retry that followed, a plan re-derived because an earlier answer was not written down, a gate the agent skipped and had to redo |
| **Accuracy** | an answer the agent acted on and later had to undo — a confident wrong result, a stale read, a claim the tool could not prove |
| **Emitted code** | the text the agent *sent* to an edit tool: a legacy construct, a sync-over-async call, an allocation on a hot path, a rule the CI format gate kills |
| **Intervention** | a user turn that corrected, re-scoped, re-ran or unblocked the agent — the extra prompt is the cost, and the tool call immediately before it is the defect |

### M5.1 — Confirm the emitted-code classes before converting them

Section C's two tables are a regex over text with no compiler behind it. Take the **top three of
each**, in count order, and do exactly one of these — naming which one in the row:

| Class section C counted | Confirm with | Lever, in this repo's ranking order |
|---|---|---|
| legacy syntax an analyzer already owns (`IDE0300`, `IDE0161`, `IDE0090`, `IDE0032`, `IDE0066`) | `analyze <file> severity=info` on a file this repo actually has | **mechanism 1** — the edit tools already run a compile gate over the changed declaration; make that gate *report* the info-severity diagnostics the new text introduced, so the agent learns at the write instead of two calls later |
| syntax the build cannot see and the ubuntu leg kills (`IDE0022`, `IDE0060`) | `cleanup verify=true fix=style` | the same mechanism, plus a `remedy:` naming the rule and the fixed form. Highest-value class in the phase: invisible locally, fatal in CI |
| `CA1305` / interpolation as a value converter | `search_regex` over `src/`, then `get_symbol_source` on one hit | a `SKILL.md` / `[Description]` row — intent is not decidable at the write path, so the lever is teaching |
| sync-over-async, sync file I/O, missing `ConfigureAwait(false)` | `analyze` on the named file; the async hard gate in `CLAUDE.md` is the specification | mechanism 1 where an analyzer owns it; otherwise a skill row naming the async gate |
| allocation classes (`Substring`/`Split`/`ToList`/`ToLower`/interpreted `Regex`) | `analyze` (`CA1859`, `CA1861`, `CA1862`, `SYSLIB1045`), else `get_symbol_source` on the member and say which path it sits on | a row **only** when the path is per-file, per-line, per-element or per-symbol — that is the allocation gate's own scope |

Three rules keep this honest:

1. **A class an existing gate already catches is a workflow row, not a code row.** If `analyze` at
   `info` or `cleanup fix=all` would have named it, the defect is that the agent shipped it *to* the
   gate — so the `Tool` cell names `SKILL.md`, `CLAUDE.md` or the tool `[Description]`, and the saving
   is the rework calls from section C's `after_edit` counter.
2. **A class no gate catches, and that the write path could catch, is mechanism 1 and outranks
   everything else here.** Price it as `edit calls × P(diagnostic) × cost(analyze round trip)`.
3. **Never fix a line of C# in this command.** A confirmed class becomes a row; the row is implemented
   by `/ship-improvements`.

**Floor:** ≥5 occurrences across ≥3 sessions, **or** one occurrence of a class that CI kills, **or**
one occurrence of a class the async or allocation hard gate names explicitly. Below that, aggregate by
family into one row. **Cap:** a construct is not slow because it is old. A row that cannot name **the
path's multiplicity** — per file, per line, per element, per symbol — is a style opinion, and this
command does not log style opinions.

### M5.2 — The intervention pass: rank by what preceded it, never by the cue

This is goal `[accuracy]`, and it is the most expensive class in the corpus. The cue histogram says
*how* the user steered; the `before` histogram says *what made them*. Only the second converts. For
the top eight entries of `before`, open two occurrences and classify:

| Shape | Signature | Lever |
|---|---|---|
| **Silent no-op** | the tool answered `applied` / `changedLines=0` and nothing landed | the response must say loudly that it landed nothing — the `replace_symbol` drops-extra-members trap, and the most expensive shape because the agent *believes* the answer |
| **Unprovable answer** | `0 results`, `0 properties`, `NOT_RESOLVED` where the truth was "this tool cannot see that" | the response distinguishes "none" from "out of my reach" |
| **Wrong default** | the first call truncated, was too narrow, or omitted the field the agent needed, and the second call fixed it by argument | a default flipped, or the field folded into the first response |
| **Missing remedy** | an `ERROR` whose `remedy:` did not name the next call, so the retry guessed | the `remedy:` gains a worked example — measured lever 72% → 90% on complex parameter handling |
| **Gate breach** | the user had to name the hard gate the agent walked past | a `ToolGuard` row, or a `SKILL.md` swap-table line — never "the agent should have known" |

**"The agent should have known which tool to call" is banned as a diagnosis.** If the agent guessed
wrong, the schema, the description, the default or the `remedy:` is the defect. An intervention with
no product lever behind it is named in the report's Dropped list with its count.

Every entry in section C's trap ledger converts one-to-one, its `Expected saving` being
`count × (recovery calls + re-paid input tokens)` from the ledger's own columns. Two constraints: a
trap already carried in `CLAUDE.md`'s traps section or already closed in `IMPROVEMENTS-ARCHIVE.md` is
a **discoverability** row, not a new capability row; and a trap whose only fix is "be careful" is not
implementable and does not become a row — state it in the report so the next run re-measures it.

### M5.3 — The ratios that only mean anything as a trend

Record all six in M11 so the next run can compare: **interventions per session**, **edits per user
turn**, **tool calls per task cycle**, **wall minutes per task cycle**, **calls per assistant
message**, **redundant-call share**. A run where edits per user turn falls while edit volume stays
flat is a run where the agent needed more steering for the same output — a finding even when no
single tool looks bad. A goal with no rows is legitimate **only** when M11's goal ledger states what
was measured and why it came back clean.

## M6 — Composite, batch and new-tool synthesis

Section B's sequence axis and M5's round-trip evidence say where the agent spent **two calls on one
answer**. This phase turns that into candidate changes, and it is the only place in this command where
the question *"should there be a new tool?"* may be asked. Asked anywhere else it produces an 89th
tool nobody calls, paid for in every request of every session.

**Six mechanisms, ranked. Take the first one that covers the measured chain:**

| # | Mechanism | Why it ranks there |
|---|---|---|
| **1** | **Make the first response carry what the second was called for** — the id the caller re-derived, the count it re-queried, the declaration it looked up | a format change: no surface, no parameter, no schema token, and it deletes the whole second call |
| **2** | **Teach the call site** — a `remedy:`, a `[Description]` opening line, a `SKILL.md` swap row, a `ToolGuard` deny that names the batch form, a hook that fires at the moment of the call | lever `(e)`: zero server work, and it is the only mechanism that reaches sessions of other agents. The W&D result says the reminder must land **at the call**, not once in a system prompt |
| **3** | **Add a parameter that fuses the pair** — `usages=true`, `source=true`, `context=N` | one schema line against one deleted round trip. Default stays **off**, or every caller pays the fused payload for the case it did not want |
| **4** | **Make the existing tool take a list where it takes one** — the batch case, derived from the runs and unused-batch histograms, never from a feeling | one parameter, N−1 round trips deleted |
| **5** | **Aggregate server-side, or answer by reference** — return the computed answer instead of the rows it was computed from; return a handle (path, symbol id, stored query) the agent can re-enter instead of the payload | the measured industry lever: NVIDIA's harness reached 82.2% on SWE-bench Verified at **29 LLM calls / ~1.1 M tokens per task** against 66 calls / 2.2 M for the comparison, largely by making tool results **live variables with a bounded preview** rather than context text. Anthropic's just-in-time guidance is the same shape: keep lightweight identifiers, load on demand |
| **6** | **A new tool** | only when 1–5 cannot express it, and only against the surface cost priced in M3.4 |

### M6.1 — The composite floor

A chain `A -> B` is a candidate only when **all four** hold, each with its number from section B:

- **frequency** — ≥1 occurrence per session on average, or ≥20 in the window;
- **adjacency** — B is A's *immediate* successor, so a fused call would genuinely have removed a turn;
- **same target** — `same-target` is ≥50% of the pair's count. A pair that mostly walks on to a
  *different* file or symbol is a workflow, not a composite;
- **the intermediate is discarded** — read two occurrences in the transcript and confirm A's payload
  was used only to derive B's arguments. Where A's payload was itself an answer, fusing saves the
  round trip but **not** the tokens; the row says which of the two it claims.

The `intermediate=` column is A's **mean payload over all its calls**, not over this pair's calls — an
estimate, and it is labelled one.

**The counter-case that caps this phase, and it is already refuted: a composite whose second half
fires in fewer than half the cases makes the common case more expensive.** Break-even is
`P(second call) × cost(second call) > cost added to every first call`. Compute it. A candidate that
does not clear it goes in the row's `Rejected` cell, not into the backlog.

### M6.2 — The batch floor

A run of ≥3 consecutive calls of one tool is a batch candidate only when:

- it appears in **≥3 different sessions** — one session's loop is a task shape, not a tool defect;
- the deleted per-call cost is measured: argument framing (`in=… ch/call`) plus per-response framing,
  times the calls inside runs, **plus** `count × gap p50`, which is usually the larger half;
- **the fan-out histogram is read first, and it cuts both ways.** Where the agent already issues
  several `tool_use` blocks in one message, those calls are parallel and a batch parameter only wins
  if the *responses* share framing that could be emitted once. Where the run is serial, the finding is
  the bigger one: the agent serialised calls it never had to.
- **if the tool is already in `BATCH`, this is not a batch row at all** — it is an `unused batch
  parameter` row, mechanism 2, and it outranks every new parameter in this phase because the code is
  already written.

Two constraints every batch proposal states, or it is not implementable here:

- **it answers per item.** A batch that fails whole because item 7 was not found is worse than N
  calls, because the agent cannot tell which item failed: per-item status, per-item error code.
- **it scopes per item.** A batch whose items span workspaces or projects resolves each one — a helper
  that re-derives scope from the raw path answers wrongly and silently, which is a shipped trap in
  this repo, not a hypothetical.

### M6.3 — The re-fetch, duplicate and steer signals

- **re-fetch** (one target, several tools, one session) names the fusion nobody asked for — the same
  file outlined, read and diffed. Rank by count; the row is almost always mechanism 1.
- **`dup`** is the same call twice with identical arguments. That is never a composite: it is an
  **idempotence or a caching** row, or evidence the first answer was not trusted. Say which.
- **`steer`** followed by the same tool with a wider cap is a **default** row, not a new tool — the cap
  is wrong for that tool's real payload. Quantify the overflow, not the call.

### M6.4 — Conversion

Every candidate leaves this phase as a row whose `Tool` cell names **the existing tool and the
parameter**, unless mechanism 6 won on the arithmetic — in which case the row states the surface cost
it beat and the chain it deletes. `Expected saving` is `calls per session × tokens per call`, or
`calls × (gap p50 + tool p50)` for a `[speed]` row, both from the scan, never an estimate of how nice
the tool would be. Anything that cleared no floor is named in the report's Dropped list with its
measured count, never silently.

## M7 — Deep research: what the field knows, launched in parallel with the scan

The corpus says what *this* agent wasted. It cannot say what a better-designed tool surface would
have avoided in the first place. This phase is **mandatory**, it is not a literature review for its
own sake, and it ends in rows. **It is launched in M0's single message, alongside the scan** — it
shares no data with M2 and has no reason to wait for it.

### M7.1 — Start from the standing corpus of already-verified findings

**Do not re-derive what is already settled.** What is settled, with the instrument that settled it:

| Finding | Verified by | Status |
|---|---|---|
| Cache reads are 98.8% of input-side volume; 1h writes are 97.1% of cache writes | corpus | confirmed — a cost model without multipliers is wrong by design |
| A tool-result-only scan misses ~26% of records and 70 MB of attachment context | corpus | confirmed |
| `tool_use`↔`tool_result` timestamps pair at 99.6% | corpus | confirmed |
| Per-tool **error rate** exposes defects a raw count hides (`Write` 12.4% vs `read_text` 0.0%) | corpus | confirmed |
| Multi-space padding costs 1 token per run; blank lines and trailing whitespace cost ~0 | tokenizer | confirmed / **refuted the intuition** |
| 19 of 20 abbreviations save exactly zero tokens | tokenizer | confirmed |
| Claude ≥4.7 tokenizes ~30% higher than earlier models; do not reuse old counts | primary source | confirmed verbatim |
| Claude Code caps a tool response at **25 000 tokens**; concise vs detailed measured 72 vs 206 | primary source | confirmed verbatim |
| Tool-use examples in the schema: **72% → 90%** on complex parameter handling | primary source | confirmed |
| Error messages should be engineered as steering, not logs | primary source | confirmed verbatim |
| API failures are dominated by rate limiting | corpus | **refuted here** — 503×198 against 429×6 |
| "Claude's tool selection degrades past 30–50 tools" | not found in the cited page | **UNVERIFIED — do not cite** |
| Compression that changes shape (TOON −18% at −9pp, TRON −27% at −14pp; LLMLingua 71% → 0% on code) | primary source | confirmed — caps how far M3 may go |
| **Tool Search Tool: ~85% fewer tokens on a 50+ tool surface** (≈77 K → ≈8.7 K, search tool ~500 tok); enabled by default in Claude Code | primary source (Anthropic, advanced tool use) | confirmed — the M3.4 deferral row is measured against this |
| **Code execution / programmatic tool calling: 150 K → 2 K tokens (98.7%) on a multi-server task**; independent reproductions 58%→92.8% as the surface grows | primary source | confirmed — but it needs a sandbox, and the trade is ~7% higher per-call latency |
| **Pass-by-reference tool results**: 82.2% SWE-bench Verified at 29 calls / 1.1 M tokens vs 66 calls / 2.2 M | primary source (harness study) | confirmed — the evidence behind mechanism 5 |
| **Prompting alone does not produce parallel tool calls**; reliable width came from injecting the request immediately before the call | primary source (W&D, ICLR 2026 workshop) | confirmed — the reason mechanism 2 outranks a `CLAUDE.md` line |
| **MCP-Bench grades parallelism**: top marks need <10% redundant calls and 90–100% of parallelisable work run in parallel | primary source | confirmed — gate 9's target |
| **Speculative tool execution (PASTE): −48.5% task completion time, 1.8× throughput** — but it *loses* when tool-idle is under ~5% | primary source (Microsoft Research, 2026) | confirmed, with the breakeven — section B prints the gap share that decides it |
| **Context rot**: accuracy degrades continuously with input length well before the limit | primary source | confirmed — trimming is an accuracy lever, not only a cost one |
| **Cache invalidation is asymmetric**: a broken prefix loses the ~90% read discount *and* re-pays the 25% write premium; tool-schema reordering is a common cause | primary source | confirmed — the `mid-session re-prime` counter exists for this |

Anything in that table is a premise, not a research task.

### M7.2 — Scope: search for methods, then test them against the measured corpus

- tool and schema design: naming, description length as a two-sided optimum, parameter count,
  enum-vs-free-text, embedded examples, error-message design, how selection accuracy degrades with
  surface size, deferred/progressive tool loading;
- context engineering: what belongs in a system prompt versus a tool response, just-in-time retrieval
  versus preloading, cache-stable prefixes, context rot, compaction economics, note-taking outside the
  window;
- **parallelism and orchestration**: what makes a model actually emit several `tool_use` blocks, how
  dependency is signalled in a schema, where over-parallelisation is penalised, batch-versus-speculate
  economics, and harness-level mechanisms (hooks, `updatedInput`, batch events);
- response format: structured versus prose, truncation signalling, confidence and provenance markers,
  refusal-to-guess as an accuracy device, concise/detailed response modes;
- verification: adversarial self-check, execution as arbiter, when a second opinion pays for itself,
  and the measured net-negativity of intrinsic self-correction without evidence;
- multi-agent economics: measured token multipliers of fan-out, when delegation is negative-value;
- **code-generation quality** (`[modern]`) and **performance-aware generation** (`[perf]`): measured
  rates of deprecated API use in LLM completions, and which intervention shape — schema wording, an
  in-response diagnostic, a worked example, an analyzer surfaced at the write — measurably changes it;
- benchmark evidence: token, latency and accuracy numbers from published evaluations of MCP servers
  and semantic code-navigation tools.

### M7.3 — Method: fan out once, then refute

1. Spawn **four to six** parallel research subagents **in one message**, one per scope cluster, each
   briefed to return claims with sources and dates. Give them **zero** transcript content — the topic
   only (gate 3). A subagent that cannot cite is returning an opinion. **Four to six, not nine:** a
   previous fan-out died mid-verify at 16 agents, 2.32 M tokens and 15 minutes, losing every Verify
   and Synthesize result while the Research results survived on disk, and `Agent` p99 in the corpus is
   **2 303 s**. Fewer, wider briefs land.
2. Prefer primary, dated sources: vendor engineering documentation, published evaluations, papers
   with numbers. Reject undated assertions and anything supported only by another agent's summary.
3. **Verify with the instruments of gate 5, in this order.** Re-derive it from the corpus if the
   corpus can see it; encode it if a tokenizer can settle it; otherwise fetch the primary source and
   **quote the sentence**. A claim attributed to a vendor document that is not in that document is
   `UNVERIFIED`, however plausible — that is exactly how the "30–50 tools" figure entered this file.
4. **Then try to refute**, against what M2–M6 measured here. Where they disagree, **the corpus wins**.
5. Discard anything already true of TerseSharp, and anything in the M7.1 table. The output of this
   phase is only the delta.
6. If the run is killed, recover from `<session>/subagents/workflows/<runId>/agent-*.jsonl` — the
   `StructuredOutput` tool input on each agent's last turn is the full result — rather than re-running
   the fan-out.

### M7.4 — Conversion

A research finding becomes a row only if it is actionable **here**. Name the concrete change: a tool
description reworded, a parameter added or removed, a response format changed, a `remedy:` that
teaches the retry, a `SKILL.md` section rewritten, a default flipped, a hook that fires at the call.
State the expected accuracy or productivity gain, **the instrument that verified it**, and how the
next run would *observe* it — a row whose success can only be felt is not accepted. A method that is
real but has no lever in this repo goes in the report's research section, not in the backlog.

## M8 — Deduplicate against what is already logged

`read_text paths=["IMPROVEMENTS.md", "IMPROVEMENTS-ARCHIVE.md"] headings=true` — **issued in M0's
launch message, not here** — then read `IMPROVEMENTS.md`'s `## Open` in full. The closed rows live in
`IMPROVEMENTS-ARCHIVE.md`, which is ~200 KB: read it with `columns="Finding,Outcome"` and open a full
row only when a candidate looks like a match. **Each file is exactly one table, one pointer line to
the other, and nothing else** — adding a heading fails `BacklogShapeTests`. A candidate that matches an
existing row is **not** a new row:

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

## M9 — Write the rows

1. Next id = highest existing `I<number>` + 1, continuing the sequence — never reuse one.
2. Append to `IMPROVEMENTS.md`'s `## Open` table with `edit_text` — a **new** row is never appended to
   the archive, which only ever receives the rows a `/ship-improvements` run closes; strengthening an
   existing closed row's `Outcome` (M8) is the one write the archive takes from this command —
   anchored on text read in this run, in the file's own **five**-column format — the fifth column is
   `Rejected` and a row missing it fails `BacklogShapeTests`, because GitHub Flavored Markdown pads a
   short row silently:

   `| **I<n>** <the finding> | <tool> | <proposed change> | <expected saving> | <approaches already refuted for this row, or —> |`

   - **Finding** — the goal tag first, then what was measured, in how many sessions, over which
     window. The tag is one of `[accuracy]` `[modern]` `[perf]` `[speed]` `[cost]`, written
     immediately after the bolded id — `| **I245** [accuracy] …` — so a later run can count coverage
     per goal without re-reading every row. Bold the headline number. One row, one line.
   - **Instrument** — a `[modern]` or `[perf]` row states inside its `Finding` cell which `analyze` /
     `search_regex` / `get_symbol_source` call confirmed the class on real source (gate 7). A row
     carrying only the regex count from section C is not written.
   - **Tool** — the terse-sharp tool, or the document (`SKILL.md`, `README.md`, `CLAUDE.md`,
     `.claude/commands/…`) or the hook when the lever is discoverability, teaching or workflow rather
     than capability. There is no separate workflow table; the `Tool` cell carries that distinction.
   - **Proposed change** — one concrete change. "Make it better" is not a proposal.
   - **Expected saving** — derived from the measurement, in calls and tokens, or in hours,
     milliseconds and megabytes for a `[speed]` row. Never both units in one cell. **A `[speed]` row
     also carries its gate-8 lever letter** — `(a)` delete, `(b)` parallelise, `(c)` shorten,
     `(d)` never call, `(e)` teach the harness — because a row that does not say which lever it pulls
     cannot be ranked against one that does.
   - **Rejected** — every approach this run already refuted for that row; `—` when there is none. A
     constraint that blocks the obvious fix belongs here (for example: the `Replaces Bash …` prefix
     cannot be shortened without un-enrolling the guard census).
3. Rank the way this repo ranks: **fixing a fallback outranks a new capability**; **improving an
   existing tool or response format outranks adding a tool**; **a lever that costs no server change at
   all — `(e)`, teaching the call site — outranks both when the capability already exists**. Within
   that, M6's mechanism order decides between two rows addressing the same chain. Highest measured
   cost first — **except that an `[accuracy]` row outranks a `[cost]` row of equal measured size**.
   Order within the file: `[accuracy]` and `[speed]` — co-primary, tie broken by measured hours —
   then `[perf]` and `[modern]` where a gate already exists to carry them, then `[cost]`. Within
   `[speed]`, `(a)`, `(b)`, `(d)` and `(e)` outrank `(c)` of equal size.
4. **No cap, and no silent drop — the floor and the aggregation rule are the discipline.**
   - A candidate clears the floor on its own if it is worth **≥200 tokens per session**, **≥1 call per
     session**, **≥500 ms per call**, or a named accuracy gain.
   - Everything below the floor is **aggregated by family** into one row — one `alignment padding
     across N tools` row, one `constant tags on success responses` row, one `tool-description wording`
     row — carrying the **summed** measurement and the list of tools it covers.
   - Anything genuinely dropped is named in the report with its measured cost and the reason.
5. Record the scan itself in the report — window, transcript count, total tool calls, built-in share,
   total tool-result tokens, trim-ledger tokens, measured wall time. **It does not go in the file:**
   prose in either backlog file fails the shape gate.

## M10 — Verify, commit, push

1. `read_text IMPROVEMENTS.md section="## Open"` — the table still parses, ids are unique and
   sequential across both files, and **every row has exactly five cells**. Then
   `read_text paths=["IMPROVEMENTS.md", "IMPROVEMENTS-ARCHIVE.md"] headings=true`: exactly two headings
   per file — `# Improvements backlog` / `## Open`, and `# Improvements archive` / `## Closed` —
   nothing else.
2. **Privacy re-read:** every new row, checked once more against gate 3. No path inside another repo,
   no type name, no prompt text, no quoted result, no boilerplate line the script suppressed. **Rows
   sourced from section C get a second look**, because they are the only rows sourced from prompt text
   and from code the agent wrote: a `[modern]` or `[perf]` row may name the construct class and the
   rule id, never a line of the emitted source; an `[accuracy]` row may name the cue label, the tool
   and the count, never a phrase from the prompt. This is the last chance before the file is public.
3. `changed_files` — `IMPROVEMENTS.md`, plus `IMPROVEMENTS-ARCHIVE.md` when M8 strengthened a closed
   row, must be the **only** paths this run touched. No build, no test run: nothing else changed.
4. `Bash: git add IMPROVEMENTS.md IMPROVEMENTS-ARCHIVE.md && git commit -m "Log I<n>–I<m> from the <N>-week session scan"`
   (body: the corpus line from M9.5). **No `Co-Authored-By`.**
5. `Bash: git show --stat HEAD` then `git push origin main`. No review, by standing instruction.

## M11 — Report

| Section | Content |
|---|---|
| **Speed headline** | **first, before anything else** — the four `[speed]` metrics: tool calls per task cycle, wall minutes per task cycle, round-trip latency (model gap p50 + tool p50), and the parallelism score (calls/message, multi share, unspent pairs, unused-batch calls, redundant share) — each against the previous run and against the 2026-08-26 baseline of 25.6 calls / 44.8 min / 6 097 ms / 1.165 calls-per-message / 8.3% redundant. Then the five levers with the hours each is worth this window. A report that opens with tokens has buried the half the user waits through |
| **This run's own cost** | the calls this command made, its wall minutes, and **its own calls per assistant message**. A run that scored below the corpus mean it just reported has failed gate 9 in the one session it fully controlled, and says so |
| Corpus | window, transcripts, records read **and the share carrying no `message`**, projects covered (slugs only), tool calls, built-in vs MCP share, tool-result tokens, tool-input chars, attachment tokens, spilled/sidecar bytes, thinking blocks and sealed share, tool wall time against turn wall time, seconds slept |
| Token ledger | per `message.model`: input, cache read, 1h and 5m writes, output, base-input-equivalent total, **cache hit rate and mid-session re-prime volume**. Never a dollar figure the corpus cannot prove |
| Positive control | section C's `lines > scanned` line, stated explicitly. A zeroed emitted-code table with a failing control is a **script defect**, reported as one, not as clean code |
| Top waste | the eight cost centres from sections A and B, each with its number |
| Sequence & parallelism | top chains with same-target share, same-tool runs with longest run, fan-out histogram, unspent-parallel pairs, unused-batch runs, re-fetch pairs |
| Composites & batches | every candidate from M6 with the mechanism it took (format / teach / parameter / list / aggregate-or-reference / new tool), the floor it cleared, and the break-even arithmetic for every one rejected |
| Attribution | tokens and records per skill, per MCP server, per subagent; the delegation ledger (count, self-reported tokens, wall time) |
| Friction | error **rate** per tool for tools with ≥50 calls, error-code histogram, API-error statuses, permission denials by kind, interruptions, queue enqueue/remove ratio, `max_tokens` truncations, compaction pre→post |
| Trim ledger | the full ledger in tokens per class and share of output — **and the placebo section stated as zero**, so the next run does not re-propose it |
| Surface cost | total `[Description]` bytes and `SKILL.md` bytes, what they cost per request, and the **distinct-tools-per-session** figure that prices deferral |
| Latency & memory | slowest tools by `p99` and by total hours, the verification tax (calls and hours per cycle, scoped vs whole split, identical re-runs under 5 min apart), the duplicate ledger in hours, bare-wait count and seconds, subagent p99 and max, RSS per workspace and per document, any cold-path trigger named |
| Research | the methods M7 found worth adopting, each with **the instrument that verified it** and the refutation attempt it survived; the ones dropped as already-true or not actionable here; an explicit `UNVERIFIED` list |
| Emitted code | legacy and slow-construct rates per 1000 emitted lines, top classes with session counts, which instrument (d) confirmed each, and the classes dropped as already-caught, out-of-scope for the allocation gate, or deliberately-off in `.editorconfig` |
| Interventions | interventions per session and as a share of prompts, the cue histogram, the tool immediately before each, and the shape each top entry was classified into |
| Traps & rework | the trap ledger with counts and re-paid input tokens, the edit rejection rate per tool, gates red after an edit, the same-target rework distribution |
| Goal ledger | one line per goal — `[accuracy]` `[modern]` `[perf]` `[speed]` `[cost]` — naming the rows it produced, or what was measured and why it came back clean. A missing line is a degraded run and says so |
| Productivity trend | the six ratios of M5.3 against the previous run's figures when one exists |
| New rows | every id written, with its one-line finding and expected saving, highest cost first |
| Strengthened | existing rows given a new measurement instead of a duplicate |
| Dropped | candidates measured but not logged, with their combined cost and the reason — never silent |
| Privacy | confirmation that no row names a path, type, prompt or result from another project |
| Commit | SHA and the paths staged |
| Trend | this run's `CORPUS` / `SPEEDLINE` / `EMITTED` lines against the previous run's |

If M1 found nothing, the report is just: window, roots, most recent transcript outside the window, and
the statement that nothing was changed.
