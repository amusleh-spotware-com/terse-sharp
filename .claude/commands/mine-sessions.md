---
description: Mine every Claude Code session across all projects for token, character, latency, memory and productivity waste down to the individual character, mine the call sequences for composite and batch tools that fuse a measured round trip into one call, deep-research the state of the art in agent accuracy, log every measured finding as an open row in IMPROVEMENTS.md, then commit and push.
argument-hint: "[weeks to scan, default 1]"
---

# 🚫 HARD GATE — findings are **measured to the character**, never impressionistic, and never leak another project's content.

`$ARGUMENTS` — a number of **weeks** to scan. Absent or unparseable → **1 week**. Nothing else takes
input from the user; do not ask, do not confirm the window.

**Four gates that outrank everything else in this command:**

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
   which is why M2 estimates at `chars // 4` — and why an estimate is all it is. Prefer trims that
   delete **whole tokens** (a separator, a padded column, a repeated line prefix, a whole field) over
   trims that shorten one: deleting characters from inside a word usually saves nothing at all,
   because a common word is already a single token.
5. **Every claim names the instrument that verified it.** Three are admissible, in this order:
   (a) **the corpus** — a count from the M2 scan over the real transcripts; (b) **a tokenizer
   experiment** — an encode-before/encode-after on a real payload, in both `o200k_base` and
   `cl100k_base`, labelled directional because neither is Claude's; (c) **a primary source** — a
   vendor document or a paper, fetched and quoted, never a summary of a summary. Where an outside
   claim contradicts the corpus, **the corpus wins and the row says so**. A claim that survived none
   of the three is written `UNVERIFIED` in the report and is not allowed into `IMPROVEMENTS.md` at
   all. This gate exists because a previous run of this command shipped a trim ledger whose three
   headline classes were each worth ~0.1%, sourced from plausible reasoning that nobody encoded.

**Also banned:** `AskUserQuestion`, `ExitPlanMode`, editing any file other than `IMPROVEMENTS.md`,
`git add -A`, a `Co-Authored-By:` trailer, and writing any script anywhere inside the repository.

**No review.** `code-review-gate`, `/code-review`, `caveman:cavecrew-reviewer` and every other review
path are explicitly waived for this command by standing user instruction. Do not spawn one and do not
report the phase as degraded.

**Subagents:** banned for every phase that touches a transcript (M2–M6) — a reviewer adds nothing to a
log scan and doubles the private-content exposure. **Permitted, and expected, in M7 only** (the
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
5. `TaskCreate` one task per phase, M1 through M11.

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

**Two shell traps this command has already paid for.** On Windows, bash's `/tmp` and the Windows
`%TEMP%` are the same directory but not the same *name*: write the file from bash with a POSIX path
and hand `python` the **Windows** path, or it answers `can't open file`. And a heredoc longer than
about 120 lines, or one containing a `'"` sequence, gets truncated mid-string and dies with
`unexpected EOF while looking for matching '`. Append the script in chunks and `ast.parse` it before
running it.

**The schema below was verified against 282 061 records on 2026-08-08**; every field named here was
counted, not assumed. The five things the previous version of this script got wrong are called out
inline, because each one silently under-reported.

```python
import ast, collections, datetime, json, os, re, sys

WEEKS = float(sys.argv[1]) if len(sys.argv) > 1 else 1.0
CUTOFF = datetime.datetime.now().timestamp() - WEEKS * 7 * 86400
QUOTE = chr(34)
BUILTIN = {'Read', 'Write', 'Edit', 'NotebookEdit', 'Grep', 'Glob', 'Bash',
           'WebFetch', 'WebSearch', 'Agent', 'Task', 'TodoWrite', 'Skill', 'ToolSearch'}
SHELL_TEXT = re.compile(r'\b(grep|rg|cat|head|tail|sed|awk|ls|find|type)\b')
SHELL_GIT = re.compile(r'\bgit\s+(status|diff)\b')
SHELL_DOTNET = re.compile(r'\bdotnet\s+(build|test|clean|format)\b')
SLEEP = re.compile(r'\bsleep\s+(\d+)')
ABSPATH = re.compile(r'[A-Za-z]:\\[\w.\-\\]+|/(?:Users|home|mnt|var|opt)/[\w.\-/]+')
PADRUN = re.compile(r'(?<=\S) {2,}')
INDENT = re.compile(r'^ {2,}')
ERRCODE = re.compile(r'\bERROR\s+([A-Za-z][A-Za-z0-9_]*)')
CONSTANT = ('(truncated=false', 'errors=0 warnings=0', 'EXACT', 'remedy:')

roots, seen = [], set()
for candidate in (os.path.expanduser('~/.claude/projects'),
                  os.path.join(os.environ.get('CLAUDE_CONFIG_DIR', ''), 'projects')):
    if candidate and os.path.isdir(candidate):
        real = os.path.realpath(candidate).lower()
        if real not in seen:
            seen.add(real)
            roots.append(candidate)

types = collections.Counter(); systems = collections.Counter()
calls = collections.Counter(); errors = collections.Counter(); error_codes = collections.Counter()
result_chars = collections.Counter(); input_chars = collections.Counter()
steers = collections.Counter(); structured_trunc = collections.Counter()
durations = collections.defaultdict(list); dupes = collections.Counter()
per_project = collections.defaultdict(collections.Counter)
bash_kind = collections.Counter(); attach = collections.Counter(); attach_n = collections.Counter()
attribution = collections.Counter(); attributed_tokens = collections.Counter()
denials = collections.Counter(); queue = collections.Counter(); stops = collections.Counter()
by_model = collections.defaultdict(collections.Counter)
trim = collections.Counter(); placebo = collections.Counter()
line_freq = collections.Counter(); line_projects = collections.defaultdict(set)
chains = collections.Counter(); same_target = collections.Counter(); refetch = collections.Counter()
runs = collections.Counter(); longest = collections.Counter(); parallel = collections.Counter()
payloads = []; turns = []; compactions = []; api_errors = collections.Counter()
sessions = set(); slept = interrupted = think_chars = think_n = think_sealed = 0
persisted_n = persisted_bytes = spill_files = spill_bytes = 0
delegations = 0; delegated_tokens = 0; delegated_ms = 0
mcp_structured = mcp_structured_chars = 0
records = with_message = 0

TARGET_KEYS = ('path', 'file', 'filePath', 'symbolId', 'symbol', 'query', 'pattern', 'name', 'command')

def target(arguments):
    for key in TARGET_KEYS:
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
    pending, seen_calls, order = {}, collections.Counter(), []
    for line in open(path, encoding='utf-8', errors='replace'):
        try:
            record = json.loads(line)
        except ValueError:
            continue
        records += 1
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
            systems[subtype] += 1
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
            split = stats.get('cache_creation')
            if isinstance(split, dict):
                for key, value in split.items():
                    if isinstance(value, (int, float)):
                        by_model[model][key] += value
            else:
                by_model[model]['cache_creation_input_tokens'] += stats.get('cache_creation_input_tokens') or 0
            for key in ('attributionSkill', 'attributionMcpServer', 'attributionAgent'):
                if record.get(key):
                    attributed_tokens[(key, record[key])] += (stats.get('output_tokens') or 0)
        blocks = message.get('content')
        if not isinstance(blocks, list):
            continue
        fanout = sum(1 for b in blocks if isinstance(b, dict) and b.get('type') == 'tool_use')
        if fanout:
            parallel[min(fanout, 8)] += 1
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
                tool = block.get('name') or 'none'
                arguments = block.get('input') or {}
                calls[tool] += 1
                per_project[project][tool] += 1
                pending[block.get('id')] = (tool, at)
                encoded = json.dumps(arguments, sort_keys=True)
                input_chars[tool] += len(encoded)
                seen_calls[(tool, encoded[:4000])] += 1
                order.append((tool, target(arguments)))
                if tool == 'Bash':
                    command = arguments.get('command', '') or ''
                    bash_kind['git status/diff' if SHELL_GIT.search(command) else
                              'dotnet build/test/format' if SHELL_DOTNET.search(command) else
                              'shell text tool' if SHELL_TEXT.search(command) else
                              'sleep/wait' if SLEEP.search(command) else 'other'] += 1
                    slept += sum(int(v) for v in SLEEP.findall(command))
            elif shape == 'tool_result':
                tool, started = pending.pop(block.get('tool_use_id'), ('none', None))
                content = block.get('content')
                text = content if isinstance(content, str) else json.dumps(content or '')
                result_chars[tool] += len(text)
                payloads.append((len(text), tool, project))
                audit(text, project)
                if started and at and at >= started:
                    durations[tool].append((at - started) * 1000)
                if block.get('is_error') or text.lstrip().startswith('ERROR '):
                    errors[tool] += 1
                    found = ERRCODE.search(text)
                    if found:
                        error_codes[found.group(1)] += 1
                if 'narrow with' in text or '(truncated=true' in text:
                    steers[tool] += 1
                    structured_trunc['tool steer'] += 1
    for (tool, _), count in seen_calls.items():
        if count > 1:
            dupes[tool] += count - 1
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
    touched = collections.defaultdict(set)
    for tool, spot in order:
        if spot:
            touched[spot].add(tool)
    for tools in touched.values():
        if 2 <= len(tools) <= 4:
            refetch[tuple(sorted(tools))] += 1

payloads.sort(reverse=True)
total_calls = sum(calls.values())
total_chars = sum(result_chars.values())
builtin = sum(c for t, c in calls.items() if t in BUILTIN)
mcp = sum(c for t, c in calls.items() if t.startswith('mcp__'))

def quantile(values, fraction):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * fraction))]

def show(label, value):
    print(f'{label:<32}{value}')

print(f'== window {WEEKS} week(s)  transcripts={len(sessions)}  roots={len(roots)}')
show('records', f'{records:,} ({with_message:,} carry a message, '
                f'{(records - with_message) * 100 // max(records, 1)}% do not)')
show('record types', dict(types.most_common(8)))
show('tool calls', f'{total_calls:,}  builtin={builtin * 100 // max(total_calls, 1)}%  '
                   f'mcp={mcp * 100 // max(total_calls, 1)}%')
show('tool-result chars', f'{total_chars:,} (~{total_chars // 4:,} tok)')
show('tool-input chars', f'{sum(input_chars.values()):,}')
show('spilled tool results', f'{persisted_n} records / {persisted_bytes:,} B  '
                            f'(sidecar dir: {spill_files} files / {spill_bytes:,} B)')
show('attachment context', f'{sum(attach_n.values()):,} records / {sum(attach.values()):,} B '
                           f'= {sum(attach.values()) * 100 // max(total_chars, 1)}% of tool-result volume')
show('thinking', f'{think_n:,} blocks, {think_sealed:,} sealed (empty text + signature); '
                 f'{think_chars:,} chars readable - the cost is already inside output_tokens')
show('mcp structuredContent', f'{mcp_structured:,} records / {mcp_structured_chars:,} chars')
show('measured tool wall time', f'{sum(sum(v) for v in durations.values()) / 3.6e6:.1f} h')
show('turn wall time', f'{sum(turns) / 3.6e6:.1f} h over {len(turns)} turns, '
                       f'p50={quantile(turns, 0.5) / 1000:.0f}s')
show('seconds slept in Bash', slept)

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
          f'in={input_chars[tool] // n:>5} ch/call  p50={quantile(spent, .5):>7.0f}ms  '
          f'p90={quantile(spent, .9):>8.0f}ms  p99={quantile(spent, .99):>9.0f}ms  '
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

print('\n== call chains: A immediately followed by B, one session, in order (composite candidates)')
for (first, second), count in chains.most_common(30):
    if count < 5:
        break
    bridge = result_chars[first] // max(calls[first], 1)
    print(f'  {count:>6}x  {first} -> {second}   same-target={same_target[(first, second)]:>5}  '
          f'intermediate={bridge // 4:>7,} tok/call  ~{count * bridge // 4:>9,} tok if fused')

print('\n== same-tool runs of >=3 consecutive calls (batch candidates)')
for tool, spent in runs.most_common(15):
    framing = input_chars[tool] // max(calls[tool], 1)
    print(f'  {tool:<40}{spent:>6} calls inside runs  longest={longest[tool]:>3}  '
          f'in={framing:>5} ch/call  ~{spent * framing // 4:>8,} tok of repeated argument framing')

print('\n== tool_use blocks per assistant message (parallelism the agent already achieved)')
print('  ' + '  '.join(f'{n}:{c}' for n, c in sorted(parallel.items())))

print('\n== one target touched by several tools in one session (re-fetch / fusion candidates)')
for tools, count in refetch.most_common(12):
    print(f'  {count:>6}x  {" + ".join(tools)}')

print('\n== 15 largest single tool results')
for size, tool, project in payloads[:15]:
    print(f'  {size:>9,} ch  {tool:<34}{project}')

print('\n== per project')
for project, counter in sorted(per_project.items(), key=lambda kv: -sum(kv[1].values())):
    top = ', '.join(f'{t}x{c}' for t, c in counter.most_common(6))
    print(f'  {sum(counter.values()):>6}  {str(project)[:44]:<46}{top}')

print(f'\n== corpus  weeks={WEEKS} transcripts={len(sessions)} records={records} calls={total_calls} '
      f'builtin={builtin * 100 // max(total_calls, 1)}% resulttok={total_chars // 4} '
      f'attachtok={sum(attach.values()) // 4} trimtok={sum(trim.values())} '
      f'chains={sum(chains.values())} runcalls={sum(runs.values())} '
      f'fanout1={parallel.get(1, 0)} fanout2+={sum(c for n, c in parallel.items() if n > 1)}')
```

Run it as `python <script> <WEEKS>`. Read the **whole** output. **Do not** re-derive any number by
hand afterwards — the script is the measurement of record, and the next run must be comparable.

**Five corrections this script encodes, each one a measured under-report in the version before it:**

1. **It reads every record class, not just the ones with a `message.content` list.** That guard alone
   dropped **73 915 of 282 061 records (26.2%)** — every `turn_duration` (348.0 h of wall clock),
   every `compact_boundary`, every `api_error`, every permission denial, and 70.0 MB of `attachment`
   context.
2. **It has a time axis.** Pairing `tool_use.id` to `tool_result.tool_use_id` and subtracting the two
   records' `timestamp` values resolves **99.6%** of calls (73 186 of 73 448) and yields
   p50 367 ms / p90 12 437 ms / p99 182 810 ms over 190.6 h. Without it the whole *Speed* finding
   class in M5 is unmeasurable.
3. **It keys tokens by `message.model` and splits `cache_creation` five ways.** The corpus spans
   **8 models**, and cache writes are **97.1% one-hour** (797 152 487 vs 24 013 041), which bills at
   2.0× base input against 1.25× for the five-minute tier — while cache *reads* are **98.8%** of all
   input-side volume (33.30 B vs 387.9 M). Summing four counters unweighted is not a cost model. The
   script reports a **base-input-equivalent** total rather than dollars, because a price it cannot
   prove is exactly the confident wrong answer this repo bans.
4. **It counts the payload that leaves the transcript.** `toolUseResult.persistedOutputSize` (52
   records, 13.1 MB), the `tool-results/` sidecar directory (257 files, 71.7 MB), 70.0 MB of
   attachments — `skill_listing` alone is **39.0 MB** — and 4 685 `mcpMeta.structuredContent`
   payloads. `len(tool_result.content)` sees none of it.
   **Do not price `thinking` from its text.** Recent transcripts persist a *sealed* block: `thinking`
   is the empty string and only `signature` survives (475 of 475 in a spot-checked window), so a
   character count silently reports zero and an older corpus reports a number that is not comparable.
   Thinking bills as output and is already inside `output_tokens` — count the blocks, report how many
   are sealed, and take the cost from the token ledger.
5. **It divides errors by calls and attributes cost to a skill, a server and an agent.** A raw error
   count ranks the busiest tool first; a *rate* found the real defects — `Write` **12.4%**
   (397/3 198) against `read_text` **0.0%** (0/2 766), and inside TerseSharp itself `find_usages`
   **7.7%** (17/222) and `find_files` **7.6%** (16/211), two tools whose arguments an agent evidently
   guesses wrong. `attributionSkill` (50 094 records), `attributionMcpServer` (83 931) and
   `attributionAgent` (37 797) turn a tool histogram into the per-skill breakdown `/usage` shows.
6. **It has a sequence axis, which is the whole input to M6.** A histogram cannot see a composite or a
   batch: both are properties of *call order inside one session*. The script keeps the ordered
   `(tool, target)` list per transcript and reports four things — **chains** (A immediately followed by
   B, with the share where both name the same target and the intermediate payload a fused call would
   never return), **runs** (≥3 consecutive calls of the same tool, which is the batch signature),
   **fan-out** (`tool_use` blocks per assistant message — the parallelism the agent already achieved
   unaided, which a batch parameter has to beat rather than duplicate), and **re-fetch** (one path or
   symbol touched by several different tools in one session). `target` is the first present of
   `path`/`file`/`filePath`/`symbolId`/`symbol`/`query`/`pattern`/`name`/`command`, used only for
   equality and **never printed** — gate 3 holds here exactly as everywhere else.

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
weeks that section is a floor, not a total — say so in the report.

## M3 — The micro-trim pass: every character in TerseSharp's own responses

M2's trim ledger estimates. This phase **measures with a tokenizer**, because characters are what a
counter can see and tokens are what is paid, and the two disagree so violently that three of the four
"obvious" trims are worth nothing at all.

### M3.1 — Run the tokenizer experiment first, every time

`pip install tiktoken`, then measure the **real** widest response of each `mcp__terse-sharp__*` tool
in the corpus — take them from M2's largest-payload list, or from a live call. Never reason about
tokens from character counts.

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
> character-to-token ratio is **4.18**, which is what makes M2's `chars // 4` a safe estimator and
> nothing more.

### M3.2 — The measured verdict table

Measured 2026-08-08 on `o200k_base`, cross-checked on `cl100k_base`, over 20-row synthetic tables
with **varied** rows (identical rows let BPE merge across lines and understate every separator cost)
and over the 15 real captured TerseSharp responses in `.research/samples-*.txt`, 13 591 tokens total.

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
| **Abbreviation** (`errors`→`err`, `configuration`→`config`, 20 pairs) | **19 of 20 save exactly zero**; `changed`→`chg` **costs +1** | **Banned as a proposal.** A common word is already one token |
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
| **Constant tags** | `EXACT` on a tool that can only answer `EXACT`, `(truncated=false)`, `errors=0 warnings=0` on a tool that fails loudly, `remedy:` on a success | straight from M2's ledger |
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
   sum the returned line lengths. That is the floor of what the 86-tool advertised surface costs on
   **every request**. Multiply by the assistant-turn count from M2 to price it.
2. Flag every description that repeats the tool's own name, explains *how* rather than what it
   returns and which built-in it replaces, restates parameters the schema already declares, or spends
   words on a case the `remedy:` already teaches. **But do not confuse this with "shorter is better"**
   — Anthropic's guidance is to write *detailed* tool descriptions, and the measured accuracy lever
   points the other way: embedded tool-use examples took complex parameter handling from **72% to
   90%**. Cut redundancy, not information; a worked example that prevents one malformed call has paid
   for itself several times over.
3. `read_text src/TerseSharp.Server/Assets/SKILL.md headings=true`, then price each section by byte
   size. The skill is loaded whole into an agent's context.
4. **Never propose deleting a `Replaces Bash …` prefix.** `ToolCensusE2ETests` enrols the guard from
   it, so cutting it silently un-enrols the tool. Record that constraint in the row's `Rejected` cell.

### M3.5 — The counter-evidence, which caps how far this phase may go

Compression that changes *semantics* buys tokens with accuracy, and this repo would rather pay the
tokens:

- Token-optimised notations measured across four agentic benchmarks: **TOON −18% tokens at a 9pp
  accuracy cost**, TRON −27% at up to 14pp. Both are far worse trades than the 26–61% available from
  minification, relative paths and de-framing **at zero semantic change**.
- Perplexity-based pruning (LLMLingua) reports 20× on prose, but **71.1% reduction on code produced
  0% QA accuracy**. Anything that can delete a character inside a path, an error code or an
  identifier is unsafe for tool output.
- Header hoisting is not free either: repeated per-row keys let a model attend to each row
  independently, and JSON has been measured to *beat* markdown on per-row lookup despite costing more
  tokens. Prefer it for uniform bulk records; do not apply it to a payload the agent looks things up
  in.
- The reason to trim anyway: fewer tokens is itself an accuracy win. Performance degrades
  continuously with input length well before the context limit, and a single distractor record
  measurably lowers accuracy — so framing removed is haystack removed, not just cost removed.

So the ranking for this phase: **de-frame → relativise paths → collapse multi-space runs → minify →
only then consider changing the shape of the data.**

## M4 — The performance and memory pass

Tokens are half the prime directive; the other half is speed, and a server that answers cheaply but
slowly loses the session anyway.

**Latency, from M2's `p50`/`p90`/`p99`/`tot` columns.** The corpus-wide baseline, measured over
73 186 paired calls on 2026-08-08, is **p50 367 ms · p90 12 437 ms · p99 182 810 ms**, 190.6 h of tool
wall time against 348.0 h of turn wall time — so tools are roughly **55% of everything the agent
waits for**. Judge every tool against that distribution, not against a feeling. For every
`mcp__terse-sharp__*` tool in the top 25:

- a `p90` above the corpus `p90` on a *read* tool is a defect: a read is supposed to be the cheap end.
- a `p99` more than 5× its `p50` is a **cold path** — first call after a load, an eviction, an
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
| **Accuracy** | an answer the agent acted on and later had to undo — a confident wrong result, a stale read, a claim the tool could not prove. This class is scored in M7, because a wrong answer costs more than any payload |

---

## M6 — Composite, batch and new-tool synthesis

M2's sequence axis and M5's round-trip evidence say where the agent spent **two calls on one answer**.
This phase turns that into candidate changes, and it is the only place in this command where the
question *"should there be a new tool?"* may be asked. Asked anywhere else it produces an 87th tool
nobody calls, paid for in every request of every session.

**Four mechanisms, ranked. Take the first one that covers the measured chain:**

1. **Make the first response carry what the second was called for.** A format change: the id the
   caller had to re-derive, the count it re-queried, the declaration it looked up afterwards. Costs no
   surface, no parameter, no schema token, and deletes the whole second call. It outranks everything
   below it.
2. **Add a parameter to the existing tool that fuses the pair** — `usages=true`, `source=true`,
   `context=N`. One schema line against one deleted round trip. The default stays **off**, or every
   caller pays the fused payload for the case it did not want; propose a default only with the share
   from M6.1.
3. **Make the existing tool take a list where it takes one** — the batch case, derived from the runs
   histogram, never from a feeling.
4. **A new tool.** Only when 1–3 cannot express it, and only against the surface cost priced in M3.4:
   a new tool's name and `[Description]` bytes are paid on every request whether it is called or not,
   and that product is the denominator the saving has to beat.

### M6.1 — The composite floor

A chain `A -> B` is a candidate only when **all four** hold, each with its number from M2:

- **frequency** — ≥1 occurrence per session on average, or ≥20 in the window;
- **adjacency** — B is A's *immediate* successor, which is all the chains counter counts, so a fused
  call would genuinely have removed a turn rather than reordered one;
- **same target** — `same-target` is ≥50% of the pair's count. A pair that mostly walks on to a
  *different* file or symbol is a workflow, not a composite;
- **the intermediate is discarded** — read two occurrences in the transcript (M5) and confirm A's
  payload was used only to derive B's arguments. Where A's payload was itself an answer, fusing saves
  the round trip but **not** the tokens; the row says which of the two it claims.

The `intermediate=` column is A's **mean payload over all its calls**, not over this pair's calls —
an estimate, and it is labelled one. A row that turns it into a headline number re-measures it on the
pair's own occurrences first.

**The counter-case that caps this phase, and it is already refuted: a composite whose second half
fires in fewer than half the cases makes the common case more expensive.** Break-even is
`P(second call) × cost(second call) > cost added to every first call`. Compute it. A candidate that
does not clear it goes in the row's `Rejected` cell, not into the backlog.

### M6.2 — The batch floor

A run of ≥3 consecutive calls of one tool is a batch candidate only when:

- it appears in **≥3 different sessions** — one session's loop is a task shape, not a tool defect;
- the deleted per-call cost is measured: the argument framing (`in=… ch/call` in the runs section)
  plus the per-response framing, times the calls inside runs;
- **the fan-out histogram is read first, and it cuts both ways.** Where the agent already issues
  several `tool_use` blocks in one assistant message, those calls are parallel and a batch parameter
  only wins if the *responses* share framing that could be emitted once. Where the histogram is
  overwhelmingly `1:` — the 0.08-week probe on 2026-08-08 measured **1:2984 and nothing above it** —
  the finding is the opposite one and it is bigger: the agent is issuing a run of dependent-looking
  serial calls it never had to serialise. That is a `SKILL.md`/description row (mechanism 1) before it
  is a batch row, because no server change is needed to collect it.

Two constraints every batch proposal states, or it is not implementable here:

- **it answers per item.** A batch that fails whole because item 7 was not found is worse than N
  calls, because the agent cannot tell which item failed: per-item status, per-item error code.
- **it scopes per item.** A batch whose items span workspaces or projects resolves each one — a helper
  that re-derives scope from the raw path answers wrongly and silently, which is a shipped trap in
  this repo, not a hypothetical.

### M6.3 — The re-fetch, duplicate and steer signals

- **re-fetch** (one target, several tools, one session) names the fusion nobody asked for — the same
  file outlined, read and diffed. Rank by count; the row is almost always mechanism 1.
- **`dup`** in the M2 tool table is the same call twice with identical arguments. That is never a
  composite: it is an **idempotence or a caching** row, or evidence the first answer was not trusted.
  Say which.
- **`steer`** followed by the same tool with a wider cap is a **default** row, not a new tool — the cap
  is wrong for that tool's real payload. Quantify the overflow, not the call.

### M6.4 — Conversion

Every candidate leaves this phase as a row whose `Tool` cell names **the existing tool and the
parameter**, unless mechanism 4 won on the arithmetic — in which case the row states the surface cost
it beat and the chain it deletes. `Expected saving` is `calls per session × tokens per call`, both
from M2, never an estimate of how nice the tool would be. Anything that cleared no floor is named in
the report's Dropped list with its measured count, never silently.

---

## M7 — Deep research: what the field knows about agent accuracy and productivity

The corpus says what *this* agent wasted. It cannot say what a better-designed tool surface would
have avoided in the first place. This phase is **mandatory**, it is not a literature review for its
own sake, and it ends in rows.

### M7.1 — Start from the standing corpus of already-verified findings

A previous run of this command banked a research fan-out — 206 techniques across eight areas, each
with `name / what / howToMeasure / expectedEffect / source`. Twenty-five of its claims have since
been checked against the real transcripts, a tokenizer and the primary documents. **Do not re-derive
what is already settled.** What is settled, with the instrument that settled it:

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

Anything in that table is a premise, not a research task. Everything else in the banked set — and
anything new — goes through M7.3.

### M7.2 — Scope: search for methods, then test them against the measured corpus

- tool and schema design: naming, description length as a two-sided optimum, parameter count,
  enum-vs-free-text, embedded examples, error-message design, how selection accuracy degrades with
  surface size, deferred/progressive tool loading;
- context engineering: what belongs in a system prompt versus a tool response, just-in-time retrieval
  versus preloading, cache-stable prefixes, context rot and the accuracy curve against length,
  compaction economics, note-taking outside the window;
- response format: structured versus prose, truncation signalling, confidence and provenance markers,
  refusal-to-guess as an accuracy device, concise/detailed response modes;
- verification: adversarial self-check, execution as arbiter, judge panels, when a second opinion
  pays for itself, and the measured net-negativity of intrinsic self-correction without evidence;
- multi-agent economics: measured token multipliers of fan-out, when delegation is negative-value;
- harness levers: permission allowlists, `PreToolUse` guards and `updatedInput`, per-subagent model
  choice, tool-surface pruning, instruction-file size against instruction-following accuracy;
- benchmark evidence: token, latency and accuracy numbers from published evaluations of MCP servers
  and semantic code-navigation tools.

### M7.3 — Method: fan out, then refute

1. Spawn **at least eight** parallel research subagents, one per scope area, each briefed to return
   claims with sources and dates. Give them **zero** transcript content — the topic only (gate 3).
   A subagent that cannot cite is returning an opinion.
2. Prefer primary, dated sources: vendor engineering documentation, published evaluations, papers
   with numbers. Reject undated assertions and anything supported only by another agent's summary.
3. **Verify with the instruments of gate 5, in this order.** Re-derive it from the corpus if the
   corpus can see it; encode it if a tokenizer can settle it; otherwise fetch the primary source and
   **quote the sentence**. A claim attributed to a vendor document that is not in that document is
   `UNVERIFIED`, however plausible — that is exactly how the "30–50 tools" figure entered this file.
4. **Then try to refute.** A second agent whose instruction is to break the claim, plus a check
   against what M2–M6 measured here. Where they disagree, **the corpus wins**.
5. Discard anything already true of TerseSharp, and anything in the M7.1 table. The output of this
   phase is only the delta.

**A fan-out this size will hit a session limit.** The banked run died mid-verify at 16 agents,
2.32 M tokens and 15 minutes, losing every Verify, Audit and Synthesize result while all eight
Research results survived on disk. So: **run Research first and let it land**, keep the phases
separate, and if the run is killed, recover from
`<session>/subagents/workflows/<runId>/agent-*.jsonl` — the `StructuredOutput` tool input on each
agent's last turn is the full result — rather than re-running the fan-out.

### M7.4 — Conversion

A research finding becomes a row only if it is actionable **here**. Name the concrete change: a tool
description reworded, a parameter added or removed, a response format changed, a `remedy:` that
teaches the retry, a `SKILL.md` section rewritten, a default flipped. State the expected accuracy or
productivity gain, **the instrument that verified it**, and how the next run would *observe* it — a
row whose success can only be felt is not accepted. A method that is real but has no lever in this
repo goes in the report's research section, not in the backlog.

## M8 — Deduplicate against what is already logged

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

## M9 — Write the rows

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
   existing tool or response format outranks adding a tool**; within that, M6's mechanism order —
   format change, then a fusing parameter, then a list parameter, then a new tool — decides between
   two rows that address the same chain. A saving that cannot be measured is not accepted. Highest
   measured cost first.
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

## M10 — Verify, commit, push

1. `read_text IMPROVEMENTS.md section="## Open"` — the table still parses, ids are unique and
   sequential, and **every row has exactly five cells**. Then `read_text … headings=true`: exactly
   three headings, `# Improvements backlog` / `## Open` / `## Closed`, in that order, nothing else.
2. **Privacy re-read:** every new row, checked once more against gate 3. No path inside another repo,
   no type name, no prompt text, no quoted result, no boilerplate line the script suppressed. This is
   the last chance before the file is public.
3. `changed_files` — `IMPROVEMENTS.md` must be the **only** path this run touched. No build, no test
   run: nothing else changed, and `IMPROVEMENTS.md` is not shipped in the package.
4. `Bash: git add IMPROVEMENTS.md && git commit -m "Log I<n>–I<m> from the <N>-week session scan"`
   (body: the corpus line from M9.5). **No `Co-Authored-By`.**
5. `Bash: git show --stat HEAD` then `git push origin main`. No review, by standing instruction.

---

## M11 — Report

| Section | Content |
|---|---|
| Corpus | window in weeks, transcripts scanned, records read **and the share carrying no `message`**, projects covered (slugs only), total tool calls, built-in vs MCP share, tool-result tokens, tool-input chars, attachment tokens, spilled/sidecar bytes, thinking tokens, tool wall time against turn wall time, seconds slept |
| Token ledger | per `message.model`: input, cache read, 1h and 5m writes, output, and the base-input-equivalent total. Never a dollar figure the corpus cannot prove |
| Top waste | the eight cost centres from M2–M4, each with its number |
| Sequence | the top call chains with their same-target share, the same-tool runs with their longest run, the fan-out histogram, and the re-fetch pairs — the raw input to M6 |
| Composites & batches | every candidate from M6 with the mechanism it took (format / parameter / list / new tool), the floor it cleared, and the break-even arithmetic for every one that was rejected |
| Attribution | tokens and records per skill, per MCP server, per subagent; the delegation ledger from `toolStats` (count, self-reported tokens, wall time) |
| Friction | error **rate** per tool for tools with ≥50 calls, error-code histogram, API-error statuses, permission denials by kind, interruptions, queue enqueue/remove ratio, `max_tokens` truncations, compaction pre→post |
| Trim ledger | the full M2 ledger in tokens per class and share of output — **and the placebo section stated as zero**, so the next run does not re-propose it |
| Surface cost | total `[Description]` bytes and `SKILL.md` bytes, and what they cost per request |
| Latency & memory | slowest tools by `p95` and by total minutes, RSS per workspace and per document, any cold-path trigger named |
| Research | the methods M7 found worth adopting, each with **the instrument that verified it** (corpus / tokenizer / primary source) and the refutation attempt it survived; the ones dropped as already-true or not actionable here; and an explicit `UNVERIFIED` list of every claim that survived no instrument |
| New rows | every id written, with its one-line finding and expected saving, highest cost first |
| Strengthened | existing rows given a new measurement instead of a duplicate |
| Dropped | candidates measured but not logged, with their combined cost and the reason — never silent |
| Privacy | confirmation that no row names a path, type, prompt or result from another project |
| Commit | SHA and the single path staged |
| Trend | this run's corpus line against the previous run's, when one exists |

If M1 found nothing, the report is just: window, roots, most recent transcript outside the window, and
the statement that nothing was changed.
