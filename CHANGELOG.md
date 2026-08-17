# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

## [0.40.0] - 2026-08-17

**Response format changed.** `format` — and every mode of `cleanup` that reformats — now collapses a
run of two or more blank lines between members down to one, so a file that `add_member` left with
double blanks is rewritten by the tool that owns whitespace. `format verify=true` therefore names
files it used to accept; that is still the wider sweep, not the CI gate, and
`cleanup verify=true fix=style` / `fix=analyzers` are unchanged and stay byte-equivalent to CI.

### Added

- **`run_tests` names the test that was still running when a run was stopped.** Every run now passes
  `--blame-hang-timeout` at 80 % of `timeoutSeconds` with `--blame-hang-dump-type none`, so VSTest's
  blame collector writes its sequence file before the process tree is killed. A run that produces no
  results ends with `WARNING the run was stopped while these test(s) were still running: <names>`
  instead of only `no test results were produced` — the bisection that used to cost six `run_tests`
  calls and about two hours of wall clock is now one call. Covered by `HangSequenceTests` and by
  `RunTests_WhenATestNeverFinishes_NamesThatTestInsteadOfAnsweringNothing` against the new
  `fixtures/HangSolution`. Closes **I286**.
- **`run_tests runSettings=` passes VSTest RunSettings overrides through as one trailing
  `-- Name=Value` block.** That is the layer governing parallelism *inside* one assembly —
  `xUnit.MaxParallelThreads`, `xUnit.StopOnFail`, `MSTest.Parallelize.Workers`,
  `NUnit.NumberOfTestWorkers` — which `parallel` deliberately does not touch. An entry that is not
  `Name=Value` is refused before anything runs, exactly as `properties` is. Covered by
  `RunTests_WithRunSettings_ReachesVSTestAndBoundsTheRunInsideTheAssembly`. Closes **I291**.
- **`history tags=true` lists the repository's tags**, newest version first, one line per tag with the
  short sha it names and its date, bounded by `maxResults`. It is refused beside `baseRef=`, `path=`,
  `contains=`, `message=` or `commit=` rather than ignoring them, and `ToolGuard` now denies a
  `git tag` **listing** (`git tag`, `git tag --list`, `git tag -l "v*"`) while leaving every tag
  mutation — create, annotate, delete, push — on the shell. Covered by
  `History_WithTags_ListsTheRepositoryTagsNewestFirstWithTheCommitEachNames` and
  `Guard_ForAGitTagListing_DeniesItAndNamesHistoryTags`. Closes **I288**.
- **`edit_text row="I286" toPath=…` moves one markdown table row.** The row is matched by its **first
  cell**, cut from the source table and appended to the last table of the target, and `newText=`
  beside it is what lands there — so closing a backlog row costs its identifier plus the rewritten
  row, not the row's full text twice. An identifier matching no row, or more than one, is refused
  saying which; `row=` without `toPath=` is refused rather than silently dropped. Covered by
  `EditText_WithRowAndToPath_MovesThatRowAndRewritesItWithoutSendingItsOldText`. Closes **I289**.

### Changed

- **`replace_symbol` warns when the replacement carries none of the attributes it replaced.** A
  declaration sent without its `[McpServerTool]` silently un-advertised a tool while the build,
  `analyze` and `get_diagnostics` all stayed clean; the edit still applies — dropping an attribute is
  sometimes the intent — but the response now ends `WARNING attributes dropped: McpServerTool,
  Description`. Covered by
  `ReplaceSymbol_WhenTheReplacementCarriesNoneOfTheAttributesItReplaced_NamesThemInsteadOfUnwiringSilently`.
  Closes **I287**.
- **`run_tests projects=` refuses the same project twice.** Two entries resolving to one `.csproj`
  answered a merged verdict in which six tests failed purely from two invocations racing the same
  assembly. The batch is now refused with `ERROR InvalidArgument` naming the duplicate. Covered by
  `RunTests_WithTheSameProjectTwiceInABatch_IsRefusedInsteadOfRacingTheAssemblyAgainstItself`.
  Closes **I292**.
- **A locked-build `holder pid=` line now carries the executable the process is running**, relative to
  the workspace root when it lives inside it — which is what tells a test host running out of this
  tree's own `bin/` from another session's. Covered by
  `LockHolders_ForThisProcess_NamesTheExecutableItRunsSoTheCommandLineNeedsNoShellOut`.
  Closes **I290**.
- **`format` collapses a run of blank lines between members.** The fold is syntax-aware — it edits
  trivia, never token text — so a raw string literal cannot be rewritten by it. Covered by
  `Format_WithARunOfBlankLinesBetweenMembers_CollapsesThemInsteadOfLeavingThemToTheShell`.
  Closes **I293**.
- **Two token budgets were raised to their measured values, and to nothing more.** The three new
  parameters cost the advertised surface **301 tokens** on the narrowed 57-tool profile
  (`MarkupProfileE2ETests`, 20 379 → 20 705) and `SKILL.md` **719 tokens** (`DocsCoverageE2ETests`,
  21 770 → 22 489), after the descriptions and the skill prose were trimmed by 155 and 120 tokens
  respectively. Against this repo's own break-even — one advertised tool is ~255 tokens and pays for
  itself at 32 calls per 508 sessions — three parameters that each remove a measured shell fallback
  clear it. The full-surface budget (25 900) was **not** raised: it absorbed the change.
- **Review round fixes, all found by the fresh-context reviewer before the push.** `history tags=true`
  dereferences an **annotated** tag to the commit it names (`%(*objectname:short)` with a lightweight
  fallback) — it was printing the tag object's sha, which matched nothing `history` lists, on the very
  release flow the row was written for. The blame window is now `timeoutSeconds - 15s` and is passed
  only when that leaves a margin (above 30 s), instead of a flat 80 % measured against a different
  clock than VSTest's per-test timer; `timeoutSeconds` says so. A serial `parallel=1` batch stops when
  a project produces no results, not only when the tool's own deadline elapses — blame can now end a
  hung project *before* that deadline, which would have let the rest of a ten-project batch run on.
  `replace_symbol` compares attribute **sets**, so dropping one of two attributes is named where only
  dropping all of them was before — which is the original `[McpServerTool]` incident minus one
  attribute. The `exe=` path uses `PathBoundary.Contains`, so `C:\repo` no longer swallows
  `C:\repoEvil` and Linux stays case-sensitive.
- **A project that produced no results counts as unfinished, whatever stopped it.** `Unfinished` keyed
  on the tool's own deadline, so once blame could end a hung project *before* that deadline the batch
  under-counted: `1 of 2 project(s)` where two produced nothing. It now also asks whether the
  project's results slot holds a `.trx` **and no blame sequence file** — a blame abort writes a
  *partial* `.trx`, so the presence of results alone reported a hung project as finished and let the
  rest of a serial batch run on. Caught by CI on **macos and ubuntu only** — windows passed because
  the tool's own tree-kill won the race there, which is exactly the platform split this repo warns
  about, and it took two CI rounds because the first fix keyed on the wrong artifact.
  Covered by `Unfinished_UnderAConcurrentBatch_NamesOnlyTheProjectsThatTimedOut` and
  `Unfinished_UnderASerialBatchThatStopped_NamesTheTimedOutProjectAndEveryProjectItNeverStarted`,
  both now driven by real results slots rather than run objects alone.





## [0.39.0] - 2026-08-17

**Response format changed.** A `run_tests projects=` batch no longer stops at the first timeout by
default, so the `WARNING the batch stopped at the first timeout; M of N project(s) produced no results`
line is now emitted only under `parallel=1`. A concurrent batch answers
`WARNING M of N project(s) timed out; the rest of the batch still ran`, and a batch whose shared build
fails or times out answers the build's diagnostics followed by
`WARNING the batch build of <project> timed out, so no project ran`, and a batch where **every**
project timed out says so rather than claiming the rest still ran. `elapsedMs` on a successful
concurrent batch is now its **wall clock** plus the builds that preceded it, rather than the sum of
its test processes.

### Added

- **`run_tests` runs a `projects=` batch concurrently, and takes `parallel` to govern it.** `parallel`
  defaults to `0` = one test process per core — the same scheduling an IDE test runner uses — is
  bounded by the number of projects in the batch, and answers `ERROR InvalidArgument` with a remedy
  outside `0`-`10`, the batch cap. Every project of a batch that runs concurrently is built up front,
  one build per project and scoped to exactly the projects that will run, and each is then executed
  with `--no-build`, because N projects that each rebuild the shared graph at the same time is the
  MSBuild contention this repo already tracks as its own build flake. Scoping the build to the batch
  is what keeps `changed=true` selective and keeps `targetFramework` off projects that never declared
  it. `parallel=1` restores the previous serial run, including its stop-at-the-first-timeout
  behaviour, and is the only mode that leaves later projects unrun. **A run with a single project
  ignores `parallel` entirely and is byte-for-byte unchanged**, which is every `run_tests` call that
  does not pass `projects=` or reach a multi-project `changed=true` selection.
  `TestRunRequestTests` covers the degree and the serial/concurrent mode it resolves to — including a
  host whose core count resolves the degree to 1, where the mode must stay concurrent — and
  `DotnetRunnerTests` covers the timeout wording and which projects a batch names as unfinished in
  each mode.
  `ChangedTestSelectionE2ETests.RunTests_WithAConcurrentBatchOfTwoProjects_MergesBothIntoTheVerdictParallelOneReaches`
  asserts the two modes reach the **same** verdict once the timings are stripped, against
  `fixtures/SelectionSolution`, whose two independent test projects are what makes a genuinely
  concurrent batch observable.

### Changed

- **Each project of a batch writes its `.trx` into its own subdirectory** of the run's results
  directory, and `TERSE_RESULTS_DIRECTORY` points at that per-project subdirectory. `dotnet test` names
  a `.trx` after the user, machine and a second-granularity timestamp, so two concurrent projects could
  otherwise collide on the filename. **A single-project run still gets the results directory itself**,
  so the variable an external suite reads is unchanged there. The parser already globbed recursively,
  so nothing downstream changed;
  `BacklogClosureE2ETests.RunTests_WithParallelOne_StopsTheBatchAtTheFirstTimeoutAndNamesWhatProducedNoResults`
  is the rewritten form of the test that pinned the old serial contract. A batch build that fails
  reports only that build's own `elapsedMs`; the time its predecessors spent is not folded in.
- **`timeoutSeconds` also bounds each project's pre-fan-out build**, and a build that exceeds it names
  the project rather than surfacing as an unattributed failure. The remedy it offers depends on why it
  failed: `noBuild=true` is never suggested after a compile error, because running the previous
  binaries would answer `run_tests PASSED` for code that does not compile —
  `ChangedTestSelectionE2ETests.RunTests_WhenABatchsOwnBuildCannotFinish_NamesTheProjectAndNeverOffersNoBuild`.

## [0.38.0] - 2026-08-16

**Response formats changed.** Every position `analyze`, `get_diagnostics` and `gate` print now carries the
addressable id of the declaration containing it - `OrderService.cs:15:16 OrderService.Unused: …` -
so a record line has one more token before its `: message`. `workspace_status` gains a
`WARNING workspace=diverged` line when a document's in-memory text no longer matches disk, and a
`disk=in-sync` line plus an advertised-cost breakdown under `verbose=true`. A capped `changed_files`
summary counts **files** on both halves rather than rows against files. A failure carrying an
MSBuild build-host RPC exception is answered as the new `ERROR Transient` instead of `ERROR Internal`.
`edit_text`'s `path` is no longer required. Under SemVer these are breaking changes expressed, per
major-version-zero, as a MINOR bump.

### Added

- **`edit_text section="## Open" toPath=other.md` moves a markdown section between files in one
  write.** Splitting the 205 KB backlog cost a 13 KB `read_text section=` plus a 9 KB `write_text`
  that re-sent text the server already held - ~5 500 tokens for a move that changes no content. The
  section is now cut from `path` and landed in `toPath` as one write, answered as one line per changed
  file; `place=prepend` puts it at the top of the target, anything else appends it. `occurrence=`
  picks the source section when the heading repeats, both paths must already exist and be markdown,
  naming the same file twice is refused rather than duplicating the section, and `toPath` cannot be
  combined with `oldText`, `newText` or `edits`. Locked by
  `EditText_WithToPath_MovesTheSectionIntoTheOtherFileInOneWrite` and
  `EditText_WithToPathNamingTheSameFile_IsRefusedRatherThanDuplicatingTheSection`. (I285)

- **`edit_text` no longer needs a top-level `path` when every batched entry declares its own.** A
  cross-file batch where each entry carried its own `path` was refused with `missing path`, so the
  whole payload - ~1 200 tokens on the call that produced this row - had to be re-sent with an
  arbitrary top-level `path` picked from one of the entries. `path` is now optional; a batch with a
  path-less entry is still refused, and the refusal names the entry by index. (I282)

- **`ERROR Transient` - a failure that is worth retrying, and says so.** MSBuild's out-of-process
  build host can drop the RPC that Roslyn's apply path uses when a new `.cs` document is added; that
  surfaced as `ERROR Internal: RemoteInvocationException`, whose remedy says "this is a server defect
  ... report it". Any exception chain carrying a `RemoteInvocationException`, `ConnectionLostException`
  or `RemoteRpcException` now answers `ERROR Transient` with the retry, and says nothing was written.
  The apply itself is now taken under the same process-wide MSBuild evaluation lock `ProjectGlobs` and
  `project_properties` already use, so a `ProjectCollection` evaluation and a build-host evaluation can
  no longer overlap. The remedy says the project file was restored and a file the edit was adding may
  already be on disk, because that is all the call site can prove. Locked by `TransientFailureTests`
  and `ConcurrentNewCSharpWrites_IntoOneProject_AnswerOnlyASuccessOrAnEditConflict`. (I281)

- **Two token budgets are re-baselined, with the growth measured.** `edit_text` gained `toPath` and
  `read_text` gained `occurrence`, so the markup-narrowed `tools/list` grew from 20 250 to **20 379**
  tokens (+129, 0.6 %) after every description this release touched was trimmed back; `SKILL.md` grew
  from 21 100 to **21 770** tokens (+670, 3.2 %) for the nine behaviours it now teaches. Both budgets
  are set to the measured value, so the next change cannot give the trimming back.

- **A census gate over `CHANGELOG.md`'s own version headings and link definitions.**
  `ChangelogReferenceTests` now asserts that every `## [X.Y.Z]` heading has a link definition, that no
  definition names a heading that is not there, that `[Unreleased]` compares against the newest
  version, and that every `git tag --list v*` has a heading - minus `v0.15.1`, a tag created on the
  0.15.0 commit by mistake, which carries a written reason and a ratchet. It found two live defects on
  `main`: a `[0.28.0]` link definition with no heading, and the shipped `v0.28.0` release having no
  section at all. The `## [0.28.0]` section is restored from its own commits and marked as
  reconstructed. Locked by `EveryVersionHeading_HasALinkDefinition_AndEveryDefinitionNamesAHeading`,
  `EveryVersionOlderThanTheNewest_NamesATagThatExists`, `EveryTag_HasAVersionHeading` and
  `TheUnreleasedComparison_PointsAtTheNewestVersion`. (I280)

### Changed

- **`replace_symbol addTo=` refuses a leaf name that names two containing types.** It matched the leaf
  name only and returned the first hit, so two targets whose containers are both called `Duplicate` in
  different namespaces silently landed the new members in whichever came first - undetectable from the
  response. It now compares the qualified name when the reference carries a `.`, and refuses naming
  both candidates when a bare leaf matches more than one, so `addTo=Fixture.Broken.Alpha.Duplicate`
  is the way through. (I279)

- **`read_text ref=` and every other git answer are decoded as UTF-8.** The child process inherited
  the console code page, so an em dash came back as `ΓÇö` on Windows: a historical read could not be
  compared with the working tree, and text copied out of one would have written the corruption into
  the file. `GitRunner` now sets the child's standard-output and standard-error encoding explicitly.
  `dotnet` is deliberately left on the inherited encoding - MSBuild writes in the console code page,
  and forcing UTF-8 there would corrupt what it currently gets right. Locked by
  `ReadText_AtARef_DecodesGitsOutputAsUtf8`, which commits a real UTF-8 file into a temp repository
  and reads it back. (I284)

- **The batch steer no longer fires on a run of ranged reads.** Three consecutive `read_text` calls
  that each needed a **different** line range were told to send the next ones as `paths=[...]`, which
  cannot express a per-entry range - the steer asked for a call that would answer the wrong lines. A
  `read_text` carrying `startLine`, `endLine`, `tail` or `section` now breaks the run instead of
  extending it, so it neither steers nor counts. Whole-file reads still steer from the second call.
  (I283)

- **`changed_files` counts files on both halves of a capped summary.** Once an untracked directory
  folded, the shown half counted **rows** and the total half counted **files**, so the same summary
  carried two different units - `1/15 files truncated` for a shown row covering eight of them. The
  shown half now sums the files each shown row covers, the way `find_files depth=` already does. (I278)

- **The advertised-schema compaction drops `default` at parameter depth only.** It dropped any key
  named `default` at any depth, so a member called `default` inside the item schema of `files=` or
  `edits=` would have vanished from the advertised contract without a census gate noticing - the
  parameter census only reaches top-level properties. The compactor now walks root -> `properties` ->
  parameter and removes the key there; everything below is copied through. The 2 672 characters per
  `tools/list` the compaction is worth are unchanged. (I277)

- **`search_symbols` no longer answers a filtered query off-filter.** The referenced-assembly fallback
  holds named types only and is in neither half of the solution, so `kind=method` fell through to it
  and could answer with a class the caller had excluded, and `scope=src` could answer with a symbol
  that is in no project. Both now answer `0 symbols` plus the reason and the parameter to drop; a type
  kind still reaches the fallback. (I276)

- **The `guard=absent` and `skill=absent` warnings are decided before the transport starts.** They were
  published by a fire-and-forget maintenance task begun one line above `host.RunAsync`, so the first
  `workspace_status` of a process - the orientation call the warning exists for - could answer without
  them. `McpHost` now awaits one asset probe and publishes it before the transport begins; the
  maintenance pass still refreshes and re-publishes afterwards, and a probe that throws is swallowed
  rather than failing startup. (I275)

- **`workspace_status` reports a document whose in-memory text no longer matches disk.** A concurrent
  session reverting a file the workspace still holds is undetectable from inside terse - every read
  answers from the same snapshot that is wrong - and the only way out was two `Bash: grep -c` calls.
  The status now compares the documents changed since load against their bytes and answers
  `WARNING workspace=diverged - N document(s) differ from disk: <paths>` with the reload to run;
  `verbose=true` adds `disk=in-sync probed=N of M` so a clean check is visible too. It probes only the
  files **this server wrote**, taken from the undo history and capped at 50, so a read-only session
  pays nothing at all and `verbose=true` says `disk=not probed` - the orientation call never walks the
  solution. (I274)

- **`workspace_status verbose=true` breaks the advertised token total into what it is spent on.**
  `advertised=88 tools 25154 tokens` now gains a second line -
  `toolDescriptions=N parameterDescriptions=N schemaFrame=N names=N` - so the one number this
  repository optimises against can be attributed without driving the server over stdio from a python
  script, which is how the evidence for `I266` had to be gathered (~15 minutes per measurement). It
  stays behind `verbose=true`: it is a maintainer question, not an agent one. (I273)

- **`section=` takes `occurrence=`, on `edit_text` and on `read_text`.** A heading that deliberately
  repeats - `### Added`, once per release in `CHANGELOG.md` - answered `'### Added' names 36 sections`
  with a remedy ("pass the heading with its level") that the call had already followed, and the only
  way in was an `oldText` anchor. The refusal now names `occurrence=1..N` and the start line of each
  candidate, so the index is picked without a re-read, an index past the last names the range it could
  have picked, and `read_text occurrence=` without a `section=` is refused rather than ignored. (I272)

- **A glob carrying `{` or `}` is refused instead of answering `0 matches`.** Brace expansion is not
  implemented, so `**/*.{md,yml}` matched nothing and read as "the string occurs nowhere" - a
  confidently wrong negative that cost a call, a retry, and twice nearly a wrong conclusion.
  `find_files glob=`, `search_text`/`search_regex` `glob=` and `exclude=`, and `changed_files exclude=`
  now answer `ERROR InvalidArgument` naming the supported syntax. (I270)

- **`analyze` and `get_diagnostics` name the declaration each finding sits in.** Every position is now
  followed by the addressable id of the declaration containing it -
  `src/Fixture.Trading/OrderService.cs:15:16 OrderService.Unused` - so the fix list is symbols you can
  feed straight to `get_symbol_source`, not coordinates you first have to turn into symbols. `gate`
  carries the tag too, through the same `AnalysisService.FindingsAsync`. Measured
  on the 12-site `RS0030` sweep that produced this row: the `get_file_outline paths=[5]` it used to
  cost was ~2 900 tokens, of which nine lines were read. A finding whose location has no source tree is
  left exactly as it was. `build` is deliberately **not** covered: it releases the workspace lease
  before shelling out, so no compilation is available to map its positions onto. (I269)

- **The improvements backlog is two files.** `IMPROVEMENTS.md` now carries `## Open` alone, and every
  closed row moved to `IMPROVEMENTS-ARCHIVE.md` under `## Closed`, with a one-line pointer in each file
  to the other — the split Keep a Changelog 2.0.0 prescribes for a history file that has outgrown one
  document. At `ff4423a` the single file was **205 790 bytes** for **12 open rows and 319 closed ones**,
  so every read of the work that is still open paid ~51 000 tokens; the open backlog is now **10 475
  bytes**, **under 6 %** of that. `BacklogShapeTests` reads both files, and additionally asserts each
  file's pointer line and that the archive is never empty.

## [0.37.0] - 2026-08-15

**Response formats changed.** `analyze` and `get_diagnostics` now fold findings that share an id, a
severity and a message onto **one** line carrying every position, so a record line can carry several
comma-separated positions before its `: message`. `cleanup verify=true` and `format verify=true` append
the step to each named file (`Foo.cs  whitespace`). `changed_files` folds a directory contributing more
than five untracked files into a single `dir/**` row, whose first token is a glob rather than a path -
it is excluded from the `paths=[...]` batch line for exactly that reason. `project_properties` answers
MSBuild's evaluated properties and appends the file that set each. **The advertised tool schemas no
longer carry a `default` key**, and a name that matches no source declaration is answered from the
referenced assemblies instead of `0 symbols`. Under SemVer this is a breaking change expressed, per
major-version-zero, as a MINOR bump.

### Added

- **`find_files depth=N` answers the shape of a tree instead of its files.** Everything below the
  Nth path segment folds into one `src/TerseSharp.Core/**  x94 files` row, the way `changed_files`
  folds an untracked directory; the count line still counts every file, a directory holding a single
  match is still printed as that file, and a folded row is a glob rather than a path, so it is left
  out of the closing `paths=[...]` batch line. Measured on this repository: `find_files
  glob=**/*.cs depth=2` answers 11 rows where the flat listing answers 367. Closes `I263`.
- **A name no source declaration matches is now resolved against the workspace's referenced
  assemblies.** `search_symbols query=JsonSerializer` answered `0 symbols` and
  `get_type_outline symbolId=System.Threading.Lock` answered `NOT_RESOLVED`, so every question about
  a framework or NuGet API fell back to the model's own memory - measured at a 25-38 % deprecated-API
  rate in LLM completion. `search_symbols` now falls back to the referenced assemblies when nothing
  in source matched, `get_type_outline` lists a metadata type's public members instead of claiming
  the symbol does not exist, `get_symbol_source` answers the signature plus a `get_type_outline`
  steer rather than `SymbolNotFound`, and `get_symbol` names the assembly and version
  (`at System.Runtime 10.0.0.0 in System.Threading`) where it used to print `at -`. The metadata half
  matches the type name exactly - no CamelHump, no substring - and no body is decompiled. A qualified
  name is checked against the type's own namespace, so `System.Collections.StringBuilder` is refused
  rather than answered with `System.Text`'s; the outline names the id that replied; the listing
  reports what it truncated; and the fallback is wired to the four read tools only, so a name only
  metadata matches still answers a mutating tool on the name it was given. Closes `I264`.
- **The async and sync-over-async hard gates are now compiled, not asserted.**
  `Microsoft.CodeAnalysis.BannedApiAnalyzers` plus a checked-in `src/BannedSymbols.txt` fail the build
  on `.Result`, `Task.Wait()`, `GetAwaiter().GetResult()`, `Thread.Sleep`, the synchronous
  `File.ReadAll*`/`ReadLines`/`WriteAll*`/`AppendAllText`, `StreamReader.ReadToEnd` and
  `XDocument.Load(path)`, each entry naming the replacement to use instead. The rule was written in `CLAUDE.md` and unenforced: 14
  synchronous `File` sites survived in `src/` and the agent emitted 68 more in one measured week. The
  18 pre-existing sites are now `[SuppressMessage]` attributes carrying a written justification each -
  a checked-in, reviewable set ratcheted by `BannedApiTests`, which also asserts every banned symbol
  names its replacement. It compiles the sync-over-async and synchronous-file halves of the gate, not
  all of it: a `FileStream` without `FileOptions.Asynchronous`, and `SemaphoreSlim.Wait`, are still
  prose, and the census row says so. Closes `I265`.
- **A `retryWith` token now holds `add=`, `addTo=` and `usings=`, and a replay by the wrong edit tool
  names the tool that can apply it.** The token held only the rejected declarations, so 3 of the 4
  retries one measured task issued had to re-send the whole payload anyway, and a `replace_symbol_body`
  given a `replace_symbol` token spent a call learning that tokens are per-tool. The retry is now one
  call carrying nothing but the token; passing `add=`/`usings=` again overrides what is held, and the
  refusal reads `retryWith=r3 was issued by replace_symbol, not by replace_symbol_body`. Closes
  `I267`.
- **A declaration that does not parse now quotes the text around the fault and names which
  `declarations=` entry it came from.** `ERROR InvalidArgument: the declaration did not parse: Syntax
  error, '}' expected` named no position, so two ~2 500-character payloads were re-sent blind to find
  a JSON-escaping fault. The message now ends `at offset 27 of 28: public int Unused() => 7 + ;` -
  the same ~80-character window the `JsonException` path already quotes - and a batched
  `replace_symbol` prefixes every entry's failure with `declarations[1]:`, so the faulty entry never
  has to be guessed. Closes `I268`.

- **`read_text columns="Finding,Tool"` projects a markdown table down to the named columns.** A file
  whose whole content is two long tables has nothing `headings=true` can narrow, so "which rows does
  this table hold" cost a clipped read plus a truncated `search_regex matchesOnly` sweep. It now costs
  one call returning one line per row. A column no table under the read declares is refused naming the
  columns that exist - **even when the other columns matched**, because a projection that silently drops
  a mistyped header answers a question the caller never asked; the refusal names only the columns that
  missed, and each existing column once. It composes with `section=`, which scopes the projection to
  that section's tables instead of being overridden by it - and a refusal under a section says so
  (`names no column of section '## Open' of notes.md`) plus how to widen it, because the columns it
  scanned are that section's, not the file's. `maxLines=` bounds the rows so the summary can report
  `2/4 rows truncated`, and `headings=true`, `startLine=`, `endLine=` and `tail=` beside it are all
  refused rather than one of the two silently winning: a projection is addressed by table, not by
  line. Closes `I255`.
- **`write_text ref=HEAD` restores a file's content from a git ref.** `undo_last_change` holds Roslyn
  solution snapshots, so a corrupted `.csproj` or `.md` write had no in-server way back and the only
  recovery was `Bash: git checkout --`. The restore goes through the same compile gate and the same
  diff response as any other write. Closes the shell-out half of `I239`.
- **`write_text` warns when it writes HTML-escaped markup.** Content carrying `&lt;` or `&gt;` and no
  raw `<`, written to a `.csproj`, `.props`, `.targets`, `.xml`, `.xaml`, `.resx` or `.slnx`, now keeps
  the full response and names `write_text ref=HEAD` as the way back, instead of answering
  `changedLines=5` for a file that is no longer markup. Closes the detection half of `I239`.
- **`list_projects path=<file>` answers which project compiles that file.** The question behind every
  "will my edit be compile-gated" needed reading `ProjectGlobs`, `DocumentLookup` and the `.csproj` by
  hand. It now answers from the same evaluated `EnableDefaultItems`/`EnableDefaultCompileItems` the
  edit path already reads, and says plainly when no project compiles the path. Closes `I241`.
- **`impact_of tests=true` names the test classes that reference a symbol, each as a ready
  `run_tests test=` argument.** They are the direct references only, tagged `HEURISTIC`, so the answer
  narrows a run rather than replacing one; it reuses the reference scan the tool already ran, and only
  a type that actually declares a test is offered, so a `run_tests test=` argument can never match zero
  tests. Closes `I249`.
- **`package_list vulnerable=true` and `outdated=true` answer from the restored graph.** The last
  dependency question with no tool - a known advisory or a newer version - now runs through the shared
  child-process runner, and `ToolGuard` denies `dotnet list package`. The two are mutually exclusive,
  exactly as the CLI's own flags are, and a run that exits non-zero says `FAILED … nothing was examined,
  so this is not a clean bill of health` with the CLI's own output tail rather than a `0 packages`
  summary and a guessed cause. Closes `I253`.
- **`replace_symbol addTo=` names which containing type `add=` lands in.** A batch whose targets do not
  all share one containing type was refused outright even when every appended member belonged to one of
  them; `addTo` must name one of the targets' own containers, so a member can never land in a type no
  target lives in. Closes `I238`.
- **`path` is an alias for `glob` on `search_text`, `search_regex` and `find_files`, and `severity` for
  `minSeverity` on `analyze`.** Those four spellings were 43 of the 98 `InvalidArgument` retries measured
  in a week, on tools whose siblings already accept them. An unknown plural now also names the list
  parameter the tool actually declares. Closes `I258`.

### Changed

- **The advertised schemas no longer carry a `default` key.** Every parameter's default is already
  stated in its own description ("Default false.", "Max results (100)."), so the JSON copy was paid
  for on every `tools/list` and read by nobody: the server binds from the C# default either way.
  Measured on this repository's own 88-tool surface: **107 933 -> 105 261 characters**, 2 672 fewer
  (~668 tokens), and `TokenBudgetE2ETests.TheAdvertisedToolPayload_StaysWithinItsBudget` drops from
  26 600 to 25 900 tokens so it cannot be given back. Closes `I266`, whose proposed ~40 % is refuted
  by the measurement recorded against it in `IMPROVEMENTS.md`.
- **`analyze` folds findings that share an id, a severity and a message onto one line carrying every
  position.** 14 of 19 records for one file were the same `IDE0058` message repeated with a full path
  each; the positions are kept, because they are the fix list, capped at 20 per line with `+N more` so
  a rule that fires thousands of times cannot produce a line no argument can narrow. `get_diagnostics`
  folds the same way, and the summary now names the unit it counts - `N diagnostics, one record per id
  and message` - with a `total=N occurrence(s) folded onto M record(s)` note whenever the two differ,
  so a folded count is never mistaken for a diagnostic count. **`sinceLast=true` still diffs per
  occurrence**: the history records one entry per occurrence, byte-identical entries kept distinct by
  construction, and the fold happens at render time - so adding a second occurrence of an existing
  diagnostic can never report the first as `FIXED`. Closes `I252`.
- **`analyze ids=` reports `NOT_ENABLED <id>` for an id no referenced analyzer declares.** A targeted
  sweep answering `0 diagnostics` was byte-identical whether the rule ran and found nothing or was never
  enabled. Compiler ids, `TERSE001`, and any id the pass actually found - a generator's or the
  analyzer-crash `AD0001` - are never reported, so the note can never contradict the findings beside it.
  Closes `I246`.
- **`cleanup verify=true` and `format verify=true` name the step beside each file** - `whitespace`,
  `fixers` or `fixers+whitespace` - so attributing a `VERIFY_FAILED` costs one call instead of a
  `cleanup dryRun verbose` plus a `format verify=true`. Closes `I240`.
- **A `VERIFY_FAILED` from a mode that also reformats now names the byte-equivalent CI pair.** 40 of 57
  measured `VERIFY_FAILED`s came from `fix=all`, which runs the whitespace formatter the ubuntu leg does
  not, so the verdict said nothing about whether CI would be red. Closes `I257`.
- **`changed_files` folds a directory contributing more than five untracked files into one
  `.research/**  +? -?  ?  x40 untracked` row.** Tracked files stay one per line and the count line still
  counts every file, so nothing is hidden. Closes `I237`.
- **Every mutating tool names the warnings its edit introduced**, up to five and saying `5 of 12 shown`
  when there are more, instead of reporting only `warnings=N (+3)` and leaving an `analyze` that
  re-realizes the compilations as the only way to learn which three. Closes `I247`.
- **`project_properties` answers MSBuild's evaluated properties, each with the file that set it.** It
  read the project file's own XML, so a project whose nine properties come from `Directory.Build.props`
  answered `0 properties` - indistinguishable from a project that declares none. It reports the
  **winning** value of each property, not every intermediate one, so a property overridden in the
  `.csproj` appears once. Properties defined outside the workspace root, which is the SDK's own
  hundreds, are left out, and the evaluation runs off the request thread because MSBuild has no async
  overload. Closes `I244`.
- **`resx_set entries=` refuses a line with no `Key=Value` separator instead of dropping it silently**,
  naming the line numbers as the caller sent them - blank lines are skipped without shifting the count -
  and the repeat steer now names `entries` so the plural is discoverable. A measured 299 calls in four
  runs, the longest 138 consecutive, never used it. Closes `I262`.
- **`workspace_status` and `load_workspace` warn when the `PreToolUse` guard or the skill is not
  installed.** The guard was absent for a whole measured week while the corpus paid 884 `Bash` calls it
  denies, and only 3 of 129 `workspace_status` calls passed `verbose=true`, which was the only place that
  said so. Closes `I256`.
- **`read_text` on a `.cs` file returned verbatim ends with the `symbolIds=[...]` batch line**, when the
  read covered the whole file and it has at most ten members, so the next read is member-scoped. 989 of
  1 608 `.cs` reads passed `verbose=true` and got no steer at all. Closes `I261`.
- **`explore_symbol` and `impact_of` are described by the chains they replace.** Both were called zero
  times in 19 849 measured tool calls while the adjacent navigation pairs they collapse ran 365 times.
  The next scan re-measures the rate; a shipped tool nobody calls is a defect to fix or delete. Closes
  `I259`.

### Fixed

- **`workspace_status` reported the *uncompacted* advertised payload until a second `tools/list`
  arrived.** Its measuring filter was registered innermost, so the first listing was measured before
  `SchemaCompactor` had run; the compactor then mutated the shared `Tool` instances, and every later
  listing agreed by accident. The number now comes from the outermost filter, so it is what the
  client actually receives on the first request and on every one after it. Found by
  `TokenBudgetE2ETests.TheAdvertisedToolPayload_StaysWithinItsBudget`, whose `reported == measured`
  assertion only fires on a server that has served exactly one listing.
- **MSBuild project evaluation is serialized across the process.** `ProjectGlobs` and the new
  `ProjectEvaluation` both load a `ProjectCollection`, and MSBuild's project-root-element cache is not
  safe for concurrent loads of the same project; `project_properties` runs its evaluation off the
  request thread, so the two could overlap. Both now take one shared lock. This is hardening: a
  `RemoteInvocationException` was observed twice in a full E2E run under the unguarded shape and has
  not recurred since, but the link was not proven by a deterministic reproduction.
- **The asset self-check no longer rides on the update check.** `terse` refreshed a stale skill or guard
  only when update checking was enabled (`TERSE_UPDATE=0` turned both off); the asset half now always
  runs, which is what makes the new `guard=absent` warning reachable.
- **A parameter list copied from a signature now resolves when the type carries type arguments or is a
  tuple.** `Weigh(int count)` resolved and `Weigh(Boxed<IHandler> boxed)` did not, because the generic
  branch compared the text after the closing bracket with `SequenceEqual` and never stripped a trailing
  parameter name, and `Normalize` dropped the space after a `)` so a named tuple parameter lost its
  separator. `EveryReferenceAnOutlinePrints_ResolvesBackToASymbol` and
  `GetSymbolSource_ForAnOverloadWhoseParameterCarriesTypeArguments_ResolvesTheShortForm` cover it.
  Closes `I242`.
- **The advertised-payload budget is asserted against the number `workspace_status` reports**, so the
  ceiling can no longer be measured on a narrower surface than the agent pays for. Closes `I250`.


## [0.36.0] - 2026-08-14

### Fixed

- **A parameter list is now matched structurally, so a name an outline printed resolves even when a
  parameter carries type arguments or is a tuple.** Two defects, one symptom: `MatchesType` compared
  Roslyn's fully-qualified display string against the requested text with a single suffix test, so
  qualification could only be omitted at the outermost level — `Unrecognized(RequestContext<CallToolRequestParams>)`
  answered `SymbolNotFound` while `Unrecognized(ModelContextProtocol.Server.RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams>)`
  resolved; and `Split` trimmed `(` and `)` greedily, so a tuple parameter's own parentheses were
  stripped too and `Weigh((Order Left, Order Right))` was counted as two parameters. The comparison
  now matches the head by namespace suffix and then recurses into every type argument and tuple
  element, and the parameter list gives up exactly one enclosing pair of parentheses. The
  fully-qualified spelling still resolves. This also closes the half of the same defect where the
  `nearest:` list of a `SymbolNotFound` printed back a candidate the same call could not resolve,
  because that candidate is `SymbolReference.Brief`'s short form. The fixture gained the overloads
  that make it observable — `Awkward.Weigh(int)`, `Awkward.Weigh(Boxed<IHandler>)` and
  `Awkward.Weigh((IHandler Left, IHandler Right))` — so
  `NavigationToolsE2ETests.EveryReferenceAnOutlinePrints_ResolvesBackToASymbol` can fail; gated by it
  and by `NavigationToolsE2ETests.GetSymbolSource_ForAnOverloadWhoseParameterCarriesTypeArguments_ResolvesTheShortForm`
  plus `SymbolReferenceTests.Parse_ForATupleParameter_KeepsItsOwnParentheses`. A tuple parameter also
  matches the positional spelling — `Weigh((IHandler, IHandler))` finds
  `Weigh((IHandler Left, IHandler Right))` — because `Normalize` now keeps exactly the one space that
  separates a type from an element name and drops every other, and the leaf comparison ignores that
  name. It ignores it **only** on a side that carries no type argument list, so a plain name can
  never be suffix-matched against part of a tuple: `Weigh(IHandler)` and `Weigh(int [])` answer
  `SymbolNotFound` rather than picking an overload that does not exist, asserted by
  `NavigationToolsE2ETests.GetSymbolSource_ForAParameterListNoOverloadDeclares_RefusesInsteadOfPickingOne`.
  An **empty parameter slot keeps its arity**, in every position of the parameter list:
  `Reconcile(Order, )` and `Reconcile(Order,)` still name the two-parameter overload, as
  `Reconcile(, Order)` always did, and an empty slot still matches any type — the rewritten splitter
  emits the trailing slot the old `Trim('(', ')')` loop emitted, so no empty-slot spelling changes
  meaning. One level down it is now **stricter** than before rather than looser: a trailing empty
  argument inside a type-argument list or a tuple no longer vanishes, so `Weigh(Boxed<IHandler,>)`
  and `Weigh((IHandler, IHandler,))` answer `SymbolNotFound` instead of resolving as if the comma
  were not there. Pinned by
  `SymbolReferenceTests.Parse_ForAnEmptyParameterSlot_KeepsItsArity` and
  `NavigationToolsE2ETests.GetSymbolSource_ForAnEmptyParameterSlot_KeepsTheArityItWasAskedFor`.
  Closes **I234**.

- **`write_text` now compile-gates a `.cs` file it is *creating*, not only one it is overwriting.**
  The gate keyed on `DocumentLookup.Find`, which a file that does not exist yet cannot satisfy, so a
  new source or test file was written ungated and answered `changedLines=N` with `errors=0` never
  checked — the ten `CS0103` errors of a file written without its `using` surfaced only at the next
  `gate` call. Both write paths now stage the new file as a document of the project that globs it,
  under the same `EditGate`: the single write and the `files=[...]` batch, where several new files
  still share **one** gate, so nothing at all is written when one of them does not compile. Selection
  is `ProjectGlobs` on the project's *evaluated* `EnableDefaultItems`/`EnableDefaultCompileItems`,
  plus directory containment, minus `bin`/`obj` and the other excluded directories — so **a new `.cs`
  file under no SDK-globbing project stays ungated**, having no compilation to be checked against. It
  is deliberately a directory rule and not an item-by-item one: a project that removes the file with
  `<Compile Remove>` is still gated, which errs toward checking rather than toward a silent pass, and
  `allowErrors=true` is the way past it. That verdict is now memoized on `(path, mtime, length)` in
  `ProjectGlobs.Memoized`, the memo `ProjectFileGuard` already owned, because evaluating a project
  costs ~220 ms and the batch path asks once per file. Gated by
  `CompileGateE2ETests.WriteText_CreatingANewFileWhoseCodeDoesNotCompile_IsRolledBackByTheCompileGate`,
  `…WriteText_CreatingAValidNewFile_AppliesAndLeavesTheProjectFileByteIdentical`,
  `…WriteText_CreatingAFileNoProjectCompiles_StaysUngated` and
  `…WriteText_WithSeveralNewFilesWhereOneDoesNotCompile_WritesNoneOfThem`. The `write_text`
  description says which files are gated. Closes **I235**.

- **`cleanup fix=style` and `fix=analyzers` no longer run the Roslyn whitespace formatter, so
  `verify=true` on either really is the CI command it claims to be.** `FormatService.RunAsync`
  reformatted every document on every mode, which is what `dotnet format style` and
  `dotnet format analyzers` do **not** do — so `cleanup verify=true fix=style` reported
  `src/TerseSharp.Core/ReleaseVersion.cs` on a tree the ubuntu leg accepted, over one character
  (`(< 0, _)` against the formatter's `( < 0, _)`), and the documented byte-equivalence was false. The
  two code-fix modes now apply fixes only; `fix=all` and the default `fix=usings` still remove and
  sort usings and still reformat, so they remain the supersets the docs describe, and `format` is
  still the way to run the whitespace formatter. Measured against a freshly built `terse.dll`:
  `cleanup verify=true fix=style` and `fix=analyzers` on that file go from `VERIFY_FAILED` to clean
  while `format verify=true` still reports it. Gated by
  `CompileGateE2ETests.CleanupVerify_ForStyleAndAnalyzers_IgnoresWhitespaceTheCiCommandsDoNotCheck`,
  observed red with the reformat restored. Closes **I236**.

- **`undo_last_change` no longer reports `reverted the last change` for a write that created a file.**
  A gated new-file write reaches `LoadedWorkspace.TryApplyAsync`, whose history capture skips a
  document the pre-change solution does not hold — so the entry it recorded was empty, and the next
  undo popped it, reverted nothing and said it had. An entry with no captured document is no longer
  recorded at all, so an undo after a creation reaches the previous real edit exactly as before, and
  never claims the creation was undone. Gated by
  `CompileGateE2ETests.WriteText_CreatingAGatedNewFile_DoesNotLetUndoClaimItRevertedTheCreation`.

### Changed

- **`ProjectFileGuard` now attributes MSBuild's expansion of a self-closing `<Project … />` root**, so
  the redundant `<Compile>` item Roslyn writes when a document is added to such a project is restored
  away instead of being left in the user's `.csproj`. Previously the guard refused — the root tag it
  saw in the rewritten file matched no line of the snapshot — and the item survived. Gated by
  `ProjectFileGuardTests.OnlyRedundantCompileItems_WhenMsBuildExpandedASelfClosingRoot_IsAttributable`,
  with `…_WhenAnExpandedRootAlsoGainedAProperty_IsRefused` holding the other direction, and by
  `CompileGateE2ETests.WriteText_CreatingAValidNewFile_AppliesAndLeavesTheProjectFileByteIdentical`.


## [0.35.0] - 2026-08-12

### Added

- **`workspace_status verbose=true` carries the server's own self-checks, so diagnosing terse needs
  no `terse doctor` shell-out.** Four lines join the verbose response, in `doctor`'s own
  `OK`/`FAIL <name>: <detail>` shape and directly above the trailing `terse=<version>`: `roslyn`
  (the selected SDK's Roslyn against the one this build carries — the check that explains a dead
  Razor generator), `assets` (skill and guard install state), `guard coverage` (which measured breach
  class this tree still lets through) and `phases` (`widest=`, `realizeMs`, `outlineMs`, `gateMs`,
  `diffMs` measured on the loaded workspace, which costs a `git diff --stat` spawn and a dry-run
  compile gate - about 0.9 s warm and 3.5 s cold on this repository's own solution). A self-check
  that throws - no `git` on `PATH`, a hand-edited client settings file - reports its own
  `FAIL <name>: the check itself failed` line and never replaces the whole status with an error.
  Diagnosing the server was the one class of question
  this repository's own hard gate forced to the shell: three ~40 s `Bash` shell-outs in the previous
  release-shaped run. The default response is unchanged — no self-check line is emitted without
  `verbose=true`. Gated by
  `BacklogClosureE2ETests.WorkspaceStatus_WithVerbose_CarriesTheDoctorSelfChecksSoDiagnosingTheServerNeedsNoShellOut`.
  Closes **I228**.

- **`edit_text section=` can now write INSIDE a section, not only replace it.** One new parameter,
  `place`: `append` inserts `newText` after the section's last non-blank line, `prepend` directly
  under its heading line, and anything else — an unknown value, or a `place` with no `section` — is
  refused by name rather than quietly falling back to a whole-section replace. It is one parameter
  and not two booleans precisely so "both at once" cannot be expressed. Adding a changelog entry or a
  backlog row had to be anchored on `### Added` instead — 14 such edits in the previous
  release-shaped run, one of which answered `oldText matched 28 times` and needed `occurrence=1`.
  `edits=[…]` entries take `place` too, and a top-level `place` passed *beside* `edits=` is refused
  by the same guard that already refused a top-level `oldText`/`newText`/`section`, rather than being
  dropped without a word. Gated by
  `BacklogClosureE2ETests.EditText_WithSectionAndAPlace_WritesInsideTheSectionInsteadOfReplacingIt`
  `…EditText_WithAPlaceThatIsNotAPlacementOrWithoutASection_IsRefusedRatherThanSilentlyReplacing`
  and `…EditText_WithATopLevelPlaceAndEdits_IsRefusedRatherThanDroppingThePlacement`.
  Closes **I230**.

- **`get_file_outline` parses a `.cs` file that is not a document of the loaded solution instead of
  refusing it.** A path under the workspace root that exists on disk but belongs to no project - the
  `fixtures/**` tree here is deliberately outside `TerseSharp.slnx` - answered `DocumentNotFound` and
  cost one error plus one `read_text` per occurrence, three in the previous run, although
  `OutlineService.FromText` already parsed exactly that shape for `ref=`. It now falls through to
  that parse and appends `HEURISTIC parsed from the file's own text`, so the caller knows the members
  came from syntax rather than from the compilation. Containment is unchanged: a path outside the
  workspace root is still refused. Gated by
  `BacklogClosureE2ETests.GetFileOutline_OnACSharpFileThatIsNotADocument_ParsesItFromTextInsteadOfRefusing`.
  Closes **I231**.

- **`BuildWarningsE2ETests` no longer depends on what ran before it, or beside it.** Four spurious
  failures were observed across three full-suite runs — one `warnings=5` against its asserted 3, and
  three `CS0169` assertions failing behind 133 s builds — while every one of them passed in
  isolation. Two independent causes: the count assertions built the **whole solution**, so the number
  depended on whether the test project happened to be up to date, and the class was the only
  build-heavy E2E class **outside** `TerseServerCollection`, so its `dotnet build` ran concurrently
  with every other class's. The counting builds are now scoped to `project=Fixture.Warning`, whose
  three deliberate warnings all live in one touched file — and because that scoping stops building
  the fixture's test project, the one test that needs both halves loaded now builds the whole
  solution and reloads before it searches. The class also joins `TerseServerCollection`, so its
  builds no longer overlap the thirty-five classes already in it. Extending that membership to the
  other five build-heavy E2E classes was **tried and reverted**: run 31634146607 went red on ubuntu
  and windows, `ChangedTestSelectionE2ETests` falling back to the whole solution (`total=2` against
  its asserted `total=1`), and all five are green outside the collection. That is recorded as a
  reasoned, ratcheted exclusion set in `E2ECollectionCensusTests`, which discovers every E2E class
  that calls `build`, `run_tests`, `rerun_failed`, `list_tests` or `clean` from source and fails on
  one that is neither in the collection nor excluded with a written reason. Gated by
  `BuildWarningsE2ETests.Build_AfterAFailedBuildOfTheWholeSolution_StillReportsOnlyTheWarningsOfTheProjectItWasScopedTo`,
  which runs the failing whole-solution build first and asserts the scoped one still answers
  `warnings=3`. Closes **I232**.

- **`ChildProcessTests.StartInfo_ForEveryOtherVariable_LeavesItInherited` no longer races any test
  that sets an environment variable.** It counted the process's environment, built a
  `ProcessStartInfo`, and asserted the two counts equal — so a variable set or cleared by a
  concurrently running test between the two reads failed it. Observed once on the ubuntu leg of run
  31636905773 as `Expected: 126 / Actual: 127`, a pre-existing race that this release's scheduling
  change happened to expose. It now asserts the property under test — every non-locator variable
  that is still set reaches the child — instead of a count, which is immune to a concurrent addition
  or removal in either direction.

- **The first compile-gated edit of a server process names what the gate did not check.** One line,
  once, never repeated: `gate=semantic - errors=0 means the semantic model is clean; emit-time and
  source-generator errors are NOT checked, so run build once before you push, not after every edit`.
  The previous release-shaped run made **31 `build` calls at ~5.6 s each**, about three minutes,
  almost all of them confirming what a `errors=0 (+0)` edit had already proved — the gap a build
  really closes is the emit path, and nothing said so. The notice is a caveat about the gate's own
  coverage, so it prints in the condensed success response as well; a rejected edit does not spend
  it, and neither does a `dryRun` - which matters because `workspace_status verbose=true` runs a
  dry-run gate of its own and would otherwise have eaten the one notice the next real edit owes.
  Gated by
  `BacklogClosureE2ETests.TheFirstCompileGatedEdit_NamesWhatTheGateDidNotCheckAndNeverRepeatsIt`,
  `…AVerboseWorkspaceStatus_DoesNotConsumeTheGateNoticeTheNextEditOwes` and `GateCoverageTests`.
  Closes **I233**.

- **Three token budgets moved, and the numbers are recorded so the ratchet still bites.** The
  advertised `tools/list` payload goes 24 200 -> 24 400 (measured 24 330 over 88 tools), the
  markup-narrowed surface 19 000 -> 19 150 (measured 19 091 over 57), and `SKILL.md` 19 200 -> 19 600
  (measured 19 570). The growth is `edit_text`'s `place` parameter plus the skill rows that teach the
  five changes above; by I224's measured break-even — ~255 tokens of `tools/list` costs 1.51 M
  base-input-equivalent tokens across 508 sessions against 46 817 BIE per removed API turn — 130
  tokens breaks even at about 17 calls per 508 sessions, and the previous run alone made 14 anchored
  `### Added` edits and 3 `terse doctor` shell-outs. Each budget is set just above its measurement,
  not padded.

### Fixed

- **The one-shot `terse call` probe refuses exactly what the MCP client would.** It bound `--json`
  by parameter name and silently dropped every key no parameter matched, so
  `call analyze --json '{"severity":"info"}'` answered a successful analysis while the server itself
  refused the same call with `ERROR InvalidArgument: unrecognized severity`. The probe is the arbiter
  this repository's own hard gate names for a claim about tool behaviour, so a probe more permissive
  than the server is a confident wrong answer. Both paths now share one
  `ToolArgumentFilter.Unrecognized`, which names the unknown arguments and lists what the tool
  declares. Gated by
  `InstallCommandE2ETests.Call_WithAnArgumentTheToolDoesNotDeclare_RefusesItExactlyAsTheServerWould`.
  Closes **I229**.


## [0.34.0] - 2026-08-12

### Changed

- **The rule that vetoed new tools is replaced by the two numbers that actually decide it.**
  `CLAUDE.md` said *"a 57th tool must beat the one it splits"*; measurement says one extra tool is
  ~255 tokens of `tools/list`, which cached cost **1.51 M base-input-equivalent tokens across 508
  sessions** against **46 817 BIE per removed API turn** — a **break-even of 32 calls per 508
  sessions**, far below the bar that sentence asserted. The real veto is **discoverability**, and it
  is measurable: `explore_symbol` was called 7 times and `impact_of` once in 683 sessions while the
  chains they exist to collapse ran 1 922 adjacent navigation pairs. The rule now says to estimate the
  call count, ship above 32 calls per 500 sessions, and **re-measure the per-tool selection rate on
  the next scan — a shipped tool nobody calls is a defect to fix or delete, not a number to defend.**
  Pricing the surface by the "accuracy degrades past 30-50 tools" figure stays refused: it exists in a
  primary source but carries no eval, dataset, curve or sample size, and two independent measurements
  bracket it badly. Closes **I224**.

- **`diff_text` returns 1 000 lines by default, up from 400, and a truncated answer names the exact
  `maxLines=` that returns the rest.** It truncated on **105 of 133 calls (78.9 %)** — the highest
  steer rate of any tool measured, against `find_files` 23.0 %, `search_symbols` 15.0 % and
  `read_text` 11.1 % — so the caller paid a second call at p50 3 595 ms. The steer now reads
  `narrow with path=, paths= or maxLines=1234`, which is one retry rather than a widening ladder. The
  other three caps are untouched: a wider default there pays the payload on every call that did not
  need it. Locked by `GitToolsE2ETests.DiffText_WhenItTruncates_NamesTheExactMaxLinesThatReturnsTheRest`
  beside the unchanged `…DiffText_NeverReturnsMoreLinesThanMaxLines`. Closes **I220**.

- **The repeat steer fires on the second consecutive call, not the third, and is now an imperative
  naming a count.** `2 read_text calls in a row - pass paths=[...] with the next 2+ in ONE call`.
  Two measurements drove both halves. Reach: over 4 982 plural-capable calls, 1 735 (34.8 %) collapse
  into a ten-item batch, but a steer that first appears on the *third* call reaches only 432 (24.9 %)
  of them — 571 runs are exactly two calls long and end before any third-call steer can be read —
  against 802 (46.2 %) at a threshold of two, worth **+370 calls = 17.3 M base-input-equivalent
  tokens and 1.0 h of model think** in that corpus. Form: W&D (arXiv:2602.07359) measured that a
  model cannot self-select parallel width, while the imperative *"you MUST make at least m but not
  more than m+1 function calls"* moved BrowseComp 66 % → 74 % and nearly halved turns; the shipped
  steer stated a fact and named neither an obligation nor a number. Threshold one stays refused: it
  would fire on the first call of every plural-capable tool. Locked by
  `RepeatSteerTests.Steer_SaysNothingUntilTheSecondCallOfTheSameTool` and
  `BacklogClosureE2ETests.ASecondConsecutiveCallOfTheSameTool_CarriesTheImperativeSteerThatNamesItsPluralParameter`.
  Closes **I218** and **I226**.

### Added

- **An answer-quality gate that scores answers, not bytes.** `AnswerQualityE2ETests` carries 17
  questions a human would actually ask of a codebase — *which members does OrderService declare and on
  which lines*, *who implements IOrderRepository*, *where is it registered for DI*, *what does line 11
  say*, *what did the last commit touching this file do* — each with the facts its answer must carry,
  verified against the fixture. A format or default change that saves tokens by dropping information
  now fails a gate instead of passing a budget. This is the failure mode the prime directive itself
  creates: every token measurement in this space, including this repository's own
  `TokenBudgetE2ETests`, measures **payload size**, which is silent by construction on whether the
  answer was right — and the one published study that measured both found the trade negative, a
  graph-backed MCP agent scoring **83 % answer quality against 92 % for a plain grep-and-read agent**
  at ten times fewer tokens (arXiv:2603.27277). The set is asserted to be at least 14 questions so it
  cannot go vacuous, and a second test reports what answering all of it costs (≤ 4 000 tokens), so
  quality and cost are read together. Closes **I227**.

- **A census gate on semantic redundancy of the advertised surface.**
  `ToolCensusE2ETests.NoTwoAdvertisedTools_DescribeThemselvesNearlyIdentically` computes the word
  overlap of every pair of advertised `[Description]`s — 3 828 pairs at 88 tools — and fails above
  0.45, because merging semantically redundant tools is worth **+8.4 % to +38.6 % tool-selection
  accuracy** across three models and three benchmarks (ToolScope, arXiv:2510.20036), the largest
  published accuracy lever found anywhere in this scan, while adding merely *similar* tools costs
  8-19 % (arXiv:2504.00914). Seven pairs are excluded by design, each with a written reason and
  ratcheted by `…EverySimilarByDesignPair_StillNamesTwoAdvertisedTools`: they are opposite verbs on
  one addressing scheme (`*_add_*`/`*_remove_*`) or a literal/pattern pair, which is not the
  same-thing-twice hazard ToolScope measured. Deleting the low-traffic near-synonyms outright stays
  refused — a client can only call what its list carries. Closes **I225**.

- **`find_files name=`** matches a plain file-name substring, case-insensitively, with no glob syntax
  to get right; it combines with `glob=`, which selects first, and a glob that matched nothing now ends
  with `pass name=<text> to match a file name substring instead of a glob`. Measured: the agent
  preferred a competing server's fuzzy file finder roughly **2:1** — 363 calls against `find_files`'
  191 — and `find_files` carried the **worst error rate of any terse tool measured, 7.3 % (14/191)`,
  with 44 truncation steers. Loosening `glob=` itself stays refused: a glob that silently matches more
  than it says is the confident wrong answer this server exists to prevent, and a separate parameter
  records which semantics were asked for. Locked by
  `FileToolsE2ETests.FindFiles_WithNameAlone_MatchesAFileNameSubstringWithNoGlobToGetRight`,
  `…FindFiles_WithNameBesideAGlob_FiltersWhatTheGlobSelected`,
  `…FindFiles_WithAGlobThatMatchedNothing_NamesTheNameParameterInstead` and
  `…FindFiles_WithNeitherGlobNorName_IsRefusedNamingBoth`. Closes **I223**.

- **`history` — the 88th tool, and the last legal-`Bash` class of any size.** Commits touching a path
  or pathspec, one line each — short sha, author date, author, subject — workspace-relative and
  bounded. `baseRef=` takes a commit, a branch or a range such as `v0.32.0..HEAD`; `contains=` is
  git's pickaxe, which lists only the commits whose diff added or removed that literal and which no
  search over the working tree can answer; `message=` greps subject and body; `commit=<sha>` answers
  one commit's subject and per-file stat instead, and is **refused** beside `baseRef=`, `contains=`
  or `message=` rather than silently ignoring them. Measured: `git log` ran **725 times** — 633 of them
  `--oneline`, 48 with `-S` or `--grep` — and `git show --stat` **103 times** across 683 sessions,
  ~1.9 calls per session and 258 K tokens, against a **32-call** break-even for one extra tool's
  `tools/list` cost. `git blame` is deliberately **not** modelled: it ran once in those 683 sessions.
  The guard now denies `git log` and `git show --stat` in a .NET tree and routes them to `history`,
  routes `git show <ref>:<path>` to `read_text ref=`, and still allows `git blame`, `git difftool`,
  `git stash show`, every index or history mutation, and every `git log`/`git show` **shape `history`
  cannot produce**: `--format=`, `--pretty=`, `-s`, `--name-only`, and on `git log` also `-p`,
  `--stat`, `--numstat`, `--follow`, `--graph`, `--author=` and `-L`. A guard that denies a command
  the server cannot replace is worse than no guard, so `$(git log -1 --format=%H)` and
  `git log -p -- <path>` stay allowed while the plain listing forms do not. `README`, `NUGET_README`, `SKILL.md` and
  `CLAUDE.md` move from 87 tools to 88, the advertised-payload budget from 23 450 to 24 200 tokens on
  that evidence, and the markup-narrowed surface budget from 18 200 to 19 000. Locked by `ToolGuardTests.Inspect_ForGitHistoryInADotNetTree_NamesTheToolThatReplacesIt`,
  the widened `…Inspect_ForAGitCommandTerseSharpReplaces_Denies`, the control
  `…Inspect_ForAGitCommandNoToolReplaces_Allows`, and the whole census set through
  `ToolHappyPathE2ETests` and `ToolCoverageE2ETests`. Closes **I222**.

- **`ref=` on `read_text` and `get_file_outline` reads a file as it was at a git ref.** Historical text
  gets the same treatment as the working tree — the numbering gutter, `startLine`/`endLine`, `tail=`,
  `headings=true`, `section=` and the `maxChars` budget — and a whole `.cs` file answers its **outline**,
  parsed from that revision's own text through a throwaway compilation. `git show <ref>:<path>` ran
  **363 times** across 683 sessions of two 12-week corpora, returning **133 873 tokens** of raw
  uncompressed file text at 2 099 characters a call, and it is the one shell fallback this
  repository's own review gate *mandates*, as the introduced-vs-pre-existing evidence. `ref=` takes one
  `path=`: combined with `paths=` it is refused by name rather than reading only the first, and a ref
  that does not exist answers `ERROR InvalidArgument` rather than falling back to the working tree. A
  ref'd outline deliberately does **not** end with `symbolIds=[…]`, because those ids address the
  loaded solution and not that revision. Locked by
  `GitToolsE2ETests.ReadText_AtARef_AnswersTheOutlineOfThatRevisionAndNotItsWholeText`,
  `…ReadText_AtARef_TakesTheLineRangeAndTailTheWorkingTreeReadTakes`,
  `…GetFileOutline_AtARef_OutlinesThatRevision`,
  `…ReadText_AtARef_IsRefusedWithPathsRatherThanReadingOnlyTheFirst` and
  `…ReadText_AtARefThatDoesNotExist_SaysSoInsteadOfAnsweringTheWorkingTree`. Closes **I221**.

- **Every hit list ends with the argument its consumer takes.** `search_text`, `search_regex`,
  `find_files`, `changed_files` and `find_usages` close with `paths=["src/A.cs", "src/B.cs"]` —
  deduped, at most ten, separators normalized to `/` so the line is valid JSON on every platform, and
  suppressed entirely on a `root=` answer, whose paths are relative to a tree the consumer would not
  resolve against — and `get_file_outline` / `get_type_outline` close
  with `symbolIds=[…]` when they listed at most ten members. Measured over 12 weeks: `read_text` took
  1 710 calls of which 679 (39.7 %) were fusable, yet `paths=` was passed on 5 of 190 calls after it
  shipped, against 63 % for `search_text queries=`; the differentiator is that
  `get_symbol_source` batches at 55 % when `get_file_outline` precedes it — the one predecessor whose
  records already read as arguments — against 0 % for `read_text` after `search_text`, whose records
  the caller must extract and dedupe first. Neither line appears when there is nothing to batch
  (fewer than two distinct entries), and a **wide** outline offers `contains=` instead, because ten
  of a hundred members is a batch nobody asked for. Locked by
  `BacklogClosureE2ETests.SearchText_OverSeveralFiles_EndsWithAPasteReadyPathsArgument`,
  `…FindUsages_AcrossFiles_EndsWithAPasteReadyPathsArgument`,
  `…ChangedFiles_AndFindFiles_EndWithAPasteReadyPathsArgument`,
  `…GetFileOutline_OfANarrowFile_EndsWithAPasteReadySymbolIdsArgument` — which feeds the emitted ids
  straight back into `get_symbol_source symbolIds=` and asserts none answers `NOT_RESOLVED` — and
  `…GetFileOutline_OfAWideFile_OffersContainsRatherThanAnArbitraryTenOfItsMembers`. Closes the second
  **I217**.

- **`doctor` reports per-tree guard coverage**, on a `guard coverage:` line that runs the four
  measured breach classes through `ToolGuard` with this workspace's directory as the hook's `cwd`:
  `read-cs=denied bash-text=denied dotnet-build=denied dotnet-test=denied git-status=denied
  git-diff=denied`. The `assets guard=` line says the hook is *installed*; this one says what it
  actually denies **here**, which is the part that varies — the git rows are cwd-scoped, so a tree
  with no solution or project above it is not covered and now says so instead of being inferred.
  A 12-week scan of 509 transcripts measured 4 932 of 28 978 tool calls (17.0 %) still going to a
  built-in or shell command terse replaces, in trees whose own instructions carried the ban. Locked
  by `ToolGuardTests.Coverage_InADotNetTree_ReportsEveryMeasuredBreachClassDenied`,
  `…Coverage_OutsideADotNetTree_ReportsTheGitRowsAsAllowedBecauseNothingReplacesThem` and
  `InstallCommandE2ETests.Doctor_ReportsWhetherTheGuardCoversThisTreesMeasuredBreachClasses`.
  Closes the second **I216**.

- **`build verbose=true` names what it wrote, and the probe command for it.** One `wrote <path>` note
  per assembly MSBuild reported, and — for `terse.dll` — the exact
  `probe: dotnet "<path>" call <tool> --workspace "<solution>" --json '{…}'` line, because verifying a
  behaviour change against the *freshly built* binary is what this repository's own hard gate
  mandates and the path differed from the one `doctor` prints for the installed tool. A successful
  build without `verbose=true` is still one line. Locked by
  `DotnetRunnerTests.OutputNotes_ForTheServersOwnAssembly_NamesTheProbeCommandForTheBinaryJustBuilt`
  and `…OutputNotes_ForABuildThatWroteNothingElse_NamesNoProbeForAnotherAssembly`. Closes **I215**.

- **A census gate resolves the test names the changelog claims.** `ChangelogReferenceTests` extracts
  every back-ticked test-name-shaped identifier from the two newest `## [` sections — the ones the
  release notes are assembled from — and resolves each against the method declarations of both test
  projects, read from source. A rename that leaves a dead reference behind now fails the build
  instead of being caught by a reviewer, which is how
  `…AddMember_DryRunForAMissingUsing_NamesTheUsingsParameterItWouldNeed` shipped. The discriminator is
  itself covered, so `TERSE_RESULTS_DIRECTORY` and `get_file_outline` are not mistaken for tests, and
  the referenced set is asserted non-empty so the census cannot go vacuous. The exclusion set is
  empty and ratcheted by `EveryExclusionStillNamesSomethingTheChangelogReferences`. Closes **I216**.

- **`doctor` compares the Roslyn this build carries against the one the selected SDK carries**, on a
  new `roslyn:` line, and `FAIL`s when the SDK is ahead. That skew is what silently disabled the
  Razor source generator and every IDE code fix `cleanup fix=style` applies when SDK 10.0.400 reached
  the runners: both are served by the *carried* Roslyn, not the SDK's. Pinning `global.json` fixes
  CI, not users, and until now a user on a 10.0.4xx SDK had only `generator=unavailable` to go on.
  Locked by `InstallCommandE2ETests.Doctor_ComparesTheRoslynThisBuildCarriesAgainstTheOneTheSdkCarries`.

- **`cleanup fix=style` and `fix=all` report `UNAVAILABLE` instead of a confidently clean `no
  change`** when the project registers no IDE code fixer at all. Answering `0 files changed` for a
  fix set that never ran is the confident wrong answer this server exists to refuse; the note names
  the project and points at `doctor`'s `roslyn` line. Locked by
  `CodeFixServiceTests.StyleUnavailable_WhenNoIdeFixerIsRegistered_SaysNothingWasChecked` and
  `…StyleUnavailable_WhenTheFixersAreThereOrTheModeDoesNotUseThem_SaysNothing`. Both close **I217**.

- The shipped `SKILL.md` token budget rises from 18 450 to 19 200 for the behaviours this release
  adds to it. The budget is a ratchet: it moves only in the commit that needs it, with the reason.

- **`workspace_status` reports what the tool surface costs.** A new line reads
  `advertised=88 tools 24192 tokens`, measured on the `tools/list` payload this client actually
  received — after the schema compaction, the profile filter and the markup filter — so it is the
  same number `TokenBudgetE2ETests.TheAdvertisedToolPayload_StaysWithinItsBudget` asserts. Until this
  release the only reader of that number was that test, and three description edits in one session
  each cost a full E2E run to discover they had blown the budget. Locked by
  `TokenBudgetE2ETests.WorkspaceStatus_ReportsTheAdvertisedPayloadTheClientActuallyReceived`, which
  recomputes it client-side rather than matching a literal. Closes **I214**.

- **A rollback caused by a signature change names the declarations that call the member.** When every
  new error of a rolled-back edit is a call-shape error — `CS7036`, `CS1501`, `CS1503`, `CS1729` —
  `EditGate` resolves each error location to the `MemberDeclarationSyntax` containing it and the
  remedy reads `send these callers in the same replace_symbol symbolIds/declarations batch:
  OrderRouter.Retry(Order), OrderRouter.Route(Order), OrderService.SubmitTwice(Order)`. A `dryRun`
  that *would* be rolled back prints the same line under its `WARNING`. The callers are addressable
  ids, so they are fed straight back into `replace_symbol symbolIds=`, which is the edit the tool
  wanted in the first place. Nothing is named when a single error is not a call shape, when a caller
  cannot be resolved, or when more than five distinct declarations call it — an unproven answer costs
  more than none. Locked by
  `EditToolsE2ETests.ReplaceSymbol_RolledBackByASignatureChange_NamesTheDeclarationsThatCallIt`,
  `…ReplaceSymbol_DryRunOfASignatureChange_NamesTheCallersItWouldBreak` and the negative
  `…AddMember_RolledBackForARegressionWithNoImport_NamesNoImportItCannotProve`. Closes **I211**.

- **A complete outline names `contains=` once it is wide enough to need it.** `get_file_outline` and
  `get_type_outline` end with `104 members - narrow with contains=` when nothing was filtered and the
  file or type declares 25 members or more. The `- narrow with` steer used to fire only on a
  *truncation*, and an outline does not truncate: a 49-member type answered ~1 100 tokens with no
  mention of the parameter that would have returned the six members that were wanted. The note is
  silent below the threshold and silent when `contains=` was already passed. Locked by
  `NavigationToolsE2ETests.GetFileOutline_OfAWideFile_NamesTheContainsParameterItNeverTruncatedInto`,
  `…GetFileOutline_OfANarrowFile_NamesNoParameterItDoesNotNeed` and
  `…GetFileOutline_FilteredOnAWideFile_DoesNotSteerToTheParameterItAlreadyUsed`. Closes **I212**.

### Fixed

- **`doctor phases:` was charging a once-per-load compilation to the outline, which is the whole of
  the "per-call latency floor".** `PhaseProbe` timed the *first* semantic call after a cold load, so
  the realization the workspace pays once was billed to `outlineMs`. Measured on this repository's own
  solution before and after separating them: `outlineMs` **2 948.41 ms → 54.50 ms**, with the removed
  cost reappearing where it belongs as `realizeMs=2 748.47`; `resolveMs=0.07 syncMs=0.15` were never
  the problem. `doctor` now prints `realizeMs= outlineMs= gateMs= diffMs=` and its verdict checks the
  two per-call phases instead of a one-off. The corpus reading behind **I219** — `read_text` p50
  3 049 ms on a large solution against `Read` 27 ms — is therefore the realization being re-paid after
  every load and every idle drop, not a per-call floor in the tool; `workspace_status` already reports
  `idle=<n>m compilations=dropped`, and `--idle-minutes` / `TERSE_IDLE_MINUTES` control how often it
  happens. Locked by
  `InstallCommandE2ETests.Doctor_SeparatesTheOncePerLoadCompilationFromThePerCallPhases`. Closes
  **I219**.

- **A qualified name whose type declares no such member no longer blames the name's popularity.**
  `ProbeNames.Probe` answered `ERROR AmbiguousSymbol: name 'ProbeNames.Probe' matches more than 100
  symbols` with the remedy *qualify the name with its containing type* — which is exactly what had
  just been done. `SymbolLookup.ByContainerAsync` had already resolved `ProbeNames` to one type; it
  now answers `ERROR SymbolNotFound: 'ProbeNames.Probe' did not resolve: type 'ProbeNames' declares
  no such member`, with up to five of that type's own members as the remedy, preferring the ones
  whose name contains what was asked for. Two or more types of that name still fall through, because
  the type is then unproven. Locked by
  `NavigationToolsE2ETests.GetSymbolSource_QualifiedByATypeThatDeclaresNoSuchMember_BlamesTheQualifierNotTheName`,
  beside the unchanged bare-name saturation control. Closes **I213**.

- **`global.json` pins the SDK feature band.** `rollForward` was `latestFeature`, which let a
  **10.0.4xx** SDK satisfy a `10.0.300` pin. SDK 10.0.400 reached the GitHub runners on 2026-08-12 and
  broke CI on all three legs — the Razor source generator stopped running (`generator=unavailable`,
  every `RZR###` finding collapsing to `RZR000`) and every IDE code fix `cleanup fix=style` applies
  became a no-op, because both are served by the Roslyn this server *ships*, not the one the SDK
  carries. Falsified against the change set by re-running the previous, green commit's CI unchanged:
  it failed identically. `latestPatch` keeps the 10.0.3xx band the referenced
  `Microsoft.CodeAnalysis` packages match. Logged as **I217**: the underlying lag hits any *user* on a
  10.0.4xx SDK, and pinning CI does not fix that.

## [0.33.0] - 2026-08-12

### Added

- `run_tests` now sets `TERSE_RESULTS_DIRECTORY` on the `dotnet test` process, and `verbose=true`
  returns whatever the run wrote to `$TERSE_RESULTS_DIRECTORY/terse-notes*.txt` under a `run notes:`
  heading, bounded to 20 lines. This is the only channel that survives a **green** run: measured this
  release, a marker written from the test host's own `ProcessExit` — exactly the way `E2ETelemetry`
  wrote its attribution line — never reaches `run_tests`' captured output at any verbosity, because
  the runner captures test-host console output per test. `E2ETelemetry` now writes through the
  channel, so the four numbers it produces (`starts`, `startMs`, `calls`, `callMs`) are readable
  through the tool that runs the suite instead of costing a `dotnet test` shell-out, which is a
  breach. It stays behind `verbose=true`, because the one-line green verdict is a hard gate. Locked by
  `TestToolsE2ETests.RunTests_GreenAndVerbose_ShowsWhatTheTestHostPrinted` and the control
  `…RunTests_GreenWithoutVerbose_HidesWhatTheTestHostPrinted`. Closes **I210**.

- `replace_symbol add=["private static bool Same(…) => …"]` appends **new** members to the type that
  contains the replaced member, inside the same compile-gated edit. A body that calls a helper written
  in the same breath was the single most frequent rollback in this repository's own session log — 35
  `CompileRegression` rejections in one release, 8 in one run — and every one of them cost the same
  three calls: rejected edit, `add_member` for the callee, `retryWith`. It is now **one**. Every target
  of the call must share one containing type (a batch spanning two is refused naming both, rather than
  guessing), a call that replaces that containing type itself is refused, a container that cannot hold
  members — an enum, nested or not — is refused by name instead of being walked past to the enclosing
  class, and — like `usings=` — `add=`
  is not held by a `retryWith` token, so pass it again on the retry. Locked by
  `EditToolsE2ETests.ReplaceSymbol_WithAdd_LandsTheNewHelperInTheSameCompileGatedEdit`,
  `…ReplaceSymbol_WithAdd_WritesBothMembersInOneEdit`,
  `…ReplaceSymbol_WithAddAcrossTwoTypes_IsRefusedRatherThanGuessingTheContainer`,
  `…ReplaceSymbol_WithAddOnTheTypeItself_IsRefused`,
  `…ReplaceSymbol_WithAddOnAnEnumMember_IsRefusedNamingTheEnum`,
  `…ReplaceSymbol_WithAddInsideANestedEnum_IsRefusedInsteadOfAppendingToTheOuterClass` and the control
  `…ReplaceSymbol_WithoutAdd_IsStillRolledBackForTheHelperThatDoesNotExistYet`. Closes **I209**.

- `read_text bytes=true` ends the answer with the file's byte length as `bytes=N` — on every shape the
  tool returns (text, heading map, `section=`, and the outline a whole `.cs` read answers with) and
  once per entry of a `paths=` batch — an **empty** file answers `bytes=0` rather than omitting the
  line, because a parameter that is silently ignored is the confidently-wrong answer this server
  refuses. "How big is this file?" was the last question in this repo's own
  sessions that fell out to the shell: measuring `SKILL.md` across a rewrite cost **4** `Bash` calls
  (`wc -c` ×3 plus `git show HEAD:<path> | wc -c`). It is opt-in because a byte count is noise on the
  reads that want lines. `find_files stamps=true` already answered the same number without reading the
  file, and takes a **concrete path** as readily as a glob — now stated in `SKILL.md` and locked by a
  test, because that is the cheaper of the two calls. Closes **I208**.

### Changed

- **Response format.** A `CompileRegression` whose only new errors are a missing import now names the
  one-call recovery instead of the three-call one. The remedy read
  `add: using System.Collections.Immutable; then replay the rejected text with retryWith`, which is a
  rejected edit plus an `edit_text force=true` on the file header plus a `retryWith`; it now reads
  `retry with usings=["System.Collections.Immutable"] and the retryWith token below, which lands the
  import in the same compile-gated edit`, and the `dryRun` note that used to read `add: using X;` now
  reads `retry with usings=["X"]` — it named a parameter `replace_symbol` also declares, with an
  entirely different meaning, which is the interface-design defect this project refuses to answer with
  prose. The `usings=` parameter it names has existed on
  `replace_symbol_body`, `replace_symbol` and `add_member` since 0.28.0 and was never mentioned by the
  error that most needs it — measured at 2 calls saved per occurrence, on the commonest rollback class
  after callee-after-caller. A `dryRun` that *would* be rolled back names the same parameter without
  a token, because nothing was rejected there and there is nothing to replay. Locked by
  `EditToolsE2ETests.AddMember_RolledBackForAMissingUsing_NamesTheUsingsParameterAndTheRetryToken` and
  `…AddMember_DryRunForAMissingUsing_NamesTheUsingsParameterItWouldNeed`. Closes **I207**.
- Three token ratchets moved by the two new parameters, after the descriptions were cut back twice:
  the advertised `tools/list` payload budget 23 400 → 23 450 (**+26 measured**), the markup-narrowed
  surface 18 100 → 18 200, and `SKILL.md` 18 200 → 18 450. The first drafts cost **238** tokens on
  every request and **468** in every agent's context; what shipped costs 26 and ~250. The budgets are
  regression detectors, so they move only by what a genuinely wider surface costs — and the trimming
  pass is the reason the number is small.

## [0.32.0] - 2026-08-11

### Changed

- The advertised `tools/list` payload no longer carries the MCP SDK's serializer noise: a `"default": null`
  is dropped and a two-arm `["string", "null"]` type union is collapsed to its single real type, including
  inside an array parameter's `items`. Measured over the 87-tool surface, the payload fell from **23 613 to
  22 193 tokens (-1 420, -6.0 %)** on every request of every session, with no description reworded.
  Non-null defaults (`false`, `0`, `600`) are kept, and every parameter still binds an explicit `null`.
  Locked by `SchemaCensusE2ETests.NoAdvertisedSchema_CarriesANullDefaultOrANullTypeArm` and
  `TokenBudgetE2ETests.TheAdvertisedToolPayload_StaysWithinItsBudget` (I200).
- **BREAKING — the advertised tool set is now derived from the loaded workspace's own file kinds.**
  A solution holding no `.xaml`/`.axaml` no longer advertises the 13 `xaml_*` tools, one holding no
  `.razor`/`.cshtml` no longer advertises the 10 `razor_*`, and one holding no `.resx`/`.resw` no
  longer advertises the 8 `resx_*`. Measured on `fixtures/SelectionSolution`: **56 tools and 16 962
  tokens instead of 87 and 22 193 (-23.6 %)** on every request. Loading a second solution that does
  hold them re-advertises those families and sends `notifications/tools/list_changed`; a hidden tool
  still answers when called by name, and `workspace_status` prints `tools=<families> hidden`.
  `--tools all` / `TERSE_TOOLS=all` opts out and advertises everything, `--tools core` is unchanged,
  and an unrecognized `--tools` value now falls back to the derived surface rather than to everything.
  Locked by `MarkupProfileE2ETests` (I201).
- **`SKILL.md` enumerates the tool surface once, not twice.** The swap table and *The whole surface,
  by job* are merged into one job-keyed table carrying the replaced built-in as a column, so every
  tool appears in exactly one row; what is left of the second section is the behaviour a table cannot
  carry. The predicted 3 500-4 000 token saving is **refuted by measurement** — the two sections' bulk
  was non-duplicated behavioural prose, not a repeated enumeration, so the merge is size-neutral
  (68 971 -> 68 999 bytes, of which I201 added ~330). The durable value is the single enumeration plus
  two new census gates: `DocsCoverageE2ETests.TheShippedSkill_StaysWithinItsTokenBudget` caps the file
  at 17 600 tokens and `…EnumeratesEveryToolExactlyOnceInOneTable` fails when a tool no table row
  names (I202).

### Added

- **`read_text paths=[...]`** — up to 10 files in one response, each under its own path line with its
  own count and continuation note; a path that does not resolve is reported inline as `NOT_FOUND`
  instead of failing the call, and `maxChars` is a budget shared across the batch that names the entry
  it clipped. `path` is now optional and combines with `paths`, taken first. Measured over 1 538
  transcripts, `read_text` cost **667** adjacent same-tool round trips, 82.5 % of them independent and
  78 % spanning more than one file (I192).
- **`edit_text` entries may carry their own `path`** — one call now edits several files, grouped by
  file, one write and one answer line per file, at most 10 entries per file and 25 in total. Half of
  the 946 measured `edit_text` round-trip pairs spanned more than one file and could not be fused by
  the same-file `edits=` that already shipped (I193).
- **`write_text files=[{path,content}]`** — up to 10 files in one call, and **every `.cs` document
  among them goes through one compile gate**, so a type and the consumer it breaks land together
  instead of the first write being rolled back on its own. `path` is now optional; passing `files`
  together with a top-level `path`, `content` or `delete` is refused rather than silently dropping one
  (I194).
- **`run_tests projects=[...]`** — several test projects in one call, sharing one results directory and
  answering one merged verdict line. The timeout is now **per project** rather than a budget the batch
  shares, and any project that timed out is named in a `WARNING` instead of the merged run being
  reported as passed. At most 10 projects per call, refused by name, and the batch stops at the first
  timeout and names every project that produced no results - a single-invocation run says
  `this run timed out and produced no results` rather than claiming a batch it never had.
  `changed=true` is bounded the same way:
  when the change reaches more than 10 test projects it runs the whole solution once and says so,
  rather than paying a per-project timeout over an unbounded list (I195).
- **`diff_text paths=[...]` and `get_file_outline paths=[...]`** — both were measured 100 % multi-file.
  `diff_text` hands every entry to the same `git diff` as its own pathspec; `get_file_outline` renders
  one group per path and reports an unresolved path inline as `NOT_FOUND` (I196).
- **`get_file_outline contains=` and `get_type_outline contains=`** — keep only the members whose name
  matches, printed under their declaring type with an `N of M members` line so the omission is never
  silent (I206).
- **A same-tool repeat steer.** From the **third** consecutive call of one tool, the response gains one
  line — `3 read_text calls in a row - pass paths=[...] for the rest` — naming the plural parameter
  that tool actually declares, and nothing when it declares none or when the call already used it.
  The counter resets on any different tool. Census-gated by
  `SchemaCensusE2ETests.EveryToolWithAPluralParameter_IsKnownToTheRepeatSteerAndNoOtherIs`, which
  discovers the plural set from `tools/list` in both directions. This is the **single documented
  exception** to the one-line-success rule: a green `run_tests`, a `write_text` short form and an
  `edit_text` one-liner can now carry a second line, and `README.md`, `NUGET_README.md` and `SKILL.md`
  all say so. Because the line depends on call history, two responses of the same tool are no longer
  byte-comparable: the E2E harness's `CallAsync` strips it and `CallRawAsync` is what the three tests
  that assert the steer itself use, so no other test can become order-dependent on it — two did, and
  only on the macOS and ubuntu legs, where the test order differs (I198).
- **`doctor` prints the running server's assembly path and the one-shot probe command**, and `SKILL.md`
  now teaches that a claim about tool *behaviour* is proven against a freshly built `terse.dll` with
  `dotnet <path> call <tool> --workspace <solution> --json '{...}'` — 3 s against 13 s for the
  narrowest filtered E2E run (I205).
- **The E2E suite attributes its own wall clock.** `TerseServerProcess` counts server starts, start
  time, tool calls and call time, and reports them on process exit and from
  `BacklogClosureE2ETests.TheSuiteAttributesItsOwnWallClockToServerStartsAndToolCalls`, so the 884 s
  that `TerseSharp.E2ETests` costs is measured before anything is optimized (I186).

### Fixed

- **A `typeSymbolId` now resolves against types only.** `add_member`, `extract_interface`,
  `move_type_to_file` and `move_type_to_namespace` take a *containing type*, so a name that matches one
  type and five properties no longer answers `AmbiguousSymbol`; a name that matches no type at all says
  so and counts the non-type matches instead of hiding them (I204).
- **`replace_symbol` no longer re-indents a declaration read back from `get_symbol_source`.** The read
  dedents, the write landed the dedented text, and the continuation lines of an expression-bodied
  member ended up shifted left — invisible to `format`, to `analyze` and to the build, and caught only
  by a human reading the diff. The declaration text is now re-indented to the target's column before it
  is parsed, with the interior of every multi-line literal left untouched — in **every** member of a
  multi-member declaration, because the skip-set is built by tokenizing the whole declaration rather
  than only its first member (I188).
- **`terse call --json` binds camelCase arguments.** The one-shot probe deserialized with default,
  case-sensitive options, so `{"files":[{"path":…,"content":…}]}` — the casing the advertised schema
  and every example use — answered `'files' entry 1 carries no path` on a call that was correct.
- **`doctor` names the right assembly on Linux and macOS, and prints a probe command that runs.** The
  host check compared against `dotnet.exe`, so on any non-Windows runner it printed the muxer's path;
  and the command was prefixed with `dotnet` unconditionally, so under the apphost - which is what a
  `dotnet tool install -g` shim is, on every OS - the printed command died with
  `BadImageFormatException`. `InstallCommandE2ETests` now drives `doctor` through the apphost as well
  as through the muxer.

## [0.31.0] - 2026-08-11

> **Response formats changed.** `get_symbol_source` on a **type** id now answers `get_type_outline`'s
> payload plus a steer instead of the type's source (`verbose=true` restores it), `search_symbols`
> returns the production half plus one `N more in test projects - scope=test` line where it used to
> interleave both halves, and `workspace_status` gains a trailing `terse=<version>` line. An agent
> that parsed any of those three sees a different shape.

### Added

- **Every non-mutating tool declares `readOnlyHint`, and every deleting tool declares
  `destructiveHint`.** All 87 `[McpServerTool]` attributes carried no MCP annotation, and Claude Code
  gates parallel dispatch of MCP tools on `readOnlyHint`: a tool that omits it defaults to `false` and
  is dispatched serially. 46 read tools now carry `ReadOnly = true` and 9 deleting tools carry
  `Destructive = true`. `build`, `run_tests`, `rerun_failed`, `list_tests` and `gate` are deliberately
  **not** read-only — they run a build, and a build dispatched concurrently with an edit is a race,
  not a saving; `analyze` is not read-only either, because it records a per-scope diagnostic baseline
  that `sinceLast=true` reads back, so two parallel calls of the same scope would race it.
  Census-gated in both directions by
  `SchemaCensusE2ETests.EveryAdvertisedTool_IsClassifiedAndCarriesTheAnnotationItsClassDeclares`,
  which discovers every tool from `tools/list` and fails until it is classified read-only, destructive
  or mutating in `ToolCensus`. (I191)
- **`replace_symbol_body`, `replace_symbol` and `add_member` take `usings=`** — namespaces added to
  the file's using block, sorted System-first, inside the **same** compile-gated edit as the
  declaration. The commonest rollback class in this repo's own logs is a new body naming a type the
  file does not import: it used to cost a rejected edit, an `edit_text force=true` on the header and a
  `retryWith`. It is now one call. (I181)
- **`clean` takes `path=`** — a `.slnx`, `.sln`, `.slnf` or project file that is **not** loaded, swept
  from its own directory. Reproducing a cold build over a fixture no longer needs a `load_workspace`
  that makes every later un-hinted call ambiguous, and no longer needs the `rm -rf` this repo's own
  sessions kept falling back to. (I182)
- **`workspace_status` ends with `terse=<version>`, and `doctor` opens with a `version:` line.** The
  running binary is the installed tool, never the working tree, and until now nothing in the tool
  surface said which version answered — three documented sessions were spent arguing with a binary
  three releases old. (I183)

### Changed

- **`solution_projects` reads a `.slnf` solution filter.** The filter's `solution.projects[]` is
  parsed with `JsonDocument`; a `.slnf` whose JSON is malformed is refused by name with a remedy
  rather than answering `0 projects`. Each project is resolved against the directory of the filter's
  own `solution.path` and normalized to `/`, because a `.slnf` stores Windows-separated text and
  `Path.GetDirectoryName` is host-shaped: without that, the ubuntu and macOS legs answered a path
  that does not exist. `.slnf` is a solution file to `IsSolutionFile` again, so the capability the
  `path=` row claimed is real on all four advertised surfaces. (I190)
- **`search_symbols` answers the production half first and folds the test half to one line.** A common
  name used to return 21 records of which 20 were test declarations. The default now ranks `src` above
  `test`, keeps the production declarations and appends `N more in test projects - scope=test`; when
  only test projects match, they are still returned in full, and `scope=` is unchanged. Nothing is
  hidden silently — the count is always named. (I185)
- **`get_symbol_source` on a *type* id answers `get_type_outline`'s payload plus a steer**, instead of
  the whole class's source: `symbolIds=["ProjectTools"]` returned 176 lines where 15 were wanted.
  `verbose=true` opts back into the source, and a member id is unchanged. (I180)
- **A missing path is answered, not just refused.** `get_file_outline` and `read_text` on a path named
  after a type the workspace declares elsewhere now name the declaring file
  (`'Errors' is declared in src/TerseSharp.Core/Result.cs`), and `add_member path=` on a `.cs` file
  that does not exist yet names `write_text path=… force=true`. Neither sends the caller to
  `find_files`, which cannot find a type that does not name its file. (I184, I187)
- **Every plural parameter names itself imperatively in its tool description.** The position and mood
  of the mention measurably predicts adoption — `symbolIds`, named as an imperative in sentence two,
  reached 48 % of `get_symbol_source` calls, while `queries`, named as a statement of fact, sat at
  2.6 %. `get_symbol_source`, `search_text`, `search_regex` and `replace_symbol` now all read
  "Pass X to … . Replaces one call per …", census-gated by
  `SchemaCensusE2ETests.EveryToolWithAPluralParameter_NamesItImperativelyInItsDescription`. (I197)

### Fixed

- **`search_text`/`search_regex` `queries=[...]` no longer swallow the lines a multi-line entry
  spans.** The scan advanced past the end of the *match*, so the other entries' hits on the lines it
  covered were never emitted and the count line was short by them. It now resumes at the end of the
  line the match **starts** on, and a multi-line entry is reported once, at that line. The
  "keep the entries line-local" caveat is removed from all six surfaces. (I189)

## [0.30.0] - 2026-08-10

### Added

- **`search_text` and `search_regex` take `queries=[...]`** — up to 10 literals or expressions
  answered in **one** pass over the same file set, every record tagged `q1`..`qN` by the position of
  its query in the array. `query` and `queries` combine, `query` first; an 11th entry is refused
  naming the cap rather than truncated, and a blank entry is refused rather than matching everything.
  This closes the last question a shell `grep` with an alternation still answered better: an
  alternation returns one undifferentiated list, so the caller cannot tell which alternative produced
  which record — the measured symptom was a single `search_text` for a shared id prefix answering
  `60/82 matches truncated` with every record carrying the identical matched text, and a
  `Bash: grep -n` fallback for the eight ids that were actually wanted (**I175**).
  **No legend is echoed back**: `q1` is `queries[0]`, which the caller passed, so printing the map
  would be ceremony. A single-entry `queries` answer is byte-identical to the `query` form.
  The count line keeps its meaning — *matching lines, at most one per line* — so **a line matching
  several queries is one record carrying every matching tag**, comma-separated in query order
  (`q1,q3`). Without that, a tag missing from a shared line would read as "this literal is absent
  here", which is the false negative the review round caught before release.
  **Known limitation, documented on every surface:** keep the entries line-local. An entry that can
  match across a line break — a literal containing a newline, or `[\s\S]` / `(?s).` in a regex —
  consumes the lines its match spanned, so the other entries' hits on those lines are not reported
  and the count is short by them. Making the multi-query scan advance per line is a behaviour change
  and is tracked as an open backlog row rather than shipped unreviewed in this release.
- `RepeatQueryLatencyE2ETests`' whole-repository case warms up for three calls instead of two and
  takes six samples instead of five. A cold two-core Windows runner measured
  `11354, 4444, 6515, 86, 22` ms: the property the test exists to prove holds decisively — the
  settled calls are 86 ms and 22 ms against a 11 354 ms first call — but the third call is still
  inside the compilation-realization tail on a 4-project solution, which the one-project fixture
  never sees. The budget was widened where it was wrong rather than the run repeated until it passed.

- **`fixtures/UnloadableSolution`** — one project that loads and one the solution names but that does
  not exist — and `LoadFailureE2ETests`, so the load-failure rendering has a fixture that can actually
  fail it. No existing fixture reports a load failure: `BrokenSolution` loads cleanly and is broken at
  *compile* time.
- **`fixtures/SelectionSolution`** — one source project and two test projects, only one of which
  references it — and `ChangedTestSelectionE2ETests`. The **positive** path of `run_tests changed=true`,
  a selective run that genuinely skips a test project, had no end-to-end coverage: `FixtureSolution.slnx`
  holds one project and does not include `Fixture.Trading.Tests`, so every fixture selection fell back
  and the behaviour was covered only by five unit tests over a synthetic `AdhocWorkspace`. The new test
  edits the source project, runs `changed=true`, and asserts `total=1` of the fixture's two tests with
  the skipped project named; its control asserts `total=2` and the stated fallback reason when nothing
  changed. Adding the second test project to `FixtureSolution.slnx` instead was rejected: it moves
  `projects=1` to `projects=2` in five checked-in assertions plus the token budgets (**I173**).
- **`terse doctor` prints a `phases:` line** — `widest=<file> outlineMs= gateMs= diffMs=` — timing a
  **real** `get_file_outline`, a **real** `EditGate` dry-run compile gate and a **real** `git diff`
  spawn against the widest `.cs` document of the loaded solution. I161's `latency:` line measured
  workspace resolve+sync at **0.21 ms**, four orders of magnitude below the ~1.1 s per-call floor the
  transcripts show, and the transcript minima split cleanly on whether a tool does Roslyn or
  child-process work (`workspace_status` 9 ms and `find_files` 31 ms against `read_text` 1 118 ms,
  `get_file_outline` 1 214 ms, `edit_text` 1 269 ms, `diff_text` 1 292 ms). The new line attributes
  the remaining floor to the phase that actually holds it, so the next optimization is aimed rather
  than guessed (**I172**).
- **`solution_projects` takes `path=`** — a `.slnx` or `.sln` read directly, loaded or not.
  "Which projects does this solution contain?" had no cheap answer for an unloaded solution:
  establishing that `FixtureSolution.slnx` holds **one** project — and therefore that a
  just-written E2E test could never pass — took a failing E2E test, a failing unit test and a
  throwaway diagnostic test. Loading the fixture as a second workspace is the alternative, and it
  makes every un-hinted call in the session ambiguous. A path that is not a solution file, or that
  does not exist, is refused with a remedy rather than answering `0 projects` — including a `.slnf`
  solution filter, which is JSON and is not parsed yet, so it is refused by name instead of silently
  answering `0 projects`. `list_projects`
  now names this tool for the unloaded case (**I178**). A relative `path=` is resolved against the
  server's working directory rather than a workspace, so the answer **names the file it actually
  read** — with the header suppressed on a condensed success, a caller could otherwise not tell which
  of two same-named solutions answered. The echo reads `read  <path>`, not `outside-workspace`: the
  tool performs no containment check and a fixture solution normally lives *inside* the repository,
  so the tag would have been a claim the response cannot prove.
- `SolutionFile` reads the solution asynchronously — `XDocument.LoadAsync` over an
  `Asynchronous | SequentialScan` stream and `File.ReadAllLinesAsync` in place of `XDocument.Load`
  and `File.ReadLines`, with the cancellation token threaded from `solution_projects`,
  `solution_add_project` and `solution_remove_project`.

### Fixed

- **A qualified name whose member name saturated was refused, although the qualifier was exactly the
  narrowing information needed.** `get_symbol_source symbolId=DotnetRunner.Report` answered
  `name 'DotnetRunner.Report' matches more than 100 symbols, so it cannot be resolved safely` — for a
  private method of a named class — because the saturation cap was applied to the raw member-name
  search before the containing type was consulted, and the remedy then advised qualifying a name that
  already was. `Type.Member` now falls back to the members of the types called `Type`, which cannot
  saturate on a normal type name. A **bare** name that saturates is still refused, and the fallback
  itself refuses rather than resolving from a truncated set when the *type* name saturates too
  (**I176**).
- **The load-time MSBuild failures and warnings were absolute** — 36 876 and 17 643 characters in a
  measured week. `workspace_status verbose=true` and `load_workspace verbose=true` now print each
  message with the workspace root stripped, and an unattributed failure's group key is stripped too,
  so a failing project's path is re-usable as an argument like every other path in a response. I163
  relativised the build and test passthrough and stopped here because `LoadFailureSummary.Group`
  derives the project name from these strings: the stripping happens **after** grouping, and
  `Group_AttributesTheSameProject_WhetherTheMessageIsAbsoluteOrAlreadyRelativised` records what the
  check found — the parser reads the quoted `*proj` file name and is indifferent to the path shape,
  so the ordering is belt-and-braces rather than load-bearing (**I174**). The stripping matches on a
  **path boundary**: a root of `…/repo` leaves `…/repo2/App.csproj` alone instead of rewriting it to
  `2/App.csproj`, which a plain `string.Replace` did and which the review round caught.
- **`changedLines` was up to 27x too high on a large file.** A two-entry `edit_text` batch against
  `CHANGELOG.md` reported `changedLines=2426` for a change git measures at `+89 -1`. Two edits far
  apart leave a middle that the common prefix/suffix strip cannot shorten, its alignment matrix
  exceeds the 2 000 000-cell ceiling, and the whole middle was then emitted as **one** block — so
  both the count and the `verbose=true` hunk were wrong. Above the ceiling the diff now splits
  recursively on a line that occurs exactly once on each side, nearest the middle, and aligns each
  half; only a region with no such anchor, or more than 24 levels of splitting, still falls back to
  the single block. Measured on a 3 000-line file with two one-line edits 2 495 lines apart:
  **2 496 → 2**, and the diff carries two small hunks instead of one 2 496-line hunk. Under the
  condensed success response that count is the *only* evidence the caller gets, which is why a wrong
  one is a confident wrong answer (**I179**).
- **A rollback whose only fault is a missing using directive now names the namespace.** When every
  new error is `CS0246` or `CS0103` for a name the changed project resolves in exactly **one**
  namespace, the `remedy:` reads `add: using System.Collections.Immutable; then replay the rejected
  text with retryWith` (up to three namespaces, sorted). The candidate name is taken from the
  diagnostic and then **verified** against the project's own declarations, so a name that resolves
  nowhere, in two namespaces, or alongside a genuine regression produces no hint rather than a wrong
  one. The using is never added automatically: it edits a region the caller did not address, and an
  ambiguous type would make it a guess (**I177**).

## [0.29.0] - 2026-08-10

### Added

- `analyze changed=true` - the end-of-task shape - now names `gate` as the one-call form of the
  four-call sweep it belongs to. `gate` shipped in 0.27.0 to collapse
  `analyze` -> `format` -> `cleanup fix=all` -> `analyze`, and a one-week session scan then measured
  **356 `analyze` calls and zero `gate` calls**: the tool existed and was never discovered. The skill's
  Analyse section now leads with `gate` instead of listing it last (**I165**).
- `SKILL.md` states the parallel-call rule: independent tool calls belong in one assistant message,
  with the dependency carve-out spelled out. The same scan carried **17 567 tool calls and not one
  parallel message**, while 5 989 sat in runs of three or more consecutive calls of the same tool
  (**I166**).

- `terse doctor` gains a **`latency`** line: it runs a 20-call loop over the real request path and
  splits the per-call floor into `resolveMs` / `syncMs` / `actionMs`, so a latency claim is
  attributed before anything is optimized. It fails above a 100 ms resolve+sync floor.
  Measured on the freshly built 0.28.1-pre binary: `resolveMs=0.07 syncMs=0.14 actionMs=0.00` -
  **0.21 ms**, flat between a 1-project fixture and this 4-project / 308-document solution. That
  **refutes** the standing hypothesis behind the 1.1 s per-call floor seen in session transcripts:
  it is not `ResolveAsync` and not the `WorkspaceSync` drain, which short-circuits on an empty
  pending set (**I161**).

- `WorkspaceRegistryTests`' drop-counting tests pin the managed-heap figure they pass to
  `DropIdleCompilations` instead of inheriting the process's. `Releasable` swaps the caller's idle
  window for a **one-minute** minimum once the managed heap passes 2 GB, so a test asserting that a
  1-tick window drops exactly one workspace silently depended on how much memory every other test in
  the run happened to be holding. It went red on the macOS CI leg only, after this release added 29
  unit tests. The pressure semantics themselves are unchanged and still covered by their own test.
- `run_tests changed=true` shares **one** deadline across the projects it selects. `timeoutSeconds`
  is documented per call; running each selected project with the full timeout silently multiplied the
  ceiling by the number of projects, so eight projects turned a 600 s budget into 80 minutes. The
  loop now subtracts the elapsed time and stops when the budget is spent.
- **The guard routes instead of only refusing.** A `PreToolUse` denial now also returns
  `hookSpecificOutput.additionalContext` carrying the **complete replacement call, with the arguments
  filled in from the denied command** - `Call this instead: get_file_outline path="src/App/Order.cs"`,
  `changed_files`, `run_tests` - which Claude Code places beside the tool result. A
  `permissionDecisionReason` string alone is the weakest documented lever: negated instructions
  inverse-scale with model size (arXiv:2209.12711), adherence to an earlier-turn instruction decays
  0.877 -> 0.707 over three turns (arXiv:2410.15553), and restating it recovers only 15-20 %
  (arXiv:2505.06120) - measured here as 2 278 shell-text `Bash` calls surviving a guard that was
  installed and current all week. The routing is chosen from the file kind, so a `.xaml` read is
  routed to `xaml_outline` and a `.resx` read to `resx_get`, never to `get_file_outline`. The routing
  dispatches on the **file kind**, the same axis the deny reason already used: an earlier revision
  dispatched on the built-in's name, which made a `Grep` scoped to XAML route to `search_symbols`
  while its own reason named `xaml_find`, and made a denied `cat Foo.cs` route to
  `search_text query="cat Foo.cs"` - a call that returns nothing. Caught by the review round and
  covered by a test asserting the routing never contradicts the reason it ships with (**I170**).
- **`TERSE_GUARD_LOG=<path>` appends one JSON line per guard decision** - tool, verdict, routing,
  reason, `cwd`, `session_id` and `transcript_path`. Whether the 2 278 shell-text calls were denied
  and retried, allowed as out-of-workspace, never matched, or issued by a subagent that never saw the
  parent's `CLAUDE.md` is **not knowable from a transcript**, and each cause needs a different fix.
  The session and transcript fields are what split subagent traffic from the main thread. Opt-in and
  best-effort: a malformed payload still produces one line, and a write failure never changes the
  verdict (**I168**).
- `run_tests` and `list_tests` relativise the child-process output they pass through. A failed test
  run or listing rendered its error lines and its output tail with `root` hard-coded to
  `string.Empty`, so every MSBuild and VSTest path came back absolute - part of the 227 835
  characters of absolute path measured across `build`, `run_tests`, `workspace_status` and
  `load_workspace` in one week. Those two paths now carry the workspace root, so a failure's paths
  are workspace-relative and re-usable as arguments like every other record. The load-time MSBuild
  diagnostics of `workspace_status` and `load_workspace` are **not** covered - they are grouped by a
  parser that reads the absolute path - and are tracked as **I174** (**I163**).
- **`changed_files root=` and `diff_text root=`** answer about any absolute directory instead of the
  loaded workspace - a sibling worktree, another repository - tagged `outside-workspace`, exactly as
  `search_text root=` already worked. One week of sessions carried **420 `git` calls**, 112 of them
  `git -C <another worktree>`, precisely because the working tree of an unloaded directory had no
  tool. Loading each worktree is not the answer: `load_workspace` was measured at p50 52 s and about
  3 GB resident. `diff_symbols` deliberately does **not** take `root=` - mapping a hunk onto a
  declaration needs that directory's Roslyn compilation - and refuses by naming the two tools that
  can answer. The guard's `git status` and `git diff` deny reasons now name the `root=` form, so the
  denial finally carries a replacement for a directory that is not loaded (**I167**).
- **`run_tests changed=true`** runs only the test projects that transitively reference a project
  changed since the workspace loaded, and names both the projects it ran and the ones it skipped.
  `run_tests` was **15.99 h of the 34.5 h** of terse wall clock in a one-week scan - 751 calls,
  p50 34 433 ms - and 101 of those ran the whole solution with no filter. Selection is at
  **assembly** granularity, the level Microsoft ships TIA at, because static per-symbol selection is
  measured unsafe under reflection. It is **opt-in and fail-safe**: it falls back to the whole
  solution, naming the reason, when no document changed, when a changed file belongs to no project,
  or when no test project depends on the change - so it never silently runs less than it should, and
  the skipped set is always named. `project=` still outranks it. Each selected project runs into one
  shared TRX directory, so the existing per-project breakdown merges unchanged (**I164**).

### Changed

- **The `core` tool profile stays opt-in, and now says why.** Making it the default was implemented
  and then **reverted inside this release** on the evidence of a new census gate,
  `GuardProfileCoherenceE2ETests`: the guard names **33** tools the `core` subset does not advertise
  - every `xaml_*`, `resx_*` and `razor_*`, plus `cleanup`, `format`, `clean`, `gate`, `list_tests`,
  `rerun_failed`, `diff_text`, `rename_symbol` and `get_symbol`. The claim that a hidden tool "still
  answers when called by name" is true of the **server** and unproven of the **client**: an agent can
  only call what its tool list carries. Defaulting to `core` would therefore have made every guard
  denial on a XAML, Blazor or localized codebase point at a tool the agent cannot reach - the
  failure `CLAUDE.md` describes as "a guard that denies a command the server cannot answer", with the
  arrow reversed. The README, NUGET README and `SKILL.md` now state that limitation instead of
  claiming the profile hides nothing. Kept from the attempt: `ToolProfile.Resolve` takes the
  environment value as an explicit parameter, so 9 unit tests cover profile resolution without
  mutating process state; the E2E census fixture and `BuildWarningsE2ETests` start with
  `--tools all`; and `ToolListDiscoveryCensusTests` fails any E2E class that reads `tools/list` from
  a server it spawned without it - which immediately caught `BuildWarningsE2ETests`, whose build/test
  family had already shrunk from four tools to two (**I160**).

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `search_text` and `search_regex` tag the response `HEURISTIC` once instead of every record.

- `search_text` and `search_regex` are documented to answer nothing but `HEURISTIC`, yet stamped
  `  HEURISTIC  ` into **every** record - 13 629 occurrences across 1 151 calls in a one-week
  session scan, on a tag that can only take one value. The tag is now a single note line and each
  record is `path:line  <payload>`. A tag the caller cannot act on is paid for and not acted on:
  the closest measured analogue, CrAM (arXiv:2406.11497), finds prompt-borne credibility labels
  yield only marginal gains and sometimes a drop. `EXACT`/`HEURISTIC` on the tools that genuinely
  answer both - `find_usages`, `xaml_*`, `resx_*`, `diff_symbols` - is unchanged (**I169**).

### Fixed

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `gate`'s `analyzed=N` counts the documents in scope, not the diagnostics found before the fix steps.

- `gate` bound `analyzed=` to the **pre-fix diagnostic count**, so every clean scope answered
  `clean  analyzed=0 fixed=0 remaining=0` - byte-identical to a gate that had run over nothing, and
  read as exactly that twice in one session scan, including on an explicit `path=` naming a file
  `analyze` was answering about at the same moment. `analyzed=` now counts the documents the gate
  had in scope, so `analyzed=0` is unreachable and a clean verdict can never be mistaken for an
  empty run. The scoping half of the report is refuted and recorded: an explicit `path=` **was**
  gated, and a scope matching no document already answers `ERROR DocumentNotFound` /
  `ERROR Invalid` with a remedy rather than a verdict - now locked by its own test (**I171**).

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `read_text` no longer calls a read `truncated` that it never clipped.

- `read_text` counted every bounded read against the **file's** line total, so a caller-chosen
  `startLine`/`endLine`, a `tail`, and a complete `section=` were all labelled
  `N/<file total> lines truncated` although nothing had been cut - measured on 1 771 of 2 320
  responses (76.3 %) in a one-week session scan, and on the shipped 0.28.0 an explicit
  `startLine=1 endLine=3` over a 237-line file answered `3/237 lines truncated`. The summary now
  counts against the lines that arrived when nothing was clipped, and against the file total only
  when the read was genuinely cut - so an unclipped read answers `3 lines`, and a clipped one still
  answers `2/237 lines truncated`. The file total stays available on the
  `next: startLine=N (total=T)` and past-the-end notes. This restores the invariant `SKILL.md`
  already documented - a truncation count is always real, and a read your own range ended says
  nothing (**I162**).

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `changedLines` counts the lines that changed rather than the span between the first and the last
> change, and a diff is one `@@` hunk per change instead of one hunk spanning them all - so every
> mutating tool's count and every `dryRun`/`verbose` diff is both more accurate and much smaller. A
> `CompileRegression` names each rejected diagnostic's file **workspace-relative**, and `search_regex`
> no longer emits a record whose payload is empty. The tool surface goes from 86 to **87**: `gate`
> runs the mandated `analyze` -> `format` -> `cleanup` -> `analyze` sequence as one call and answers
> one verdict line - `clean` or `FAILED`, never a bare success line over a step that had something to
> say.

### Added

- **`gate` runs the end-of-task quality sequence as one call.** `analyze` at `info`, `format`,
  `cleanup fix=all`, then `analyze` again — the order this repo's own `CLAUDE.md` mandates — over the
  files changed since the workspace loaded, answering **one verdict line**
  (`clean  analyzed=N fixed=M remaining=0`) and, when it is not clean, only the diagnostics still
  unfixed. Never a diff. `path=` scopes it, `solution=true` gates every document, `dryRun=true` makes
  both write steps verify instead of write, and `verbose=true` adds each step's own report. Measured
  over one task: those four tools were called 6 times to gate 13 changes, always in that order —
  3 of every 4 calls on the end-of-task path. It deliberately does **not** fuse in `build` and
  `run_tests`: a test result read before its build is the previous binary's. The tool surface goes
  from 86 to **87**.
  Its verdict is `clean` or `FAILED` and it condenses to that one line **only** when every step was
  genuinely quiet: a `VERIFY_FAILED`, an `UNFIXED`, a rolled-back step or a file the run rewrote all
  keep the full report, because `FormatService` returns a failed verify inside a successful
  `Result` and a bare `clean` over one would be the confident wrong answer this project exists to
  prevent. `dryRun=true` reports `FAILED` when the tree would change - that is what a pre-push check
  is for. (I148)
- **`search_symbols` takes `scope=src` and `scope=test`**, keeping only the projects of that half
  (`TestScope`, the same src/test split `find_usages` already tags each usage with). Measured on this
  repo: `search_symbols query=Same kind=method` returned 28 declarations of which 26 were test
  methods, burying the one production `PathBoundary.SameFile` the question was about. An unknown
  value is refused naming the two it accepts, rather than silently searching everything. (I155)
- **`search_text` and `search_regex` take `matchesOnly=true`**, printing the matched span instead of
  the whole line the way `grep -o` does, and composing with `unique=true` to answer "which distinct
  values of this shape exist". Two calls in one session fell back to `Bash: grep -o` over an XML in
  the NuGet cache because a whole-line response returned ~40 lines to read 13 names. A match that is
  only whitespace still prints its line, so no record is ever empty. (I151)

### Fixed

- **A `CompileRegression` names each rejected diagnostic's file workspace-relative.** It printed the
  absolute path — `CS0103 C:\Users\...\src\TerseSharp.Core\GateHost.cs: ...` — while every other
  record in the surface goes through `PositionFormat.Relative`, so the one payload an agent reads on
  a failed edit was also the one it could not re-use as an argument. The same key backs the
  `dryRun` "would be rolled back" list and the `UNRESOLVED` list, so all three are relative now.
  (I147)
- **`search_regex` no longer emits a record with an empty payload.** A pattern that can start on the
  blank line above its content — `^\s*(public|internal|private)\s+…` — matched at the blank line,
  and the scanner then advanced past the line holding the match's *start*, so the same declaration
  was reported twice: once as `path:NN  HEURISTIC  ` with nothing after the tag, once for real. The
  matcher now returns the match's length, a hit is reported at the first line carrying its text, and
  the scan resumes after the line the match *ends* on. Measured on this repo's fixture: 66 records
  became 33, and half the response was the empty half. (I149)
- **An argument that fails to bind names the array parameter and quotes the offending text.**
  `replace_symbol symbolIds=[…] declarations=[…]` answered
  `JsonException: … Path: $ | LineNumber: 0 | BytePositionInLine: 9079` and then listed every accepted
  parameter — locating nothing in a 9 079-character argument. An `InvalidArgument` carrying a
  `JsonException` with a byte position now also names each array-schema parameter that was supplied
  and quotes the ~80 characters around that offset. (I150)
- **A `retryWith` token is bound to the workspace its edit was rejected in.** The token recorded no
  root, so replaying it with a different `workspace=` replayed the held declaration into the other
  worktree — and this tree keeps whole copies under `.claude/worktrees/agent-*`, where the same symbol
  id resolves in both. A replay that resolves elsewhere is now refused, naming both roots, before the
  symbol is looked up. Raised in the 0.27.0 review as a NIT and deferred, not refuted. (I152)
- **`terse guard` names the exact `cleanup` that verifies what a `dotnet format` sub-command checks.**
  `dotnet format analyzers` and `dotnet format style` were already denied, but with the generic
  "use format, cleanup fix=all, or cleanup verify=true" message — so the agent had to guess which mode
  matches, and `fix=all` is a superset that names files CI accepts. They now answer
  `cleanup fix=analyzers` / `cleanup verify=true fix=analyzers` and `cleanup fix=style` /
  `cleanup verify=true fix=style`, which verify **exactly** what those two commands check; a bare
  `dotnet format` still names the whitespace-and-cleanup pair.
- **`changedLines` counts the lines that changed, not the span between the first and last change,
  and a diff is now one hunk per change instead of one hunk spanning them all.** `UnifiedDiff` had no
  alignment step: it bracketed the edit with the common prefix and suffix and called everything
  between them changed. A three-line `edit_text edits=[...]` over `README.md` answered
  `changedLines=125` where git reports `+3 -3`, and `verbose=true` on it would have printed all 125
  lines as removed and all 125 as added. Line alignment is now a real LCS over the bracketed region
  (falling back to the old single block above 2 000 000 cells, so memory stays bounded on a huge
  rewrite), `changedLines` sums `max(removed, added)` per block, and `Between` emits one
  `@@ -a,b +c,d @@` hunk per block. This is a **response-format change**: every mutating tool's
  `changedLines`, and every `dryRun`/`verbose` diff, gets more accurate and much smaller. (I154)
- **`terse guard` no longer denies a git command aimed at a tree TerseSharp cannot answer for.**
  `git -C ../other-repo ls-files` was denied and pointed at `find_files tracked=true`, which answers
  about the **loaded workspace**, not about `<dir>` — a deny with no replacement. All three git rows
  now resolve the directory the command actually addresses — the `-C` target, then a directory
  operand, then the hook payload's `cwd` — and deny only when *that* sits under a
  `.sln`/`.slnx`/`.slnf`/`.csproj`. `git -C src status` inside the same tree is still denied. (I153)

## [0.28.0] - 2026-08-08

> **Reconstructed.** `v0.28.0` was tagged and released, its link definition was written, and this
> heading was not - the gap `ChangelogReferenceTests.EveryTag_HasAVersionHeading` now exists to catch.
> The entries below are the release's own commits, restored from `git log v0.27.0..v0.28.0`; they are
> a summary of what shipped, not the per-row detail the other sections carry.

### Added

- **`gate`** - the end-of-task quality sweep as one call: `analyze` at `info`, `format`,
  `cleanup fix=all` and `analyze` again, over the files changed since the workspace loaded, answering
  one verdict line instead of four responses.
- **`search_text`/`search_regex` `matchesOnly=true`** - print the matched span instead of the whole
  line, the way `grep -o` does, composing with `unique=true`.
- **`search_symbols scope=src|test`** - keep one half of the solution, so a name the tests declare
  dozens of times stops burying the single production declaration.

### Fixed

- **An honest diff.** `diff_symbols` and `diff_text` no longer report a hunk they could not map as if
  they had; the repeat-query latency oracle measures after the warm-up call rather than during it, and
  its settled-versus-cold comparison - which could not fail - was dropped.

## [0.27.0] - 2026-08-08

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> A successful edit no longer prints an `N files changed` line above its one-line-per-file answer —
> the lines are the count, and the removed line was 720 responses of ceremony including the
> ungrammatical `1 files`. It still prints for a `dryRun`, a `verbose` run, a rollback and a
> `0 files changed`. `workspace_status` and `load_workspace` now print the solution path
> **workspace-relative** instead of absolute. A clipped `.md` `read_text` gains a
> `headings=true`/`section=` steer, and a `load_workspace` gains a `compilations=cold` note whose
> counterpart, `compilations=realized in Nms`, is appended to the first semantic call that realizes
> them.

### Added

- **`get_symbol_source`, `get_symbol` and `get_type_outline` take `path=`**, resolving the name inside
  that file first and only falling back to the solution when the file holds no match. A member name an
  outline has just printed — `TestResultParser.Parse`, `SymbolReference.Parse`, `DotnetRunner.Report` —
  could still answer `SaturatedName` or `AmbiguousSymbol` solution-wide, costing one refused call plus a
  retry with the full documentation id; the file the outline named is the disambiguator, so it is now an
  argument. A `path=` that names no document of the workspace answers `DocumentNotFound` rather than
  being ignored, and `symbolIds=` scopes every id in the batch. (I134)
- **`changed_files` takes `exclude=`**, a glob applied after `path=` has selected, exactly as
  `search_text` and `search_regex` already have one. On this shared tree 27 of 63 rows were another
  session's untracked `.research/**` and `.serena/**` notes and every call paid for them; `path=`
  enumerates what you want, and the set of another session's files is not knowable in advance. An
  excluded file is not counted, so the summary reports what the listing actually holds. (I135)

- **`find_files` takes `tracked=true`**, listing only the files git tracks, answered from
  `git ls-files --cached` through the existing `GitRunner`. Nothing answered "which files does git
  track under this path", so picking a checked-in fixture to mutate cost a `Bash: git ls-files | grep`
  fallback — `find_files` lists what is on disk and `changed_files` lists only what changed.
  `.gitignore` was refuted as a source: it answers ignoring, not tracking, and a file can be untracked
  without being ignored. A bare `git ls-files` is now denied by `terse guard`; every flagged form
  (`--others`, `--deleted`, `-z`) is left alone, because those are not replaced. (I137)
- **The one-off cost of realizing a solution's compilations is now attributed to the call that pays
  it.** `load_workspace` ends with `compilations=cold - the first semantic call realizes them and pays
  for it once` while nothing is realized, and the first semantic call that does realize them appends
  `compilations=realized in Nms (once per load, not per call)`. Measured at 7 414 ms on this repo's 301
  documents with no response saying why, which is exactly how a per-call latency claim was
  mis-diagnosed from transcript timestamps. Both notes are proven, not assumed: the state is read from
  Roslyn's non-forcing `Project.TryGetCompilation`, so the note appears only when the call really moved
  the workspace from cold to realized, and never on an `ERROR`. Pre-warming at load was refuted — it
  moves the cost to where the agent can attribute it even less, and charges workspaces whose semantic
  tools are never called. (I138)
- **`terse serve --tools core` advertises a 21-tool subset instead of all 86** (`TERSE_TOOLS=core` does
  the same). The full surface is attached to every request — 3 365 479 B, about 842 000 tokens, across
  216 records in one measured week — and a catalogue this size is past every published inflection
  point for tool-selection accuracy. **Only the advertised list shrinks: every hidden tool still
  answers when called by name**, so nothing is lost, and `workspace_status` says
  `tools=core - 21 advertised; every other tool still answers when called by name` so the profile is
  never invisible. Deleting tools and shortening `[Description]` text were both refuted — every tool is
  census-gated and used, and description quality is the opposite of the measured lever.
  `ToolCensusE2ETests.EveryToolInTheCoreProfile_IsAToolTheServerAdvertises` fails if the profile ever
  names a tool the server does not advertise. (I140)
- **`edit_text` takes `edits=[{oldText,newText,section,occurrence}, ...]`**, applying several edits to
  the SAME file in order as one write. Measured at 2 033 calls over 162 transcripts, 1 189 of them
  inside runs of three or more consecutive calls (longest run 26) and 952 of 1 811 adjacent pairs
  addressing the same path, each re-sending 847 characters of argument framing. **An entry whose anchor
  fails is reported on its own line with its error code and remedy and the rest still land** — a
  whole-call failure on one bad anchor was refuted, an agent recovers 37 points worse from it — and
  the response then keeps its full form because a partial result is a caveat. Capped at 10, because
  batched-item accuracy falls off past about six; a batch spanning several files was refused outright.
  `newText` is now optional, since `edits=` carries it. (I141)
- **A `CompileRegression` now hands back a `retryWith` token instead of discarding the declaration it
  rejected.** 152 rollbacks in one measured window (`replace_symbol` 91, `add_member` 29, `write_text`
  14, `replace_symbol_body` 10, `delete_symbol` 3) each threw away an average 870 characters of input
  that the retry then re-sent. The rejection line now ends `retryWith=r3  the rejected text is held`,
  and `replace_symbol`, `replace_symbol_body` and `add_member` take `retryWith=`, replaying the held
  targets and declarations — including a multi-file `symbolIds`/`declarations` batch — under whatever
  flags the retry passes (`allowErrors=true`, `dryRun=true`, or nothing, after the missing callee has
  been added). The server holds the last 8 rejections of the process; a token it no longer holds
  answers `retryWith=… names no held rejection of <tool>` rather than silently editing with an empty
  declaration. Auto-applying the rejected edit was refused: `CS0103` is as often a genuine typo as a
  symbol you are about to add. (I142)

### Changed

- **`SKILL.md` leads with the positive routing table; the prohibition list moved below it.** The skill
  is ~13 000 tokens loaded whole into an agent's context and opened with a forbidden list — the form
  measured to decay, from 73 % compliance at turn 5 to 33 % at turn 16, while commission constraints
  hold at 100 % (arXiv:2604.20911, 4 416 trials, 12 models, decay induced by adding MCP tool schemas
  to context). Nothing was deleted: the whole hard gate, its banned reasoning and its tripwires are
  intact, they now follow the table they are about. Shortening the file was refuted — instruction-file
  size is an affirmative null over 1 650 sessions (arXiv:2605.10039). (I144)
- **A clipped `.md` read now steers to `headings=true` and `section=` beside `next: startLine=`**, the
  way a clipped `.cs` read already steers to `get_file_outline`. 441 `read_text` responses truncated in
  one window (`.cs` 290, `.md` 149) at a mean overflow of 79.3 %, with 165 repeat reads of a path
  already truncated once — an agent paging a document the heading map would have answered in one call.
  Raising the default `maxChars` was refuted: a 79.3 % mean overflow means a bigger cap would not close
  it. (I145)
- **Three framing residues, each below the individual floor, removed as one family (~20 000 tokens per
  week combined).** `workspace_status` and `load_workspace` printed the **absolute** solution path
  (116 and 55 responses) where the workspace is its own root — it is now workspace-relative, so a
  solution at the root prints its file name and `verbose=true` still echoes the full path. The
  redundant `N files changed` line above a one-line-per-file answer is gone (720 occurrences across
  `replace_symbol`, `add_member`, `replace_symbol_body` and `delete_symbol`, including the
  ungrammatical `1 files`); it still prints whenever the answer is not condensed — a `dryRun`, a
  `verbose`, a rollback, or `0 files changed`. And `edit_text`'s multi-match refusal now lists the
  candidate lines (`occurrence=1  line 12: …`, up to five), so `occurrence=` can be picked without a
  re-read — 65 of 2 033 `edit_text` calls hit that refusal and 76 immediate retries followed. (I146)
- **`unload_workspace` resolves the short solution name the other tools print.** Making the path
  relative (I146) would otherwise have handed the agent a value that `Path.GetFullPath` resolves
  against the server's working directory — on a tree with agent worktrees, `TerseSharp.slnx` would
  have unloaded the wrong workspace and reported `unloaded`. It now matches the target against every
  loaded workspace's absolute path **and** its printed short form, and refuses when two of them answer
  to the same name rather than picking. `list_workspaces` keeps the absolute path, because that is the
  value `unload_workspace` documents.

### Fixed

- **`doctor` no longer reports a never-installed asset as `OK`.** `AssetState.Stale` was
  `(SkillInstalled && !SkillCurrent) || (GuardInstalled && !GuardCurrent)`, so a **missing** asset was
  not stale and the `run: terse install --skill --guard` remedy never fired: `OK assets: skill=current
  guard=absent`. The guard was absent on one machine for a week while built-ins were 29 % of 19 432
  tool calls — it would have denied 3 040 shell text invocations, 503 `git status`/`git diff`, 195
  `dotnet format`/`build`/`test` and 159 `Read` calls on `.cs`-family paths (~107 000 tokens). The
  property is now `NeedsInstall` and is true for any asset that is missing **or** out of date, and
  `ClientRegistrarTests.NeedsInstall_IsTrueForEveryAssetKindThatIsMissingOrStale` discovers the asset
  kinds from `AssetState`'s own constructor, so a kind added later cannot be silently absent. Observed
  red on the old expression (`SkillInstalled`), then green. (I139)
- **A `.cs` write is compile-gated against the workspace as it is now, not as it was.** `write_text`,
  `edit_text` and a `.cs` delete ran with `semantic: false`, so the pending notice for a file an earlier
  `write_text` had just created was never drained: overwriting an existing document that referenced it
  answered `CS0103`/`CS0246` and rolled the whole write back, and two interdependent new files could not
  be created in either order without `allowErrors: true`. Those three tools now synchronise first when
  the target is C#; a non-`.cs` write is unchanged and still pays no sync. Reproduced by
  `WriteText_OverAKnownFileReferencingAFileJustCreated_IsNotRolledBack`, observed red with the exact
  `CS0103 … was rolled back` of the report, then green. (I136)

## [0.26.0] - 2026-08-08

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `read_text` on a `.cs` path asked for whole — no `startLine`, `endLine`, `tail`, `section` or
> `verbose` — now answers that file's **outline** plus a steer instead of its text. Whole-file `.cs`
> reads were 71.2 % of all `read_text` output and 32 % of every character this server has ever
> returned; an outline is 32 % of the same file's tokens. Pass `verbose=true`, or any line range, to
> get the text. A second format change: `TextCompressor.Source` no longer drops blank lines or strips
> trailing whitespace, so a source payload is dedented and otherwise returned exactly as written.

### Added

- **`replace_symbol` applies edits to several files as ONE compile-gated edit.** Pass `symbolIds` and
  `declarations` — one declaration per symbol, paired positionally — and a signature change lands
  together with the callers it breaks, in however many files they live in and several per file. This
  was the repo's most common rejection class: a signature change whose callers live in another
  project always cost one `CompileRegression` plus a retry with `allowErrors: true`, paying the
  rejected call *and* the whole declaration payload, six times in one measured run. Capped at 20
  edits; unpaired arrays are refused naming both counts, and two edits where one declaration contains
  the other are refused **in either order** rather than silently dropping the inner one.
  `Errors.CompileRegression`'s remedy now names the batch. (I122)
- **`get_symbol_source` takes `comments=false`**, dropping doc comments and inline comments through a
  Roslyn rewriter. A comment on its own line goes with its indent and its newline, so no blank line is
  left behind; a comment sitting between two tokens leaves exactly one space, so the stripped source
  never joins `return`/*c*/`value` into `returnvalue`. Worth about a tenth of the response on a documented codebase and nothing on one that
  carries no comments — which is why it is opt-in and never a default. (I128)
- **`get_file_outline` and `get_type_outline` take `parameterNames=false`**, printing parameter types
  without their names. Measured at about an eighth of an outline's tokens, and every overload is still
  told apart. (I129)
- **`find_files` accepts `query` as an alias of `glob`**, because `search_text` spells the same idea
  `query`; the blank-glob remedy now names all three spellings. (I131)
- **`run_tests` reports per-project counts on a multi-project run.** The green one-liner and the full
  report gain `Name:total/durationMs` per project, so "which tier is slow" no longer costs a second
  full run. A single-project run is byte-for-byte unchanged. The assembly name is split from the
  `.trx`'s `codeBase` on both separators rather than by the host's `Path` API, because a `.trx` is
  written by whichever machine ran the tests. (I125)
- **`diff_symbols` ends with the exact `diff_text path=…` call** for the hunks it could only map
  `HEURISTIC`, capped at three paths. (I126)

### Changed

- **`read_text` on a whole `.cs` file answers the outline plus a steer — a response-format change.**
  Any line range, `tail`, `section` or `verbose=true` still returns the text, and a `.cs` file that is
  not a workspace document falls through to the text unchanged. (I133)
- **`TextCompressor.Source` only dedents — a response-format change.** Blank lines and trailing
  whitespace now survive in every source payload. The blank-line squeeze saved 104 tokens of 308 980
  (0.03 %) across this repo's 283 `.cs` files, because BPE already folds `\n\n`, and it was the one
  branch that rewrote payload text and could corrupt a raw-string literal. The dedent, worth 18–31 %
  at member scope, is untouched. (I127)
- **An argument a tool does not declare is now rejected instead of silently ignored.**
  `ToolArgumentFilter` compares the supplied argument names against the advertised schema before
  dispatch and answers `ERROR InvalidArgument: <tool> rejected the call: unrecognized <names>` with
  every accepted spelling and the tool's worked example. Before this,
  `find_files {"pattern":"*.cs","max_results":3}` returned all 19 files — a confidently wrong answer
  the agent could not detect. `Errors.Blank` also names a parameter's aliases now. (I130, I131)
- **`diff_text`'s description states what it costs and how to bound it** rather than telling the agent
  not to call it, and opens with `Replaces Bash git diff`. A discouraged tool measures as a missing
  tool, and the missing tool was answered by the shell. (I126)

### Fixed

- **`replace_symbol_body` no longer eats the blank line after the member.** `WithBody` and
  `WithExpression` discarded the replaced node's trailing trivia — `.WithSemicolonToken(default)` and
  a freshly created semicolon token both drop it — so a body edit that should have been byte-identical
  dirtied the file. A no-op body edit now answers `0 files changed` with the identical-content note.
  `replace_symbol` was never affected. (I123)
- **A symbol id that arrives HTML-escaped now resolves.** `SymbolLookup.ResolveAsync` decodes
  `&lt;`/`&gt;`/`&amp;` before parsing, so a generic member id copied out of a rendered response no
  longer costs a retry. The decode is skipped when the text carries no `&`. (I124)

## [0.25.0] - 2026-08-08

> **Response-format change (MAJOR under this project's rules; on `0.x` the MINOR segment carries it).**
> `read_text` now clips at 40 960 characters unless `maxChars` says otherwise — the old default of
> 131 072 sat above what an agent client will inline, so a whole-file read of a large document was
> spilled to a file and answered nothing. A clipped read names `next: startLine=` exactly as before,
> and an explicit `maxChars` is honoured up to the unchanged 131 072 ceiling.

### Changed

- **`read_text` clips at 40 960 characters by default instead of 131 072 — a response-format change.**
  The old default sat above what an agent client will inline, so a whole-file read of a large document
  was written to a `tool-results/*.json` on disk and answered nothing: `IMPROVEMENTS.md` at 102 KB cost
  one persisted read plus four range reads to recover the same text. An unset `maxChars` now uses
  `FileService.DefaultResponseCharacters` (40 960) and the clip names `next: startLine=` as it always
  did; an explicit `maxChars` is still honoured up to the unchanged 131 072 ceiling. Raise it when you
  genuinely need a large file whole. (I119)
- **A mutating tool that changed nothing now says so.** `EditGate` emits
  `NOTE no change - the result is identical to what is already there` whenever it produced 0 diffs, so
  `0 files changed` is no longer byte-identical to the silent-drop failure. Covers every edit,
  refactor, `write_text`, rename and format path. (I112)
- **`changed_files` takes `path=`**, the same pathspec `diff_symbols` and `diff_text` accept, applied to
  both `git diff` calls and to the untracked `ls-files`. On a tree shared with other sessions this is
  the difference between reading your own change set and everybody's; the truncation steer now names
  it. (I108)
- **The still-locked build note names its holders instead of ruling out the workspace.** It used to
  assert "this is not the workspace" — and in the observed case it was: an MSBuild `BuildHost` spawned
  out of the tree's own `bin/` by an earlier terse load. The note now rules out only the shadow-copied
  analyzer and generator set, and appends one `holder pid=<n> <name> startedUtc=<utc> - <kind>` line per
  process the build named, classified as this terse server, an MSBuild or BuildHost, a live `testhost`
  to wait for rather than stop, a bare `dotnet` host, or a pid already gone. (I109, I111)
- **`analyze` / `format` / `cleanup` with `changed=true` keep their change set across the
  unload-and-reload that `build` and `run_tests` perform on a locked output.** The change epoch moved
  from `LoadedUtc` to a new `LoadedWorkspace.ChangedSinceUtc`, which `WorkspaceRegistry` carries across
  an `Unload(reclaim: false)` and drops on a deliberate unload, so a fresh load still starts fresh.
  Previously the first analyze after a build answered `no document under that scope was modified`,
  which reads as "your files are clean". (I110)
- `IMPROVEMENTS.md` is now two tables and nothing else — `## Open` (Finding, Tool, Proposed change,
  Expected saving, **Rejected**) and `## Closed` (Finding, Tool, Change, Outcome). The five per-task
  review narratives, the notes blocks and the separate "Known limitations" section are folded into
  rows; every row is one line. 380 lines → 177. The shape is a hard gate in `CLAUDE.md`, census-gated
  by `BacklogShapeTests`, which fails on any extra heading at any level, on any non-blank line that is
  not a heading or a table row, on a missing column header, and on a row whose cell count does not
  match its own table's header.

### Added

- **`terse call <tool> --workspace <path> --json '{...}'`** — call one MCP tool of this binary from the
  shell and print its response. A claim about a freshly built `terse` used to mean hand-writing four
  JSON-RPC frames into a pipe, and a probe that skipped `--workspace` answered about an auto-discovered
  solution instead of the one under test. The verb resolves the tool by its advertised name, builds the
  same `ToolContext`, loads the named workspace explicitly, binds arguments by name, and answers an
  unknown tool, unparsable `--json` or a missing required argument as a structured `ERROR` with a
  `remedy:`. (I115)
- **`find_files stamps=true`** appends each listed file's UTC last-write time and byte length, so
  "when was this written, and how big is it?" no longer costs three shell calls. (I106)
- **`search_text` and `search_regex` take `exclude=`**, a glob applied after `glob=` has selected, for
  the folder a positive glob cannot leave out. Measured at ~900 of 1 500 tokens on one call. (I117)
- **`edit_text` takes `occurrence=N`**, which picks the Nth match of a deliberately repeated `oldText`
  instead of forcing you to lengthen the anchor on a file of near-identical rows. The ambiguous-match
  remedy now offers `occurrence=1..N`, and an out-of-range value names the range it could have picked.
  (I120)

### Fixed

- **A file another process holds open no longer aborts the whole workspace sync.** The drain's read is
  `FileShare.Read`, so an editor save mid-drain threw and `DrainAsync` abandoned every path it had
  taken, answering `ERROR Internal IOException`. Only the failing path is re-queued now; the rest of
  the drain lands, and the next sync absorbs the re-queued file. (I105)
- **`write_text` no longer stamps a UTF-8 byte-order mark onto a `.cs` file that had none.** The
  compile-gated path built its `SourceText` with `Encoding.UTF8`, which carries a BOM; it now uses the
  document's own encoding, falling back to `AtomicWrite.EncodingOf`, as `EditGate` already did. The
  symptom was a phantom first-line diff on every `write_text` over an existing file. (I118)
- **`edit_text` explains an `oldText` that only matches once indentation is ignored.** Pasting a
  `get_symbol_source` payload back as an anchor could not work — that output is dedented and
  blank-stripped — and the two texts look identical in a transcript. The 0-match remedy now detects the
  case and steers to `replace_symbol_body` / `replace_symbol` / `read_text verbose=true`. (I107)
- **The guard is no longer defeated by a quote, an env prefix or a sub-shell.** `"git" diff`,
  `GIT_PAGER=cat git diff`, `(git status)`, `"dotnet" build` and `LC_ALL=C grep …` were all allowed
  because the driver was read from the raw first token. Leading `NAME=value` assignments are skipped,
  a `$(…)` command substitution is opened, and every token is stripped of quotes, backticks, parens
  and braces before the driver is taken — so `$(git status)` and `FILES=$(git diff --name-only)` are
  denied too;
  `git log`/`commit`/`push` and `dotnet restore`/`pack` behind the same prefixes stay allowed. The
  census also refuses a `Replaces Bash …` command that does not start with a known driver, so it can no
  longer enrol prose. (I113)
- **The E2E poll loops report what they observed.** `UpdateE2ETests` gave up after a hard-coded 600
  attempts and said only that it gave up; both loops now run against a wall-clock budget and name the
  attempt count, the elapsed time and the last response. The 17 MB `search_text` test waits for the file
  to reach the workspace file index before asserting, and the `resx_set` entries test asserts the tool
  reported a write before reading the file. Separately, `TemporarySolution` no longer copies
  `AtomicWrite`'s transient `*.terse-<pid>.tmp` files out of the shared fixture — it reuses the
  product's own `WorkspaceFiles.IsTemporary` — which is what made a macOS CI leg die with
  `The process cannot access the file … Scratch.fr.resx.terse-5083.tmp` on a change that touched only
  a markdown file. (I121)

## [0.24.0] - 2026-08-07

### Changed

- **The guard denies `git status` and `git diff`, and tells the agent not to retry them in `Bash`.**
  The working tree has been served by `changed_files`, `diff_symbols` and `diff_text` since 0.16.0,
  but `terse guard` still waved every `git status --porcelain` and `git diff main...HEAD` through, so
  the most expensive answer in a session stayed one keystroke away. Both are now denied in every flag
  and `-C` form — `git -C src status`, `cd src && git diff`, `git diff --cached` — and the denial names
  `changed_files` for a status and `diff_symbols, then diff_text` for a diff. The two rows are
  **scoped to a .NET tree**: the hook is installed user-wide, so the denial fires only when the
  payload's `cwd` sits at or below a `.sln`/`.slnx`/`.slnf`/`.csproj`/`.fsproj`/`.vbproj` — in a
  repository TerseSharp does not serve, `changed_files` cannot answer and `git status` stays allowed.
  Git **history** and
  index/history mutation stay allowed, because nothing here replaces them: `log`, `blame`, `show`,
  `difftool`, `add`, `commit`, `push`, `stash`. Every denial of a command a tool replaces now closes
  with `Remember it: do not run this in Bash again - the tool answers it.`
- **`dotnet watch build` and `dotnet watch test` are denied too.** The subcommand scan stopped at
  `watch` and let the wrapped verb through, so the one shell form that rebuilds and re-tests on every
  keystroke was the one the guard allowed. The scan skips a `watch` option's value and stops at `--`,
  so `dotnet watch run --launch-profile test` and `dotnet watch run -- test` are still the app, not a
  test run. `dotnet watch run`, `dotnet build-server` and the rest of the unreplaced CLI stay allowed.
- **`diff_symbols` now says `Replaces Bash git diff`** instead of "Replaces reading a raw git diff",
  which is what enrols it in the new census gate below.

### Added

- **A census gate that fails when a tool replaces a shell command the guard still allows.**
  `ToolCensusE2ETests.EveryToolThatAdvertisesItReplacesAShellCommand_IsDeniedByTheGuard` reads
  `tools/list`, extracts every command from a description opening with `Replaces Bash …`, and asserts
  `ToolGuard` denies each one. Nothing is enrolled by hand, so a tool added later is covered
  automatically, and the discovered set is ratcheted (`ToolCensus.MinShellReplacements`) so a
  description that loses the prefix fails the gate instead of silently un-enrolling the tool.
  `CLAUDE.md` now carries the matching hard gate: a tool that replaces a built-in
  ships with its guard row, its two-direction `ToolGuardTests` cases and its docs, in the same commit.
- **`build`, `run_tests`, `rerun_failed` and `list_tests` take `properties`** — MSBuild properties,
  each written `Name=Value` and passed through as one `-p:Name=Value`, applied after `-c` and `-f`.
  A project that only builds with `-p:NativeAppHostEnabled=false` was reachable by no tool, so the
  whole build step fell back to `Bash`. An entry that is not `Name=Value` — including one that looks
  like a CLI flag — is refused before any process starts, with an `ERROR InvalidArgument` whose
  `remedy:` names a valid entry, and `rerun_failed` reuses the properties of the run that produced
  the failures the same way
  it reuses its configuration and target framework.

### Fixed

- **`build` and `run_tests` no longer leak this process's MSBuild instance into the `dotnet` child**
  — the fix for `MSB4237: The SDK resolver type "NuGetSdkResolver" failed to load. Could not load
  file or assembly 'System.Runtime, Version=10.0.0.0'` on every solution whose `global.json` pins a
  pre-net10 SDK. `MSBuildLocator.RegisterInstance` writes `MSBUILD_EXE_PATH`, `MSBuildExtensionsPath`
  and `MSBuildSDKsPath` into the server's own environment; `ChildProcess` passed them on, so the
  child muxer honoured `global.json`, loaded the pinned SDK's MSBuild, and was then pointed at the
  net10 SDK's resolvers and tasks. Measured on a project pinned to `6.0.100 rollForward=disable`:
  `MSBUILD_EXE_PATH` alone reproduces MSB4237 byte for byte and `MSBuildSDKsPath` alone gives MSB4062,
  while the same directory restores cleanly from a shell. All three are now removed from every child
  the server starts — `build`, `run_tests`, `rerun_failed`, `list_tests` and the git runner alike.
  `ChildProcessTests` asserts the scrub removes exactly those three from a child's environment and
  leaves every other variable inherited, and — because a hand-written list in one project cannot see
  a change in another — a census assertion registers `MSBuildLocator` and fails if any other
  `MSBuild`-prefixed variable **whose value names a directory** is set in the process without being
  scrubbed.
- **The sync no longer records a file as absorbed on a stamp it never read** (I96). `Settle` stamped
  every drained path from a *fresh* `FileStamp.Of`, so a write landing between the merge and the
  settle — a window spanning the remaining merges and `AdoptAsync` — was recorded as absorbed while
  the solution held the previous content, permanently, until a later write changed the file's length
  or mtime. `MergeAsync` now records only the stamp it read, and only when that stamp is unchanged
  after the read; `Settle` no longer restamps a code path at all; and a drain that throws forgets the
  stamps it took. `AbsorbAsync` also re-checks every changed path immediately before adopting: since
  Roslyn's `TryApplyChanges` **writes the absorbed text back to disk**, adopting a stale read would
  have reverted a concurrent external edit. Regression test: `WorkspaceSyncTests
  .SyncAsync_ForAWriteLandingDuringTheDrain_StillAbsorbsItOnTheNextSync`, observed red before the fix.
- **The first external edit to a file Roslyn has not materialized now moves the code generation**
  (I97). `MSBuildWorkspace` attaches a lazy `FileTextLoader`, so `ReplaceAsync` compared the same
  post-edit disk bytes on both sides and reported no change. A document whose text was never
  materialized cannot be *proved* unchanged, so it is now counted as changed — without calling
  `WithDocumentText`, which would make Roslyn rewrite the file and move its mtime.
  `SyncAsync_ForAnExternalEditBeforeTheTextIsMaterialised_NeverRewritesTheFile` locks that down.
- **`write_text` no longer promises freshness it cannot deliver across processes** (I101). Its
  description scoped the "visible to every semantic tool on the next call, with no reload" guarantee
  to the workspace that performed the write, and says that another loaded workspace, or another
  `terse` process, picks the write up through its own watcher.

### Added

- **`doctor` lists the live `terse` and `testhost` processes** (I100) — pid, resident megabytes and
  start time — because a stale one holds the built binaries and makes a build silently no-op and a
  test run report the previous binary's result. This was the last non-git `Bash` fallback measured in
  the census-gate task.
- **A rejected call carries a worked example of the tool it named** (I98). `ToolExamples` ships one
  known-good call for the twelve tools whose valid arguments are not derivable from the schema — the
  ten `razor_*` tools and `package_add`/`package_remove` — and `ToolArgumentFilter` appends it to the
  `remedy:` line, so one deliberate empty call replaces reading a test file for a working argument
  set. Census-gated in both directions by `ToolCensusE2ETests`.
- **`read_text` says when `startLine` is past the last line** (I102) — `startLine=N is past the last
  line (total=T)` — instead of an empty payload that reads like an empty file.

### Changed

- **A capped listing whose overflow fits a 10 % slack returns the whole list** (I99) instead of a
  steer the caller immediately spends a second call on: `find_files glob="tests/**/*.cs"` answered
  `100/108 files truncated - narrow with …` and cost a second, wider call for the last 8 names.
  `ResultCap.Shown(total, cap)` is now the one rule, applied by every capped listing tool —
  `find_files`, `search_text`, `search_regex`, `search_symbols`, `find_usages`,
  `find_implementations`, `find_registrations`, `list_endpoints`, `impact_of`, `diff_symbols`,
  `changed_files`, `analyze`, `get_diagnostics`, the `resx_*`, `xaml_*` and `razor_*` listings.
  A total beyond the slack still truncates and still steers.

- **`README.md` and `NUGET_README.md` are rewritten for a first-time reader** — 736 → 203 and
  359 → 131 lines. Install is the first section and is two commands; the savings table, the guard and
  the grouped tool list follow; the architecture diagram, the competitor comparison, the guard's
  full deny/allow matrix, the memory and freshness internals and the paste-ready hard-gate block are
  cut or folded into a `<details>`, because they answer questions a reader only has after installing.
  Both still name every advertised tool, which `DocsCoverageE2ETests` enforces from `tools/list`.
- **The git family is hard-gated in the docs that teach the agent, not only listed.** `SKILL.md`'s
  hard gate now states that `git status`, `git status --porcelain`, `git diff` and `git diff <ref>`
  are served by `changed_files`, `diff_symbols` and `diff_text` — running them in `Bash` is the same
  breach as `grep` — and that only git **history** (`log`, `blame`, `show <ref>:<path>`) and
  index/history mutation (`add`, `commit`, `push`) stay on the shell. The same rule is added to this
  repo's `CLAUDE.md` develop-with-TerseSharp gate, whose "`git` plumbing" exemption was wide enough to
  license the fallback the tools exist to remove.
- **`CLAUDE.md` is current** — the Core service list names the `Explore`, `Registration`, `CodeFix`,
  `Clean`, `DiffSymbol`, `Resx*` and `Razor*` services it had grown, the stale "83-tool surface" reads
  86, and the docs gate's README checklist names the sections the rewritten README actually has.

## [0.23.0] - 2026-08-07

**The census gaps are closed.** `CLAUDE.md` listed three "every X does Y" rules it stated as if
enforced and that nothing enforced — plus **I93**, the fourth. All four now discover their subject
from `tools/list`, so a tool added later cannot be exempt by default. No tool, parameter, default or
response format changed by that work: it is test and fixture only.

### Added

- **`ToolCensus`, one checked-in catalogue for every census exemption** (I93). Probes, exclusions,
  verdict prefixes and budget overrides are records that each carry a **written reason**, and
  `ToolCensusE2ETests.EveryExemptionCarriesAReasonAndTheSetOnlyEverShrinks` fails on an empty reason
  or on a set that grew past its `Max…` ratchet. `NoExemptionSurvivesTheToolItNames` fails on an
  exemption naming a tool the server no longer advertises.
- **A census for the no-header rule** (I93). `ToolCensusE2ETests.EveryAdvertisedTool_IsProbedOrExemptWithAWrittenReason`
  reads `tools/list` and fails on any advertised tool that has neither a probe nor a written
  exemption; the header assertion itself runs on a **success** response —
  `ToolHappyPathE2ETests` for the fixture-probed tools, `RazorToolsE2ETests.NoRazorTool_OpensItsResponseWithItsOwnName`
  for the ten Razor tools, and `ToolCensusE2ETests.EveryProcessSpawningTool_AnswersASuccessWithoutAHeaderAndWithinItsBudget`
  for `build`, `list_tests`, `run_tests` and `rerun_failed`. Four tools stay exempt with a reason and
  no success path on the fixture: `unload_workspace`, `undo_last_change`, `package_add`,
  `package_remove`. The census found one real case on its first run — `build ok  …` and
  `run_tests PASSED  …` do open with the tool's own name, deliberately, because those lines are a
  **verdict** and not a request echo; they are registered in `ToolCensus.VerdictPrefixed` and
  `EveryVerdictPrefixedTool_StillAnswersWithTheVerdictItIsExemptFor` proves the exemption is still
  spent on the shape it was granted for.
- **A census for the listing-tool token budget.** Three tests budget **every** read probe in the
  catalogue instead of four tools named by hand:
  `ToolCensusE2ETests.EveryProbedReadTool_StaysWithinItsTokenBudget` for the 42 fixture reads (it
  refuses to pass on fewer than 40), `EveryProcessSpawningTool_AnswersASuccessWithoutAHeaderAndWithinItsBudget`
  for the four process tools, and `RazorToolsE2ETests.EveryProbedRazorReadTool_StaysWithinItsTokenBudget`
  for the six Razor reads, which need the Razor fixture. `search_text` and `search_regex` legitimately
  exceed the 800-token read cap on a full default page of 100 matches, so they carry a reasoned,
  ratcheted override in `ToolCensus.BudgetOverrides`.
- **A census for warnings-behind-`verbose` on the build/test family.**
  `BuildWarningsE2ETests.TheBuildAndTestFamily_IsDiscoveredFromTheAdvertisedSurface` discovers the
  family as every advertised tool declaring **both** `configuration` and `targetFramework` — exactly
  `build`, `run_tests`, `rerun_failed`, `list_tests` — and
  `EveryBuildAndTestTool_HidesTheCompilerWarningsUnlessVerboseIsAsked` sweeps it.
- **`fixtures/WarningSolution` gains a test project**, `tests/Fixture.Warning.Tests`, so `run_tests`,
  `list_tests` and `rerun_failed` are covered against a solution that really compiles with warnings
  rather than only at the render-function level. It is deliberately warning-free itself — the three
  warnings the `build` assertions count come from `src/Fixture.Warning/Calculator.cs` alone — and it
  carries one deliberately failing test, because `rerun_failed` refuses before it ever builds when
  nothing failed, which would have made its leg of the sweep unable to fail. The sweep now asserts
  no member answered `ERROR`, so a tool that never reaches a build can no longer be counted as proof.

### Changed

- **`ToolRobustnessE2ETests` no longer excludes tools by bare name.** The `ProcessSpawning`,
  `WorkspaceMutating` and `Destructive` arrays are replaced by `ToolCensus.RobustnessExcluded`, seven
  `ToolExemption` records each stating why that tool cannot be swept, under a ratchet.
- **`TokenBudgetE2ETests.EveryReadToolStaysWithinTheGlobalCap` is replaced**, not deleted, by
  `ToolCensusE2ETests.EveryProbedReadTool_StaysWithinItsTokenBudget`, which covers its four tools and
  every other read probe as well.
- **`IMPROVEMENTS.md`** — **I93** moves from open to shipped, and **I91** and **I92** are re-examined
  and stay **not taken**, now with the measurement rather than the estimate: the declared-tag design
  for I92 was costed at ~14 call sites and an emitter census found **~30** across 15 `TerseSharp.Core`
  files, two of them load-bearing for behaviour (`XamlRename` filters on `Confidence is "EXACT"`, and
  `TextSearchService` strips its own `"  HEURISTIC  "` to build the `unique=` dedup key), for a
  0.89 % saving behind a MAJOR format change. `CLAUDE.md`'s census table records the gates that now
  enforce its three previously-unenforced rows.
- **`workspace_status` no longer runs the Razor source generators on projects that have no Razor**
  (I67). It walked **every** project calling `GetSourceGeneratedDocumentsAsync`, which forces a full
  compilation, to answer one `generator=ok|unavailable` word — measured at **1448 MB / 29 391 ms**
  against 415 MB / 2.2 s without it on a 148-project solution. A project that holds no
  `.razor`/`.cshtml` additional document cannot have produced Razor output, so it is skipped.

  This row had six falsified hypotheses behind it, and the seventh attempt only landed because the
  real cause was finally instrumented rather than guessed. It was never a product coupling.
  `MSBuildWorkspace` attaches a lazy `FileTextLoader` per document and the load never asks for a
  compilation, so **no `.cs` content is read at load time**. Unfiltered, the generator scan forced a
  compilation of every project on the first `workspace_status`, which materialized every document's
  text *before* the test appended to one. Filtered, nothing did — so when the watcher's edit was
  drained, `WorkspaceSync.ReplaceAsync` compared the document's *lazily read* text against the file
  and found them equal, because both sides read the same post-append bytes. No `ChangeKind.Code` bump
  followed, while `search_symbols` still saw the new type. The test now runs a search **before** the
  append, which parses every document and pins its text, so the edit must arrive through the watcher.

## [0.22.0] - 2026-08-06

**The improvements backlog is empty of fixable rows.** One row remains open —
**I67** — and it is now pinned to a single sentence with six falsified hypotheses and a named next
step, rather than a mystery. Everything else is either shipped or recorded in *Known limitations*
with the measurement that closed it.

### Added

- **`read_text` takes `maxChars`** (I94). `maxLines` cannot bound a file whose lines are very long —
  a 155-line range of this repo's own backlog file answered **50.5 KB** and was persisted to disk by
  the client instead of read. `maxChars` clamps to the same 128 000-character budget the reader
  already applied internally, and a clipped read still names the line to continue from. It bounds the
  file **text**; the gutter, the notes and the count line are not charged to it, and `headings=true`
  is not bounded by it — the description says both. When the budget runs out **inside** a line,
  the response says so explicitly (`line N was cut mid-way …`), because a line range cannot resume at
  a character offset and a bare `next: startLine=N` there would be a steer that never advances.

### Changed

- **`IndexE2ETests.ExternalCodeChange_LeavesTheXamlIndexIntact` polls for the generation it asserts**
  instead of relying on the incidental latency of the Razor generator scan. No assertion weakened;
  the test now fails deterministically rather than by timing, which is what the open **I67** row
  needs from it.

## [0.21.0] - 2026-08-06

**Backlog closure.** This release closes every open row in `IMPROVEMENTS.md` that had a fix — 24 of
them — plus three defects found while proving the fixes. Every one was a measured fallback, dead
call, unprovable answer or silent breakage from a real session.

**Response formats changed**, so an agent or script that parsed them must be updated: `build` reports
`warnings=N emitted`, a clipped `read_text` ends with `next: startLine=…`, `list_projects` prints each
project's path, a complete listing of 25+ records advertises its narrowing parameter, and
`workspace_status` can carry `mapped=`, `targetFramework=` and `idle=… compilations=dropped`. Under
this project's own rule that is a MAJOR change; on `0.x` the MINOR segment carries it, as `0.20.0`
did for the larger framing change.

**The tool surface goes from 83 to 86** — `changed_files`, `diff_symbols` and `diff_text`.

### Added

- **Three git tools — `changed_files`, `diff_symbols`, `diff_text`** (I73). Git was the largest
  fallback class measured in a week of real agent sessions: 575 `Bash` calls / 235 738 tokens, of
  which `git diff`/`git show` alone were 220 calls / 130 458 tokens. `changed_files` answers one line
  per file (`path  +added -deleted  status`, untracked included); **`diff_symbols` maps every hunk
  onto the declaration that contains it and answers with symbol ids** you feed straight to
  `get_symbol_source`, `EXACT` only when a hunk sits inside exactly one declaration and `HEURISTIC`
  with the raw line range and a reason otherwise; `diff_text` returns the bounded raw diff and is the
  last resort. All three take `baseRef=` and are scoped to the workspace root with git's own
  `--relative`, so a workspace nested inside a larger repository never reports a file outside it.
  **This is a second deliberate shell-out** — `GitRunner` over the shared `ChildProcess` runner that
  `DotnetRunner` now also uses, with the same deadline, drain and kill contract. The tool surface goes
  from 83 to **86**.
- **`build`, `run_tests`, `rerun_failed` and `list_tests` take `configuration` and `targetFramework`**
  (I69, I70), passed straight through as `dotnet -c` and `-f`. A Release-only failure and a single
  framework of a multi-targeted project are now reachable without a `Bash dotnet build -c Release`.
- **`read_text tail=N`** (I74) returns the last N lines the way `tail -n` does, so the end of a
  40 000-line log is addressable. Overrides `startLine`/`endLine`.
- **`search_text` and `search_regex` take `context=N` (0–5) and `unique=true`** (I74, I75). Context
  lines are indented continuation lines on the hit's own record, so a search no longer needs a
  follow-up `read_text`; `context=0` is byte-identical to the previous answer, asserted by a test.
  `unique=true` collapses repeated matching lines to the first record plus `x<count>`.
- **`search_text` and `search_regex` take `root=<absolute directory>`** (I74), so a log folder outside
  every workspace root is searchable — `read_text` already read outside roots; the searches did not.
  The answer carries an `outside-workspace` line naming the root. A relative root is refused, and a
  root that does not exist answers `DocumentNotFound` rather than a misleading zero.
- **`get_symbol_source` takes `symbolIds`** (I72, I80), returning several members in one response and
  reporting each id that does not resolve inline as `NOT_RESOLVED <id>` instead of failing the call.
- **Every `symbolId` tool takes `symbol` as an alias** (I77), and no tool declares `symbolId`
  required — a call with neither answers `ERROR InvalidArgument` naming `symbolId`, never the SDK's
  opaque `An error occurred invoking 'X'.`
- **`add_member` adds enum members**, addressed by the enum's symbol id, and **`replace_symbol` and
  `delete_symbol` work on an enum member** (I47). Adding an error code, a diagnostic id or an enum
  case no longer falls out of the compile-gated symbol path into `edit_text force=true`.
- **`add_member path=<file.cs>`** (I57) appends namespace-level type declarations to an existing
  file as one compile-gated edit, so a sibling type needs neither a whole-file `write_text` nor a
  forced text edit.
- **`write_text delete=true`** (I53) deletes a file. A `.cs` document goes through `EditGate`, so the
  removal is compile-gated and covered by `undo_last_change`; a path outside the root is refused.
- **`doctor` reports the machine's installed SDKs and runtimes** (I71) from `dotnet --list-sdks`,
  `--list-runtimes` and `--version`, so a missing .NET 6 runtime is named before a `run_tests` on a
  `net6.0` project fails in the test host. The old line is relabelled `server runtime` because
  `Environment.Version` describes the server process, not what the machine offers a build.
- **`workspace_status` reports `mapped=`** (I54) — how many analyzer or source-generator assemblies
  this process holds — so the I52 regression detector is observable without `unload_workspace`
  destroying the state being measured. Paths under `verbose=true`.
- **`SchemaCensusE2ETests`** (I93, I77): census gates discovered from `tools/list` asserting that
  every mutating tool takes `verbose`, every `symbolId` tool has a `symbol` sibling, and no tool
  declares `symbolId` required.
- **A workspace's Roslyn compilations are released once it goes idle** (I81, I82). One solution-wide
  `analyze` or `get_diagnostics` used to pin every project's compilation for the life of the process
  — measured at **5.8 GB still held 38 minutes after the last call, on a server using 0.00 s of CPU**.
  `LoadedWorkspace.DropCompilations` now re-forks the solution from `MSBuildWorkspace.CurrentSolution`,
  which discards the compilation cache, and refuses while any lease is outstanding. A timer sweeps
  after `--idle-minutes` (or `TERSE_IDLE_MINUTES`, default **15**, `0` restores the old behaviour),
  and **also** releases any workspace idle over a minute once the managed heap passes 2 GB, so the
  ceiling follows active work rather than the largest sweep the session ever ran. `workspace_status`
  prints `idle=<n>m compilations=dropped`, because a silent multi-second re-realization on a call the
  agent thought was cheap is exactly the confident-wrong-answer shape the response rules forbid.
- **`load_workspace` takes `targetFramework`** (I70), passed to MSBuild as the `TargetFramework`
  global property, so a multi-targeted solution no longer answers from whichever framework MSBuild
  happened to evaluate first. The framework is part of the load identity — loading the same solution
  under a different one replaces it — and `load_workspace` and `workspace_status` both print
  `targetFramework=` whenever one was chosen, so the answering framework is never implicit.

### Fixed

- **An applied `extract_interface` or `move_type_to_file` rewrote the user's `.csproj` and broke their
  next build** (I55). `MSBuildWorkspace.ApplyDocumentAdded` writes a redundant `<Compile Include="…" />`
  item plus a UTF-8 BOM, and repeating the refactor appended a duplicate until `dotnet build` failed
  `error NETSDK1022: Duplicate 'Compile' items were included` — so the tool answered `applied` and
  broke the build. Four earlier fixes were attempted and reverted; the prerequisite each of them
  lacked was a sound answer to "does this project glob this file", which now comes from MSBuild's
  **evaluated** properties (`ProjectGlobs`, on the newly referenced `Microsoft.Build` with
  `ExcludeAssets=runtime`) rather than a substring test on the one `.csproj` — correct for
  `Directory.Build.props`-hosted values, for `EnableDefaultItems=false`, and for the `<Sdk Name=…/>`
  element form, all of which the text-based guesses got wrong. The project file is snapshotted before
  the apply and restored through the new `AtomicWrite.BytesAsync`, but **only when the change is
  provably MSBuild's**: every added line must be an `<ItemGroup>` tag or a `<Compile>` item naming a
  file the edit just added, and nothing may be removed or modified — so a concurrent external edit is
  left alone rather than destroyed, which is what killed the second attempt. The discriminator
  requires `UsingMicrosoftNETSdk` before it trusts the two properties, because they are undefined in a
  legacy non-SDK project — where the `<Compile>` item is **required** and removing it would be
  strictly worse than the duplicate this fixes. All four project shapes are pinned by tests.
- **`undo_last_change` can revert a deleted document.** Reverting a `write_text delete=true` used to
  throw, because the restore replayed `WithDocumentText` for a `DocumentId` the solution no longer
  held; the snapshot was popped first, so the file stayed deleted and the undo was lost. The revert
  now re-adds a missing document, and it runs the same `.csproj` guard as any other added document.
- **Every child process the server spawned inherited the server's own stdin — the MCP protocol pipe**
  (I95). `DotnetRunner` redirected stdout and stderr but never stdin, so `dotnet build`, `dotnet test`
  and every `git` call was handed the live channel the client speaks on. Beyond the protocol hazard it
  was the dominant cost of a shell-out: measured against `fixtures/FixtureSolution`, the git E2E suite
  took **248 s (~50 s per call)** where the identical command from a shell in the same directory took
  **86 ms**; redirecting and closing stdin took the same suite to **5.9 s**. The fix lands in the one
  shared `ChildProcess` runner, so it applies to `build`, `run_tests`, `rerun_failed`, `list_tests`
  and the git tools alike.

### Changed

- **A clipped `read_text` names where to continue** (I76): `next: startLine=<first line not returned>
  (total=<lines>)`, plus an `outline: get_file_outline path=…` steer on a `.cs` file. A read the
  *caller's own* `startLine`/`endLine` ended is not clipped and gets no steer.
- **`list_projects` prints each project's workspace-relative path** (I49) and advertises `filter=`.
- **A complete listing advertises its narrowing parameter above 25 records** (I51), not only when it
  truncated — so `list_projects`, which has no cap, can finally say `filter=` exists.
- **`build` reports `warnings=N emitted`** (I58). MSBuild re-emits nothing for an up-to-date project,
  so the count is what *this* build produced, not a cleanliness verdict on the solution. Three
  routes to a positive "nothing recompiled" detector were refuted and none shipped; the wording no
  longer claims what it cannot prove.
- **The compile gate no longer rolls an edit back for a name the project never resolved** (I79). A
  `CS0246`/`CS0234` that the baseline already carried — or that lands in a file which did not exist
  before the edit — is reported as `PRE_EXISTING the project does not resolve a name this new file
  uses: …` with a remedy, and the edit is applied. Everything else keeps today's rollback, so a real
  regression is still refused. This removes the trigger for the most expensive habit measured in the
  session logs: a built-in `Write` to a `.cs` file after the gate refused a new test file whose
  package reference the workspace had never resolved.
- **The call-tool filter answers every binder failure structurally** (I77, I90), not only
  `ArgumentException`: an argument the SDK cannot coerce now returns `ERROR InvalidArgument` naming
  the tool's required and accepted parameters. `ToolBoundary` renders anything else as
  `ERROR Internal <Type>: <message>` with a remedy, under the new `TerseErrorCode.Internal`.

## [0.20.0] - 2026-08-06

**Response format changed, on every tool.** Measured over 1 050 real `terse` calls in one project's
session logs (2 127 134 response characters), roughly 19 % of every byte the server returned was
framing an agent cannot act on. This release removes it. `verbose=true` restores the previous shape
verbatim on every tool that takes the parameter, so nothing is lost — but an agent or script that
parsed a header line, `(truncated=…, total=…)` or the `(verbose=true …)` footer must be updated.
**A record's own text is never rewritten**: every compression here removes framing the server added,
never a character the payload owned.

### Changed

- **No response echoes the request.** The `<tool> <argument>` header line is gone from every tool;
  it is emitted only under `verbose=true`. Where the header carried something the caller could not
  derive — `get_symbol`'s resolved documentation id, `load_workspace`'s discovered solution path,
  `read_text`'s `outside-workspace` marker — that value moved into the body instead. Measured at
  2.70 % of all response bytes, 950 of 1 050 calls.
- **The summary line states the truncation only when there was one.** `4 usages in 2 files` instead
  of `4 usages in 2 files (truncated=false, total=4)`, and `1/17 matches truncated - narrow with
  glob= or maxResults=` when it was clipped. 87 % of the old counters reported a non-event.
- **`read_text` prints the `N: ` gutter only where the numbering jumps**, strips trailing whitespace,
  and drops blank lines in whitespace-insignificant files. A contiguous read now carries one line
  number. The gutter was 7.6 % of that tool's output, and `read_text` alone was 39 % of all bytes.
  The count line reports every line the range **covered**, so a dropped blank never makes a complete
  read report itself truncated. `verbose=true` numbers every line and keeps every blank.
- **`get_symbol_source` and `get_symbol` are dedented**, blank-line-free and trailing-space-free;
  `verbose=true` returns the member verbatim. A payload holding a `"""` or `@"` literal keeps its
  blank lines and trailing spaces, because there they are values rather than layout.
- **Outlines drop the parameter list from a member's short id** unless the type overloads that name,
  so `get_file_outline` prints `OrderService.Submit` and keeps `Reconciler.Reconcile(Order, decimal)`.
  Both still round-trip into every tool that takes a `symbolId`.
- **`search_symbols` and `find_implementations` no longer repeat the symbol name** in the description
  when the documentation id beside it already ends with it: `T:App.IExecutor  interface` rather than
  `T:App.IExecutor  interface IExecutor`.
- **`edit_text` and `write_text` report the file name alone** on a successful write —
  `OrderService.cs  changedLines=3` — because the caller supplied the path. Other edit tools keep the
  workspace-relative path, which they derived.
- **The `(verbose=true for the diff)` / `(verbose=true for the full report)` / `verbose=true lists
  them` footers are gone** from every response. The tool descriptions already say it.
- **The compile gate's counters are omitted when there is nothing to report.** `errors=N (+D)` and
  `warnings=N (+D)` print only when the count or the delta is non-zero; a `dryRun` always prints both,
  because there the counters are the answer.
- **`workspace_status` and `load_workspace` keep their telemetry behind `verbose=true`** — `loadMs=`,
  `elapsedMs=`, `lastUsedUtc=`, the `watch=`/`gen=`/`pending=`/`lastSyncMs=`/`gaps=` line and the
  `index=` hit/miss line. The sync line still prints unprompted when the watcher is off or degraded or
  a gap was seen, and the Razor generator line still prints unprompted when the generator is
  unavailable.
- **`format verify=true` / `cleanup verify=true` answer a clean scope with `clean`** and nothing else.
- **`build` renders its diagnostics workspace-relative**, so a failed build no longer repeats the
  absolute repository path on every line.
- **`TerseError` renders on two lines**: `ERROR <Code>: <message>` then `remedy: <remedy>`. A
  `SymbolNotFound` remedy lists at most 5 nearest ids, where the longest observed was 679 characters.

### Added

- `verbose` on `read_text`, `get_symbol_source` and `get_symbol`.
- `TextCompressor` and `ResponseCompression` in `TerseSharp.Core`, with unit coverage of the summary,
  header and payload-preservation contracts, plus `TokenBudgetE2ETests` assertions for `read_text`,
  `get_symbol_source`, `edit_text` and `workspace_status` against the widest fixture case.
- A `SplitHandler` partial type in `fixtures/FixtureSolution`, so the outline's short-id rule is
  proven against a name overloaded across two files rather than within one declaration.

## [0.19.0] - 2026-08-05

**Response format changed.** `load_workspace` and `workspace_status` no longer list the MSBuild load
failure messages; they report one line per failed project and keep the messages behind `verbose=true`.
The generation counter and index lines both gained a field. An agent that parsed `FAILED <message>`,
`gen=c…/rz…` or the `index=` line should re-read the two entries under **Changed**.

**Large solutions got faster and much lighter.** On a 148-project, 31 000-document solution:
`find_files` **2305 ms → 20 ms**, `search_text` **5547 ms → 685 ms**, and with
`--max-workspaces 1` the resident set after switching solutions is **3347 MB → 963 MB**.

### Added

- **`terse serve --max-workspaces N`, and `TERSE_MAX_WORKSPACES`.** The registry has always kept the
  four most recently used solutions loaded and unloaded the rest; nothing could change that number.
  A loaded workspace costs what Roslyn costs — measured at ~3 GB resident for a 148-project /
  31 325-document solution once its compilations exist — so four is a multi-gigabyte budget that a
  user working in one solution never asked for. The option takes precedence over the environment
  variable, an unusable value in either falls back to the shipped default of 4, and the default
  behaviour is unchanged.

### Changed

- **BREAKING (response format) — load failures are grouped per project.** `load_workspace` and
  `workspace_status` used to print one `FAILED <full MSBuild message>` line per diagnostic, up to
  twenty. On a solution whose NuGet audit is escalated to errors that is a wall of near-identical
  advisory text — measured at **6712 characters** of `workspace_status` on a 148-project solution,
  most of it the same `SharpZipLib` advisory repeated. The default response is now a
  `20 load failure(s) in 9 project(s); verbose=true lists the messages` header followed by one
  `FAILED <project>.csproj  messages=N` line per project, capped at twenty projects with a note when
  more were folded. Same response, **1211 characters**. `verbose=true` restores every message
  verbatim, exactly as before, and the `failures=` / `warnings=` counters are unchanged.
- **`workspace_status` reports a sixth generation counter and a fifth index.** The freshness line is
  now `gen=c12/p1/x3/r0/rz2/f4` — `f` counts file-tree changes — and the index line carries
  `paths(hit=7 miss=1 files=31324)`.

### Fixed

- **An unloaded workspace kept its compilations alive.** `RazorGeneratedMap` caches generated-document
  descriptions in a `static` dictionary keyed by `ProjectId`, and each entry holds an
  `INamedTypeSymbol` and a `Project` — both of which root the whole `Solution`, and therefore every
  compilation in it. Nothing cleared those entries, so `unload_workspace` and LRU eviction dropped the
  workspace without releasing its memory. A disposed workspace now forgets its own projects' entries.
- **Unloading a workspace now actually returns the memory.** Dropping the last reference is not the
  same as giving the pages back: with Server GC on a machine with free RAM there is no pressure to
  collect gen 2, so an evicted 3 GB solution stayed resident indefinitely. Measured on a 148-project
  solution, evicting it moved the working set by **57 MB**. `unload_workspace` and LRU eviction now
  end with a compacting gen 2 collection, which takes the same measurement from **3418 MB to
  652 MB**. It runs when a workspace is **actually** unloaded or evicted, never merely because a tool
  was called, and always after the gate that serialises loading has been released. On a capped server
  that means it can land inside a `load_workspace`, or inside the automatic reload a tool call
  triggers when the watcher saw the solution change — those are the calls that did the evicting. It
  costs about 1.3 s. The unload-and-retry that `build`, `run_tests`, `list_tests` and `clean` perform
  when a locked output blocks them passes `reclaim: false`, because it reloads the same workspace
  immediately; that recovery path is unchanged.

### Performance

- **`find_files`, `search_text` and `search_regex` no longer walk the directory tree on every call.**
  Each call used to enumerate every directory under the workspace root and allocate a `FileInfo` per
  file before it could match a single glob. They now answer from a path index that is built once and
  rebuilt only when the file watcher sees a file appear, disappear or get renamed — the same
  generation-keyed slot the XAML, resx, registration and Razor indexes use, so it is not trusted (and
  the walk happens anyway) when the watcher is off, degraded or behind. Measured on a 148-project /
  31 325-document / 45 941-file solution, warm median: `find_files **/*Service.cs`
  **2305 ms → 19.7 ms**, `search_text` **5547 ms → 685 ms**. The JetBrains Rider MCP answers the same
  two questions on the same solution in 30.5 ms and 386.5 ms.
- **`search_text` stopped decoding files that cannot match.** A literal search now scans the raw
  UTF-8 bytes of each file — vectorized, from a pooled buffer — and only decodes to a `string` when
  the needle is present or the file carries a UTF-16 byte-order mark. Previously every candidate file
  was decoded in full before the first comparison. `search_regex` still decodes every candidate: a
  regular expression has no single byte sequence to pre-scan for.
- **`search_text` and `search_regex` stopped renting a whole-file buffer for a file they are about
  to reject.** The binary probe now reads 4 KB into a small pooled buffer, and the full-size buffer
  is rented only once the file is known to be text. Previously every candidate — including a 8.7 MB
  workspace file or a 5 MB database segment — was rented at full size and released after the probe,
  and `ArrayPool<byte>.Shared` does not pool above 1 MB, so each of those was a fresh large-object
  allocation per file per search. Measured on a 148-project solution: eight identical `search_text`
  calls grew the working set by **491 MB before, 293 MB after**.
- **`search_text` and `search_regex` stopped reading binary files in full.** The 4096-byte NUL probe
  used to run on the decoded text, so a file was read and decoded before it could be rejected. It now
  runs on the first 4 KB of **bytes** and the rest is never read. On the solution above that is
  **2523 MB → 226 MB** of file content per search: 528 MB of `.ldb`, 338 MB of `.db-wal` and 192 MB
  of `.ctr201` were being read and thrown away on every call, none of which any extension allowlist
  named. A UTF-16 byte-order mark suppresses the probe, so wide text is not mistaken for binary.

## [0.18.0] - 2026-08-04

**Response formats changed.** `build`, `run_tests`, `rerun_failed` and `list_tests` no longer return
warnings unless `verbose=true` asks for them: a successful build is one line however many warnings it
produced, a failed one lists error-severity diagnostics only, and the output tail these tools fall
back to is now keyed on "no error was found" rather than "no diagnostic was found". An agent that
parsed `build ok  0 diagnostics`, `FAILED with no parsable diagnostics`, or a failed build's warning
lines should re-read the two entries below.

### Changed

- **BREAKING (response format) — `build` never returns warnings unless they are asked for.** A build
  that **succeeds** now answers in one line however many warnings it produced:
  `build ok  errors=0 warnings=37  elapsedMs=4235  (verbose=true for the full report)`. Previously a
  single warning tipped the response into the full report, so a solution with hundreds of warnings
  cost thousands of tokens on every green build. A build that **fails** now lists its
  **error-severity diagnostics only** and reports the rest as one
  `warnings=37 hidden (verbose=true for the full report)` note, instead of listing every warning
  beside the errors. `verbose=true` restores the previous report, every severity included.
  The quiet line's counters changed from `0 diagnostics` to `errors=0 warnings=N`, so a client
  matching on the old text must be updated. The failed build's summary line counts what was
  **parsed**, not what was printed — `1 diagnostics (truncated=true, total=3)` — so the response
  never claims the hidden warnings do not exist. Two guarantees are unchanged: a failure with no
  error-severity line still lists what it does have rather than answering with nothing, and a locked
  output file, a timeout and an unparsable failure are never condensed to the one-line form — a
  locked build still hides its warnings behind `verbose=true` like any other failure. **`warnings=N`
  counts what *this* build emitted**, so a repeat build that recompiled nothing reports `warnings=0`
  for a solution that has warnings; touch a source file, or read the count as "warnings from work
  this build actually did".
- **BREAKING (response format) — `run_tests`, `rerun_failed` and `list_tests` no longer return build
  warnings either.** When the build inside `dotnet test` fails, the run produces no results and the
  response used to end with the last 15 lines of raw output — which on a warning-heavy solution was
  fifteen lines of MSBuild warnings and none of the errors. That block is now the same shape as
  `build`: **error-severity diagnostics only**, plus one `warnings=N hidden` note, with
  `verbose=true` restoring every severity on `run_tests` and `rerun_failed`. Unlike `build`, these
  three have **no** "list the warnings when there is no error" fallback: a failure that carries only
  warnings answers with the raw output tail, bounded at 15 lines. That tail is now appended whenever
  no **error-severity** diagnostic was found rather than when no diagnostic at all was found, on
  `build` as well, so a failure whose only signal is a warning no longer loses the MSBuild or
  test-host message underneath it — and `verbose=true` stays a strict superset. Its header changed
  accordingly, from `FAILED with no parsable diagnostics; last output lines:` to
  `FAILED with no error-severity diagnostic; last output lines:`. The
  `no test results were produced` note no longer ends in `; last output lines:` because what follows
  it is now usually the errors. `list_tests` is unchanged on success — a listing that matched no
  name still answers in two lines.

## [0.17.1] - 2026-08-03

### Fixed

- **A loaded workspace no longer locks the analyzer and source-generator assemblies a solution builds
  from source.** Every `AnalyzerFileReference` is bound to a shadow-copying `IAnalyzerAssemblyLoader`:
  the directory containing the analyzer is copied once to a user-private
  `terse-analyzers/<content hash>/` cache and Roslyn maps the copy, so the file in the project's
  `bin/` is never mapped and the user's own
  `dotnet build` succeeds while the workspace is loaded. Previously any semantic call — a single
  `get_symbol` was enough — mapped the assembly in place for the lifetime of the server process, so an
  external build failed `MSB3027`, TerseSharp's own `build` and `run_tests` failed the same way, and
  `unload_workspace` could not release it. Measured on `fixtures/GeneratorSolution`: with the analyzer
  mapped in place and its source touched, `dotnet build` exits 1; with the shadow copy it exits 0 and
  the assembly stays writable through `get_symbol`, `analyze`, an edit and `undo_last_change`.
  Roslyn's own non-locking loader could not be reused — `IAnalyzerAssemblyLoaderProvider`,
  `AbstractAnalyzerAssemblyLoaderProvider` and `AnalyzerAssemblyLoader.CreateNonLockingLoader` are all
  internal in Roslyn 5.6 — and a collectible `AssemblyLoadContext` was refuted in 0.15.0 because MEF
  fixer discovery stopped resolving across the context boundary, so the copies load into the default
  context and fixer discovery is unchanged. The cache lives under the user-private local application
  data directory (never a world-writable `/tmp`) and is created `0700` on Unix, copies are published
  atomically through a staging directory and are content-addressed, dependency probing matches the
  requested assembly name and version rather than the first file with the right name, and orphaned
  copies older than seven days are swept at server start. If the copy cannot be made — read-only or
  full disk — the loader falls back to the original path, i.e. to the previous behaviour, rather than
  losing the analyzer. Two properties are unchanged from before and worth knowing: an analyzer
  **rebuilt while the server runs is still served from the copy loaded first** (the default
  `AssemblyLoadContext` cannot replace an assembly identity in place — restart the server), and the
  loader does synchronous file I/O because `IAnalyzerAssemblyLoader` is a synchronous interface with
  no async overload. `I52`.

### Changed

- **`unload_workspace`'s mapped-analyzer `WARNING` is now a regression detector, not the norm.** The
  block naming every analyzer or source-generator assembly still mapped into the server process
  remains, but with the shadow-copying loader above it no longer fires for a solution that builds its
  own analyzer, and the tool description no longer tells the agent to expect it. If it does fire, only
  restarting the server releases those files and the response still prints the pid.

### Added

- **`build`, `run_tests`, `list_tests` and `clean` accept a project *name* for `project=`.** The name
  `list_projects` prints is now addressable: it is matched against the solution's project files first
  and, failing that, against `*.csproj`/`*.vbproj`/`*.fsproj` under the workspace root, so a test
  project that is not in the solution still resolves. A path still wins when it exists, an unknown
  name answers `ERROR ProjectNotFound` naming the closest projects, and a name shared by two projects
  answers `ERROR AmbiguousProject` listing both instead of guessing. Previously a name was resolved as
  a path, handed to MSBuild and came back as `MSBUILD : error MSB1009: Project file does not exist` —
  an error with no remedy, from a tool the agent could not tell it had misused.
- **`list_projects` takes `filter=`**, keeping only projects whose name contains it. On a 145-project
  solution the unfiltered listing is ~7 000 characters, and the parameter was previously accepted by
  the caller and silently dropped.
- **`find_files` accepts `pattern=` as an alias for `glob=`**, matching `search_text` and
  `search_regex`, and `glob=` is no longer a required parameter — omitting both answers
  `ERROR InvalidArgument` with a remedy instead of the SDK's opaque message.
- **`unload_workspace` accepts `workspace=` as an alias for `path=`**, the name every other workspace
  tool uses. Its description now says it takes the solution path, not a worktree name.

### Changed

- **`clean` answers `ERROR ProjectNotFound` where it answered `ERROR DocumentNotFound`** for a
  `project=` that names no project or directory, because all four project-taking tools now resolve
  through the same path. The remedy is strictly more useful — it names the closest projects — and the
  behaviour is unchanged: the call is still refused rather than cleaning the whole workspace.

### Fixed

- **An unbound argument no longer escapes the error contract.** A missing or misspelled parameter was
  answered by the MCP SDK as `An error occurred invoking '<tool>'.` — no code, no remedy, nothing an
  agent could act on. A call-tool filter now renders it as `ERROR InvalidArgument`, naming the missing
  and the unrecognized parameters and listing the tool's required and accepted ones. Closes `I38`.
- **`run_tests`, `rerun_failed` and `list_tests` detect and recover from a locked output file** exactly
  as `build` and `clean` already did: the response carries `WARNING a locked output file blocked the operation`,
  and when a single workspace is loaded the server unloads it, retries the run and reloads. Before,
  `dotnet test` blocked by `MSB3021`/`MSB3027` returned its raw tail with no warning and no retry —
  the reason a session fell back to `Bash dotnet test` and then fought the lock by hand.
- **The still-locked note names the real cause and the process to restart.** A source generator
  referenced as `OutputItemType="Analyzer"` is loaded into the server's default `AssemblyLoadContext`
  and stays mapped for the process lifetime, so `unload_workspace` cannot release it and the user's own
  `dotnet build` keeps failing `MSB3027`. The note now says that, and prints this server's process id.
  The underlying lock is **not** fixed — tracked as `I52` in `IMPROVEMENTS.md`.
- **`ToolRobustnessE2ETests` no longer fabricates the `remedy:` line it asserts.** Its `CallAsync`
  caught the SDK's exception and synthesized `ERROR InvalidArgument … remedy: fix the arguments`, so
  the census could not fail on the very defect above; it now asserts the server's own payload and
  bans the opaque message outright.

## [0.17.0] - 2026-08-01

**Response formats changed.** `search_symbols` now reports the real `total=` and sets `truncated=true`
when it caps; `load_workspace` and `workspace_status` gained `failures=`/`warnings=` counters and stopped
listing MSBuild warnings as `FAILED`; `find_usages`, `explore_symbol`, `impact_of` and `resx_usages` tag a
usage in generated code `gen` instead of `src`; `read_text headings=true` prints an anchor slug column;
`xaml_styles` caps at 100; and `search_regex` anchors `^`/`$` per line. Every change makes an answer that
was wrong or unprovable correct — an agent that parsed the old shape should re-read the entries below.

### Fixed

- **`search_symbols` no longer claims a truncated answer is complete.** It capped the list at
  `maxResults` and then reported that number as the total, so every capped search printed
  `truncated=false, total=<cap>`. Measured on a 148-project solution: `search_symbols("OrderService")`
  answered `50 symbols (truncated=false, total=50)` where the real count is 178 — an agent reading that
  line stops, and silently misses 128 declarations. The summary now carries the real total, sets
  `truncated=true` and steers with `- narrow with kind= or maxResults=`. When the raw match set exceeds
  the internal dedupe ceiling the total is a count of declarations rather than of distinct symbols, and
  the response says so instead of implying an exact figure.
- **`find_files`, `search_text` and `search_regex` walked directories the rest of the server excludes.**
  They carried their own exclusion list (`.git`, `bin`, `obj`, `node_modules`, `.vs`, `.idea`) while
  `WorkspaceFiles` — used by every XAML, resx and Razor index — also excludes `.claude`, `artifacts`
  and `TestResults` and refuses to follow directory symlinks. On a repo with agent worktrees under
  `.claude/worktrees`, `find_files **/*.xaml` reported `total=1376` where the workspace holds 689, and
  `search_regex` returned two-thirds of its matches from stale copies of the same files. Both walkers
  now share one list and one symlink guard.
- **A usage inside generated code is tagged `gen`, not `src`.** `find_usages`, `explore_symbol`,
  `impact_of` and `resx_usages` labelled a hit in `obj/**/*.g.cs` as `src`, inviting an edit to a file
  the build regenerates.
- **MSBuild warnings are no longer reported as load failures.** `load_workspace` and `workspace_status`
  rendered every `WorkspaceFailed` diagnostic as `FAILED` and counted it in `failures=`, so a solution
  whose projects all loaded reported `failures=20` — NuGet advisories (NU1903) and target-framework
  notes (NU1701). They are now split: `failures=` counts diagnostics that actually stopped a project
  loading, `warnings=` counts the rest, and the warnings are listed only with `verbose=true`. That
  removes 20 lines from every `workspace_status` on a large solution. Load diagnostics are also
  collected through a concurrent queue, since MSBuild raises them from parallel project loads.
- **`razor_validate` no longer claims framework services are unregistered.** `RZR009` compared each
  `@inject` against `Add*` calls found in source, so `NavigationManager`, `HttpClient`, `IJSRuntime`,
  `IStringLocalizer` and friends — registered by the Blazor host, not by user code — were reported
  `NOT_REGISTERED  … InvalidOperationException at first render`. Measured on a real Blazor app: 466
  findings, of which the first ten were all false. Host-provided services are now excluded, and when
  the index meets `Add*` calls whose registered types it cannot read (`AddMudServices()` and other
  package extension methods) the finding says the service may be registered inside one of them instead
  of asserting a runtime failure. The suppression list is deliberately narrow — only services the host
  always registers. `IMemoryCache`, `IDistributedCache`, `IStringLocalizer`, `IHttpClientFactory`,
  `HttpClient` and `AuthenticationStateProvider` need an explicit `Add*` call, so they are still
  reported; suppressing them would have hidden the exact bug the rule exists to catch. The `Add*` calls
  counted as unreadable are only those that pass no type and no `typeof` — a collection `.Add(item)`
  or `.AddRange(items)` is not one, so the number in the message is a count of real registration
  helpers.
- **`razor_validate scope=solution` no longer rebuilds its DI index once per file.** The registration
  scan walked every document in the solution for each Razor file examined; it is now computed once per
  run, still lazily, so a 126-component app does one scan instead of 126.
- **`xaml_validate includeUnused=true` reads asynchronously and honours the workspace exclusions.** Its
  C# literal scan used a synchronous `Directory.EnumerateFiles(root, "*.cs", AllDirectories)` plus
  `File.ReadAllText`, walking `bin`, `obj`, `.claude` and `node_modules` and following symlinks.
- **`xaml_styles` caps its answer.** It had no `maxResults` and no truncation: `xaml_styles("TextBlock")`
  on a real WPF app returned 218 records in one response. It now takes `maxResults` (default 100) and
  reports `truncated=`.
- **The symbol writers keep the edited file's line endings.** `replace_symbol`, `replace_symbol_body`,
  `add_member`, `delete_symbol` and the refactors emitted CRLF into an LF file, leaving mixed endings;
  every edit now adopts the ending of the document it changes, and a new file takes it from a sibling
  **non-generated** source document rather than from the solution file. Adoption converts only `\r\n`
  and `\n` — never the other characters `String.ReplaceLineEndings` treats as breaks (`\f`, `\v`,
  U+0085, U+2028, U+2029), which occur inside verbatim string literals — and it runs only on a file
  whose existing endings are already uniform, so a mixed-ending file is left alone instead of being
  rewritten end to end.
- **`resx_validate` proves a zero result.** It answered `0 findings` with nothing to say how much it
  looked at; it now notes the number of families checked and the rules applied.

### Added

- **`search_text` and `search_regex` accept `query`.** Every other search tool on the surface takes
  `query` (`search_symbols`, `xaml_find`, `razor_find`, `resx_find`, `find_registrations`); these two
  took `pattern`, and a call with the wrong name failed with the MCP SDK's opaque
  `An error occurred invoking 'search_text'.` and no `remedy:` line. `query` is now the documented
  parameter, `pattern` stays as an alias, and a call with neither returns a structured error naming
  `query`. Both descriptions now also state what `total=` actually counts — matching **lines**, at most
  one per line — and that a zero result proves absence only in the files the walker searched.
- **`analyze` takes a directory, a glob and `changed=true`**, matching `format` and `cleanup`. The
  mandatory per-file gate over a task's touched files was one call per file; it is now one. The
  dead-code scan is scoped by the same resolved document set as the compiler and analyzer diagnostics,
  so a glob reports the dead code inside it and `changed=true` does not report dead code from files the
  task never opened. `changed` is part of the `sinceLast` history key, so a scoped run is not diffed
  against — and does not overwrite — the whole-solution baseline.
- **`get_file_outline usings=true`** lists the file's own using directives, so a new member's header can
  be written without reading the source.
- **`read_text headings=true` prints each heading's GitHub anchor slug**, so an in-page link is copied
  rather than derived by hand. Repeated headings are numbered the way GitHub numbers them — the second
  `## Added` is `#added-1` — which is most of them in a changelog.
- **`read_text` accepts an absolute path outside every workspace root**, tagged `outside-workspace`, so
  comparing a file against another repo no longer needs a full `load_workspace`. Every writer still
  refuses to leave the workspace.
- **`add_member` and `replace_symbol` accept several declarations in one call**, applied as one
  compile-gated edit. A set of members that reference each other no longer has to be added in
  dependency order, and `replace_symbol` can split a member into overloads.
- **`replace_symbol_body` accepts a bare expression on an expression-bodied member**, instead of
  wrapping it in braces and failing the compile gate with `CS0161`.
- **`load_workspace` and `workspace_status` take `verbose`**, which lists the MSBuild load warnings.
- **`load_workspace discover=true`** lists every `.slnx`/`.sln`/`.slnf`/`.csproj` under a directory,
  shallowest first, and loads nothing. Pointing the server at an unfamiliar repository previously had
  no answer at all — auto-discovery only walks *up* from the working directory — so it took a `Glob`.

### Changed

- **`search_regex` anchors `^` and `$` to each line.** It compiled without `RegexOptions.Multiline`, so
  the anchors matched the whole file: `^### Added` answered `0 matches` on a file with fifteen such
  headings while `### Added` answered thirty-seven. A silently-empty search is read as proof of
  absence, which is what the tool now says it is.

### Documentation

- **`README.md` and `NUGET_README.md` rewritten around what the server buys you** — that TerseSharp is
  the bridge between an agent and a C# codebase, and that the payoff is tokens, money, wall-clock time
  and round trips rather than a tool count. New up front: a TL;DR, a "what it saves you" section
  (money/time/fewer-wrong-edits) and a round-trip comparison. The GitHub README gains three colourful
  **Mermaid** diagrams — the bridge architecture (agent → guard → TerseSharp → Roslyn → disk), the
  four-stage development loop with the tools of each stage, and a sequence diagram contrasting
  `Grep`-and-read with one `find_usages` call. Mermaid is GitHub-only, so `NUGET_README.md` stays pure
  Markdown.
- **The comparison table is extended from 10 rows to 26**, adding the capabilities the alternatives do
  not have: `undo_last_change`, CI-asserted response budgets, one-line success with `verbose=true`,
  short symbol references that round-trip, the `EXACT`/`HEURISTIC` tag, steering truncation, the XAML
  resource graph, Razor/Blazor component API and validation, `@code` edits through the C# tools, the
  `.resx`/`.resw` translation lint, DI/endpoint tools, project-package-solution editing, live disk
  sync, `--read-only`, and the shipped skill plus `PreToolUse` guard hook. It now also appears in
  `NUGET_README.md`, which had none.
- **Leaner prose.** The README drops *Status*, *Design principles* and *What it deliberately doesn't
  do*, and moves the guard matrix, the freshness contract and the update check into `<details>` blocks:
  **6.8% fewer words** (6,144 → 5,726) at the same line count, while gaining three diagrams and a FAQ.
  A new FAQ answers the recurring questions (no IDE/licence, which agents, will it edit behind your
  back, huge solutions, VB/F#, git-DB-debugging scope, how the savings are measured) for human, agent
  and search-engine readers. `NUGET_README.md` keeps its XAML/Razor section, now also covering the
  Blazor validation and markup-aware rename it did not describe.
- **Corrected claims that had gone stale.** Tool count **82 → 83** (README badge, NuGet summary line);
  the `run_tests` per-failure message cap **12 → 30 lines** (`DotnetRunner.MaxMessageLines`); the
  worktree error spelled `AMBIGUOUS_WORKSPACE` is `ERROR AmbiguousWorkspace`; the Razor rule set is
  `RZR000`–`RZR010`, not the six ids previously listed as complete; the compile gate and
  `undo_last_change` are stated as covering the C#/Razor/refactoring edits only, since the `.resx`,
  `.xaml` and project/package/solution writers are file writes; and the token-budget claim now names
  what is actually asserted (the savings table, 21 assertions) instead of "every number".

## [0.16.0] - 2026-08-01

Two changes, both about what a tool response costs and what it tells you: every mutating tool stops
echoing back a diff you already know, and the server tells you when a newer release exists.

> **Response-format change (MAJOR under this project's rules).** An agent that parsed the unified diff
> out of an edit's response must now pass `verbose=true`. Everything a caveat would have told you —
> diagnostics, rollbacks, stale-workspace notes, `NOT rewritten` lists — still prints in full.

### Added

- **A new GitHub release is announced to the agent, once, on a tool response.** The channel is the only
  one every MCP client hands to its model — one extra **last line** on a tool response:
  `UPDATE terse 0.15.2 -> 0.16.0 is available - run: dotnet tool update -g TerseSharp`. **Response-format
  change:** any tool routed through `ToolBoundary` may now carry that trailing line, at most **once per
  server process**; everything above it is the unchanged answer, and a run with nothing to announce adds
  nothing at all.
  The check itself is one `HEAD` request to `https://github.com/…/releases/latest`, whose 302 `Location`
  names the tag — an empty body, no API token and no rate limit, against `api.github.com`'s 60/hour. It
  runs on a background task started after the host, so it cannot touch the fixed 60 s `initialize`
  ceiling, and it is deadlined at 3 s with no retry. The outcome — including a *failed* outcome — is
  cached in `~/.terse/update` (`TERSE_HOME`-aware) for 24 hours, so a restarted server inside that window
  makes no network call, and a broken network is not re-probed once per session.
  `TERSE_UPDATE=0` disables the check, the state file and the asset refresh below; `TERSE_UPDATE_URL`
  repoints the endpoint at an enterprise mirror or a test stub.

- **`terse serve` refreshes the skill and the guard hook to match the running binary.** After
  `dotnet tool update -g TerseSharp`, the installed `SKILL.md` still taught the *old* tool surface and the
  `PreToolUse` matcher could be a version behind — a stale skill is worse than no skill, because the agent
  acts on the wrong contract. Startup now compares the installed skill with the embedded asset and
  rewrites it when they differ, and re-applies the `terse guard` entry when its shape changed. It only
  refreshes what was actually installed: an absent skill is never created, an absent hook is never added,
  and every other hook in `settings.json` is left untouched.

- **`doctor` reports two new lines.** `assets: skill=current|stale|absent guard=…` (with
  `run: terse install --skill --guard` as the remedy) and `update: terse <version> is current` /
  `terse <running> -> <latest> is available`. `doctor` forces a fresh check rather than reading the cache,
  because it is an explicit diagnostic.

### Changed

- **Every mutating tool answers a success in one line per changed file; the diff moves behind
  `verbose=true`.** **Response-format change**, and the largest per-call saving in the surface: an edit
  used to return the whole unified diff on a result the agent had already decided to make.
  `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`,
  `extract_interface`, `move_type_to_file`, `move_type_to_namespace`, `change_signature`, `write_text`,
  `edit_text`, `xaml_set_property`, `xaml_add_element`, `xaml_remove_element`, `razor_set_attribute`,
  `razor_add_element`, `razor_remove_element`, `razor_set_directive`, `resx_set`, `resx_remove`,
  `resx_rename`, `project_create`, `project_set_property`, `project_add_reference`,
  `project_remove_reference`, `package_add`, `package_remove`, `solution_add_project` and
  `solution_remove_project` now answer `<tool> applied` + `<path>  changedLines=N` (+ `errors=N (+D) warnings=N (+D)`
  where the compile gate ran), and take `verbose=true` for the previous output.
  The short form is only emitted when there is nothing else to say: **`dryRun` is never condensed** —
  there the diff is the answer — and **every caveat still prints in full**: the `errors=/warnings=`
  deltas, a rollback, a new compile error, `0 files changed`, `compileGate=unavailable`,
  `workspace=stale`, `UNFIXED`, `designerStale`, and the `NOT rewritten` list a XAML-aware rename
  leaves. `rename_symbol` on a **Razor component** keeps the whole diff, because that result always
  carries a staleness caveat. Paths in the condensed line are workspace-relative, like every other
  path in a response.

- **`load_workspace`, `list_workspaces` and `unload_workspace` route through `ToolBoundary`.** They were
  the only three tools that bypassed it, so an expected exception surfaced as a raw MCP error instead of a
  rendered `ERROR … remedy:` line — and they could not carry the update notice. Their success output is
  unchanged.

## [0.15.2] - 2026-08-01

Seven defects found after 0.15.0 shipped — six by the review, one by CI on macOS.

> **0.15.1 is 0.15.0.** A `v0.15.1` tag was created on the 0.15.0 commit by mistake and the release
> pipeline published it before it could be stopped; deleting a pushed tag was not authorised. The
> package is byte-identical to 0.15.0 and contains **none** of the fixes below. Use 0.15.2.

### Fixed

- **`WorkspaceNotLoaded` on the first tool call after start.** The 0.15.0 fix for the cold-start
  handshake (**I28**) started the stdio transport *before* calling `Preload`, which is what assigns the
  `ready` task every tool awaits. That opened a window where a request arriving immediately found an
  empty registry and was answered `ERROR WorkspaceNotLoaded` instead of waiting. Reproduced by CI on
  macOS (`ReadOnlyServerE2ETests.ReadTools_StillWork`), which is the runner fast enough to hit it.
  `Preload` is assigned on the startup path again; the heavy work stays off it via the `Task.Run`
  introduced in 0.15.0, so the handshake is still not blocked by MSBuild registration or the first load.

- **`PathBoundary.SameFile` no longer calls `File.ResolveLinkTarget` on every comparison.** Because `||`
  short-circuits the other way, the symlink clause added in 0.15.0 ran a filesystem syscall on **both**
  operands for every pair that did *not* match — the common case in every scan over documents. Measured
  at ~108 µs per non-matching call, **~270× slower** than 0.14.0 (8 ms → 2 164 ms over 20 000
  comparisons), on `DocumentLookup`, `CodeFixService` (per diagnostic × per file × up to 25 passes) and
  `RazorContext`. Worse, it did not do what it was added for: a symlinked worktree is a *directory*
  link, so `File.ResolveLinkTarget` on the solution file returns `null`. Link resolution now lives in
  `WorkspaceRegistry` identity only, resolves the **parent directory** with `Directory.ResolveLinkTarget`,
  and runs once per `load_workspace` rather than once per comparison.
- **A `.razor` edit invalidates the generated-symbol cache again.** 0.15.0 replaced
  `RazorIndex.Invalidate` with `Sync.Noticed` and deleted the method — which was `RazorGeneratedMap.Forget`'s
  only caller. Since that map self-invalidates only on a *count* change, editing a component's content
  left `razor_usages`, `razor_codebehind` and `rename_symbol` resolving members from the pre-edit
  compilation, tagged `EXACT`, for the life of the process. `Noticed` now forgets the map on a Razor bump.
- **`project_*`, `package_*` and `solution_*` no longer force a solution reload for a `dryRun`.** The
  0.15.0 write-guard notified on any `IsOk`, and a `dryRun` returns `IsOk`; `ChangeKind.Project`
  unconditionally requests a rebuild, so previewing a diff cost a full MSBuild reload on the next call.
- **`solution_add_project` and `solution_remove_project` notify the solution file they actually wrote**,
  not the `.csproj` argument — which for `project_create` may not even exist yet.
- **`cleanup ids=` is case-insensitive again.** The 0.15.0 analyzer filter compared ids ordinally while
  the result filter used `OrdinalIgnoreCase`, so `cleanup fix=all ids=ca1822` selected no analyzer,
  produced no diagnostics and reported a clean pass having fixed nothing — a silent wrong answer.
- **The XAML `Mentions` pre-filter and the binding finder share one predicate.** The filter only looked
  at values *starting* with `{` while `XamlBindingService` matches a binding anywhere in the value, so
  `Text=" {Binding Amount}"` could make `rename_symbol` skip the file and report success.
- `GeneratedCode.InOutputDirectory` tests the final path segment again, matching pre-0.15.0 behaviour for
  a path that ends in `obj` or `bin`.

## [0.15.0] - 2026-08-01

Closes every remaining row in the improvements backlog.

### Changed

- **The watcher now covers Razor.** `ChangeKind.Razor` joins Code, Project, Xaml and Resx, and
  `WorkspaceGenerations` gains a fifth counter, so `workspace_status` prints
  `gen=c12/p1/x3/r0/rz2`. `.razor` and `.cshtml` were classified as `null` before, which meant no
  watcher coverage at all and no generation to key an index on. **Status-line format change.**
- `find_registrations` follows one level of `Add*` extension methods. A registration wrapped in
  `services.AddTrading()` is now reported at the call site as `AddSingleton<…>  via AddTrading()`, not
  only inside the helper. The helper's own body is still reported, and the chain is followed exactly
  one level - following it arbitrarily is whole-program analysis.
- `xaml_resolve` on a key that matches no keyed resource now lists the implicit styles whose
  `TargetType` is that key, tagged `HEURISTIC`, and **explicitly declines to name a winner** because
  the index does not model per-dialect resource lookup order. A wrong winner would be the confident
  wrong answer the response contract forbids.

### Fixed

- **`replace_symbol_body` accepts the expression body its own error message advertises.** `=> 42;`
  was wrapped as `{=> 42;}`, which parsed into an error-node block, passed the `is BlockSyntax` check
  and produced broken code that only the compile gate caught. Expression bodies are now applied as
  `ArrowExpressionClause`, and a block that fails to parse is refused instead of applied.
- **`RazorIndex` is per-workspace and generation-keyed.** It was a process-wide `static
  ConcurrentDictionary` with no bound, plus a full directory walk and one `stat` per file on every
  call at five sites - including `workspace_status`, which paid it on every status call. It now lives
  in `WorkspaceIndexes` beside the XAML and resx indexes, reuses unchanged documents from the previous
  generation, and is reported in the `index=` line. Closes **I21**.
- **`resx_files` and `resx_validate` no longer re-parse the overflow beyond the 128-document LRU.**
  The per-file translatable key set is cached on the index itself, which is replaced wholesale when
  the resx generation changes, so it is bounded without being unbounded. Closes **I22**.
- **The XAML sweep in `find_usages`, `rename_symbol` and `explore_symbol` no longer parses every XAML
  file.** Each `XamlFileRecord` now carries the identifiers its handlers, binding paths and `x:Class`
  mention, so only files that could match are parsed. Closes the half of **I25** those three tools
  pay; `xaml_find` and `xaml_validate includeUnused=true` still need whole documents by nature.
- **`cleanup fix=…` drives the analyzers with the requested id set.** `ids=` narrowed only the filter,
  so the whole analyzer set ran once per diagnostic id, up to 25 times per project. Analyzers are now
  filtered to those whose `SupportedDiagnostics` intersect the request. Closes **I14**.
- `unload_workspace` clears the fixer catalog, so an unloaded workspace stops pinning analyzer
  assemblies. Closes the practical half of **I15**; the collectible load context remains the only way
  to release the files themselves, and is now the sole content of that row.
- `project_set_property`, `package_add`, `package_remove` and `solution_add_project` tell the workspace
  which file they wrote, so they are correct under `--no-watch`. Closes **I19**.
- `PathBoundary.SameFile` resolves symlinks with `File.ResolveLinkTarget`, so a symlinked worktree no
  longer produces two registry entries for one solution. Closes **I20**.
- **The server answers `initialize` before it touches the workspace.** The preload ran on the startup
  path ahead of `host.RunAsync`, so MSBuild registration and the first solution load could eat into the
  fixed 60 s handshake ceiling - the cold-runner timeout seen on the v0.14.0 tag. The host starts
  serving first and the preload runs on the thread pool. Closes **I28**.
- An interleaved `edit_text` and symbol edit on the same file is covered by a regression test that
  asserts **both** changes survive. Closes **I10**, whose silent-revert form the watcher had already
  fixed; the failure that remained was the expression-body bug above.
- The E2E fixture retries the MCP handshake once when it times out, so a cold runner is a retry rather
  than a false red. This is belt-and-braces beside the `initialize` fix above.


## [0.14.0] - 2026-08-01

### Changed

- **`format`, `cleanup` and `clean` report one line per changed file instead of a diff.** `format` and
  `cleanup` print `path  changedLines=N` per file plus the `errors=/warnings=` counters; `clean` prints
  its counters and stops. `verbose=true` restores the diff and the per-directory list. A rolled-back
  edit, a locked directory and every `dryRun` keep the full output, because those are results that have
  something to say. Response-format change to three tools. Closes **I26**.
- **`write_text force=true` on a `.cs` file that is already a workspace document is now compile-gated.**
  It runs through `EditGate` exactly like `replace_symbol`: the diff, the `errors=N (+D)` counters, and
  a rollback if the write introduces a compile error. `allowErrors=true` opts out for a deliberate
  mid-refactor write. This closes the last hole in the compile gate the server advertises — the index
  task did 9 unchecked whole-file rewrites and the previous release did 6. A file that is not yet a
  document is still written directly; there is nothing to compare it against. Closes **I24**.

### Added

- **`format(changed: true)` and `cleanup(changed: true)`** limit the pass to files modified since the
  workspace loaded, so a post-edit sweep stops reformatting files the task never touched. Closes the
  half of **I23** that was still open; generated code under `obj/` was already excluded.
- **`xaml_add_element(position: "first" | "last")`.** `last` is the default and inserts before the
  closing tag; `first` inserts right after the opening tag. An element with no matching closing tag is
  refused rather than guessed at.

### Fixed

- **`replace_symbol` and `replace_symbol_body` no longer emit the replacement's opening brace at column
  0.** The new node is annotated and run through the Roslyn formatter, so a body passed with its own
  braces lands at the member's own indentation. Observed 20+ times in the previous task, each costing a
  `format` sweep afterwards. Closes **I27**.
- **`replace_symbol` no longer reports `applied` for a no-op.** A declaration whose full text matches
  what is already there answers
  `0 files changed - the declaration is identical to what is already there, so nothing was written`
  instead of a success that wrote nothing. Closes **I9**.
- **`replace_symbol` and `delete_symbol` work on fields.** A field symbol's declaring syntax is the
  variable declarator, so replacing it threw `InvalidCastException: FieldDeclarationSyntax →
  VariableDeclaratorSyntax` and deleting it left a dangling `private int ;`. The target is now promoted
  to its field declaration, and a field that shares one declaration with others (`int a, b;`) is refused
  with a remedy naming what to do. Closes **I8**.
- **`get_file_outline` on a file of top-level statements no longer answers `0 types`** — a claim it
  cannot support, which reads as "the file is empty". It now reports the statement count, the file's
  length and `use read_text`, with a line range per statement. Closes **I18**.
- **`SymbolNotFound`'s `nearest:` line no longer suggests a candidate the resolver would also reject.**
  A name that cannot round-trip — a constructor, an operator, a generic method, a member of a generic
  type — is offered as its documentation id instead of the short form. Closes **I16**.
- **A rebuilt analyzer at an unchanged path no longer serves stale `CodeFixProvider` instances.**
  `FixerCatalog`'s key now includes each analyzer reference's last-write time and length, and the
  process-wide cache is bounded at 32 entries. Closes the correctness half of **I15**; the collectible
  load context it also asks for is still open.
- `xaml_set_property`, `xaml_add_element` and `xaml_remove_element` tell the workspace which file they
  wrote instead of relying on the watcher, so they are correct under `--no-watch` too. Partly closes
  **I19**; `project_*`, `package_*` and `solution_*` still rely on the watcher.
- `load_workspace` matches an already-loaded solution by file identity rather than by normalised path
  string, so two spellings of the same solution no longer produce two entries that make every later
  call ambiguous. Closes the practical half of **I20**.
- CI: `dotnet format style --verify-no-changes` failed on `IDE0022` after 0.13.0. The rule is now part
  of the pre-push check (`cleanup verify=true fix=style` and `fix=analyzers`, plus `format verify=true`).

## [0.13.0] - 2026-08-01

### Changed

- **A green `run_tests` and a clean `build` now answer in one line.** Measured over this repo's own
  sessions, a passing suite cost 60-150 tokens of counters, warnings and timing blocks that no agent
  ever acts on. `run_tests` on a run where `exitCode=0`, nothing timed out, `total > 0` and there are
  no failures returns
  `run_tests PASSED  passed=478 skipped=0 total=478 durationMs=122371  (verbose=true for the full report)`;
  `build` with `exitCode=0`, zero diagnostics and no locked output returns
  `build ok  0 diagnostics  elapsedMs=4235  (verbose=true for the full report)`. **Any failure, any
  diagnostic, a timeout, a zero-test run and a locked output all keep the full report** - the short
  form is only ever emitted for a result that has nothing else to say. `verbose=true` restores the old
  response, and `includePassed` or `slowest` on `run_tests` imply it. `rerun_failed` takes `verbose`
  too. This is a **response-format change** to `build`, `run_tests` and `rerun_failed`.
- `run_tests` prints up to 30 lines of a failure message, was 12, so a multi-line assertion diff
  survives.
- `AmbiguousWorkspace` and `WorkspaceNotFound` now list each workspace as
  `App.slnx (worktree) -> C:\full\path` instead of the path alone, so the remedy names something that
  actually resolves.

### Added

- **`read_text(path, headings: true)`** returns a markdown file's heading map with line ranges and no
  body, and **`read_text(path, section: "## Commands")`** returns one section. Locating two sections of
  a 216-line `CLAUDE.md` used to mean pulling the whole file (~2.6k tokens); the heading map is ~40
  lines. Closes **I1**.
- **`edit_text(path, section: "## Commands", newText: ...)`** replaces a whole markdown section with no
  `oldText` at all, which removes the read-then-match round trip on every documentation edit. Closes
  **I2**. `oldText` is now optional; passing neither `oldText` nor `section` is refused.
- `write_text` creates the directories its target needs instead of failing with
  `DirectoryNotFoundException`. Closes **I3**.

### Fixed

- **`edit_text` no longer fails on a line-ending mismatch.** Matching falls back to a
  line-ending-normalized comparison and maps the result back to the file's real offsets, so an `\n`
  `oldText` matches a CRLF file and only the replaced region is rewritten. Measured on this repo's own
  session log, **130 of 577 `edit_text` calls (22.5%) failed with `oldText matched 0 times`**, and the
  remedy - "include more surrounding text" - made the next attempt *less* likely to match. When
  nothing matches, the error now names the file's closest lines
  (`L21: public static async Task<Result<string>> ...`) instead. Closes **I7**.
- `write_text` keeps the line endings of the file it overwrites, and uses the solution file's dominant
  ending for a new file, so the next `format` no longer rewrites the whole document. Closes **I12**.
- **`add_member` no longer glues the new member to the previous one or to the type's closing brace.**
  It inserts a blank line before the member and keeps `}` on its own line. In two prior tasks this
  defect cost **9 `add_member` calls -> 12 corrective `edit_text` calls** and **6 -> 8** - every one of
  them a `force=true` line edit on C#, the exact fallback the server exists to remove. Closes **I11**.
- **`workspace=` resolution no longer answers `AmbiguousWorkspace` for a hint that names exactly one
  workspace.** Hints are ranked - full path, solution file name, solution name without extension,
  worktree name, root directory name, then substring - and only ties *within the best tier* are
  ambiguous. Loading a repo and a solution nested inside it (this repo and its `fixtures/`) now
  resolves a path hint to the **innermost** workspace containing it rather than refusing. 88
  `AmbiguousWorkspace` errors appear in this repo's own session log. Closes **I5** and **I13**.
- `read_text` no longer counts a phantom trailing line: a file ending in a newline reported
  `total=N+1`.

### Performance

- **Every file-system call on the request path is asynchronous.** `read_text`, `write_text`,
  `edit_text`, `search_text`, `search_regex`, every `.resx`, XAML and Razor writer, the project and
  solution file writers and `terse install` now use `File.ReadAllTextAsync`/`WriteAllTextAsync` and
  `FileStream` with `FileOptions.Asynchronous`; `AtomicWrite.Text` became `AtomicWrite.TextAsync`.
- **`search_text` and `search_regex` scan files in parallel and allocate nothing per non-matching
  line.** They used to materialize one `string` per line of every file and call `string.Contains` on
  each. They now read each file once and walk it with a vectorized `MemoryExtensions.IndexOf` over the
  span (`Regex.EnumerateMatches` for `search_regex`), materializing a string only for a line that
  matched, and fan out over `Parallel.ForEachAsync`. File sizes come from the directory enumeration
  rather than a `FileInfo` stat per candidate.
- `edit_text` counts occurrences with a span scan instead of allocating a full copy of the file with
  every occurrence removed.
- `FileGlob` matches against a `stackalloc` buffer instead of allocating a separator-normalized copy of
  every path it tests, and skips the copy entirely for a path that has no backslash.

## [0.12.0] - 2026-08-01

### Fixed

- **A file created or edited outside the symbol tools is now part of the workspace.** A loaded
  solution was a snapshot taken at load time and nothing ever re-read it, so `write_text` on a new
  `.cs` followed by `replace_symbol` returned `SymbolNotFound`, and an external edit — your IDE,
  `git checkout`, `dotnet format` — was answered from the load-time snapshot **with an `EXACT` tag**,
  which is the response contract's worst failure: a confident wrong answer the agent cannot detect.
  Each workspace now runs a `FileSystemWatcher`, but the watcher is only a hint: state changes after a
  **content comparison**, so a dropped, duplicated or out-of-order OS event can delay a refresh and
  never corrupt one, and the server's own writes are naturally no-ops. Sync is **lazy** — events
  accumulate and are drained by the next call that needs semantics, so a `git checkout` storm costs
  one reload rather than one per file. Before answering about a specific file, its
  `(LastWriteTimeUtc, Length)` is compared against the last known stamp, which catches an event the OS
  dropped and is why `--no-watch` is still correct. A changed `.csproj`, `.props`, `.targets`, `.sln`,
  `global.json` or `.editorconfig`, a `.cs` added or removed under a project's directory, a watcher
  buffer overflow and an over-cap pending set all reload the solution rather than guess; a call
  already holding a lease keeps answering from the snapshot it was addressed against.

- **`undo_last_change` actually reverts now.** It stored whole `Solution` snapshots and replayed them
  through `TryApplyChanges`, which refuses a solution whose workspace version has moved on — so every
  undo after a real edit answered `the workspace refused the revert`. No test had ever exercised a
  successful undo, only the empty-history path. Undo now replays the previous **document texts** onto
  the current solution.

- **A workspace lease is released when a tool call fails.** The sync point held a lease across an
  `await` with no `try`/`finally`, so a cancelled call or an `IOException` from a file being written
  leaked it: the lease count never returned to zero, the `MSBuildWorkspace` was never disposed, and
  `unload_workspace` reported success while MSBuild kept its file locks — defeating the documented
  unload → build → load recipe.

- **The resx document cache was `static`, unbounded and shared by every workspace in the process.**
  Keyed by absolute path and pruned only by an edit, it grew monotonically for the life of the server
  and outlived the workspace that filled it. It is now a per-workspace bounded cache that dies with
  its workspace, so a long-lived server holding several worktrees cannot accumulate parsed resources
  it will never read again.

- **An edit made through TerseSharp's own tools now moves the generation counters.** The counters only
  ever moved for a change the watcher *found on disk*, and an edit applied through `add_member`,
  `replace_symbol`, `rename_symbol` or `undo_last_change` leaves the in-memory solution and the file
  byte-identical, so the drain saw nothing to report. That was invisible while nothing depended on the
  counters; with an index keyed on them it would have meant `find_registrations` answering *"no
  AddSingleton/AddScoped/AddTransient call mentions this type"* for a registration the same session had
  just written — a confident wrong answer with no staleness marker. Applying a solution change now
  bumps `Code`, and `xaml_set_property`/`xaml_add_element`/`xaml_remove_element` and the `resx_*`
  writers bump `Xaml` and `Resx`, so a tool's own write invalidates the indexes that read it instead of
  waiting on watcher latency.

### Added

- **`load_workspace(reload: true)`** discards the in-memory solution and reads it from disk again.
  Generation counters carry over across the reload and the undo history is cleared, because those
  snapshots belong to a workspace that no longer exists. Concurrent callers that all notice the same
  staleness cost **one** reload, not one each.

- **Per-kind generation counters on `workspace_status`** — `Code`, `Project`, `Xaml` and `Resx`, not
  one shared number, so a `.cs` edit does not invalidate a XAML graph and a `.resx` edit invalidates
  nothing Roslyn holds. `workspace_status` grows exactly one line:
  `watch=active gen=c12/p1/x3/r0 pending=0 lastSyncMs=8 gaps=0`. A reload bumps `Code` and `Project`
  only, because it rebuilds the Roslyn solution and says nothing about markup or resources — so a
  `.csproj` save does not invalidate a XAML cache. The counters carry across a reload instead of
  restarting at zero; they answer "changed since I last looked", so a consumer compares them for
  inequality rather than ordering.

- **`--no-watch` and `TERSE_WATCH=0`** turn the watcher off for constrained containers where inotify
  limits make it unreliable; freshness then rests on the per-file stamp check. `terse doctor` reports
  whether this platform supports file watching at all.

- **Undo provenance.** An external change to a file an undo snapshot covers drops that snapshot and
  every snapshot above it, and `undo_last_change` says so — `nothing to undo - 2 snapshot(s) were
  dropped after an external change to src/Foo.cs` — rather than silently reverting someone else's
  work. A reload reports the whole stack as dropped for the same reason.

### Changed

- **The guard names a tool that can actually create a file.** `Write`/`Edit` on a `.cs` path that does
  not exist was denied with a remedy listing `replace_symbol_body`, `replace_symbol`, `add_member` and
  `rename_symbol` — **none of which creates a file**. An agent that needed a new type was left with a
  denial and no legal move, which is exactly how a 0.8.0 session ended up on `edit_text force=true`.
  The denial now names `write_text(path, content, force=true)` for a missing **rooted** path; for a
  relative path, which the hook process cannot resolve against the agent's working directory, it
  offers creation only as the "if it does not exist yet" case, so it never recommends overwriting a
  file that does exist. Every `.cs` **write** denial carries the clause that a file written that way
  is picked up automatically. `find`, `fd`, `ls`, `dir`, `tree`, `wc` and `nl` joined the shell
  text-read list, because `find . -name "*.cs"` walked straight past the guard that `find_files`
  replaces.

- **`write_text` and `edit_text` tell the workspace what they wrote**, and the six file and text tools
  opt out of the sync point: they answer from disk, so forcing a reload before a `read_text` would be
  pure cost.

- **XAML, resx and DI questions are answered from a per-workspace index instead of re-walking and
  re-parsing the whole tree on every call.** Thirteen call sites each did a full recursive scan:
  `xaml_resolve` re-parsed every `.xaml` in the solution to answer about **one** key, `xaml_validate`
  did it to check **one** file, `xaml_styles` to look up **one** type name, and `xaml_localization`
  paid **two** whole-tree walks — one for markup, one for resources — in a single call. The index is
  built once per (kind, generation) and reused until the watcher's per-kind counter moves, so a repeat
  question costs one interlocked read and **zero** file I/O; concurrent callers that all miss share a
  single build rather than one each. When a generation does move, only the files whose
  `(LastWriteTimeUtc, Length)` changed are re-parsed and the rest are carried over: on a 200-file tree
  a one-file edit costs **1 parse instead of 200**. When the watcher is `Off` or `Degraded` the index
  verifies by stamp sweep before answering, which is why `--no-watch` still sees an external change on
  the next call. Any doubt — a watcher gap, an over-cap pending set, a reload in flight — rebuilds from
  scratch rather than guessing. Per-file *records* (keys, names, styles, `x:Uid`s, resource references)
  are always cached; parsed documents live behind a bounded LRU (128 documents or 32 MB of estimated
  document bytes, whichever binds first) because an `XDocument` costs 5-10× its file and caching 1 500
  of them would be a 150-300 MB regression. No tool's response format changed.

- **`workspace_status` reports the index counters** — one more line,
  `index=xaml(hit=12 miss=1 files=9) resx(hit=4 miss=1 families=2) code(hit=0 miss=0 calls=-) documents=9/128 parses=9` —
  so the hit rate is provable from a status call rather than paid for on every response.

- **The guard names the XAML query tools before `find_files`, and sees PowerShell.** `Glob` or a shell
  walk over a `.xaml`/`.axaml`/`.paml` pattern now names `xaml_find`, `xaml_resolve` and `xaml_styles`
  first, because globbing XAML is nearly always a search for a key, a name or a style rather than a
  question about which files exist; the `.resx` remedies name `resx_find` and `resx_validate` beside
  `resx_files`. `Get-ChildItem`, `gci`, `Get-Content`, `gc`, `Select-String` and `sls` joined the shell
  text-read list — on Windows the fallback is PowerShell, and it walked straight past the guard.

## [0.11.0] - 2026-08-01

### Added

- **Ten `razor_*` tools — Razor and Blazor answered through the compiler.** The Razor compiler is a
  Roslyn source generator, so a loaded workspace already knows the type behind every `<Card />`;
  nothing surfaced it. `razor_outline` prints a `.razor`/`.cshtml` file's directives, its element tree
  with every component resolved to its type, and the members declared in `@code`, each at its
  **Razor** line. `razor_component` answers "how do I use this" from source **or** from a referenced
  package: every `[Parameter]` and `[CascadingParameter]` with its type, which are `[EditorRequired]`,
  and the routes it declares. `razor_find` searches components, elements, attributes, directives,
  expressions and routes. `razor_bindings(validate: true)` resolves every `@bind`, `@on*`, `@ref` and
  `asp-for` against the component's own type and reports `EXACT`, `NO_SETTER`, `UNRESOLVED` or
  `UNRESOLVED_CONTEXT`. `razor_codebehind` links the `.razor` to its `.razor.cs`, `.razor.css`,
  `.razor.js` and its `_Imports` chain.
- **`razor_validate` — the faults the compiler does not catch.** An attribute matching no
  `[Parameter]` compiles clean and throws `InvalidOperationException` at render; two components on one
  `@page` route throw `AmbiguousMatchException` at navigation; an `@inject` nothing registers throws at
  first render. `RZR001`–`RZR010` report those, plus a missing `[EditorRequired]`, a `@bind` with no
  setter, a route parameter with no property, a mistyped `@ref`, an orphan `.razor.css` and markup
  that will not parse — each naming the runtime failure it prevents.
- **`razor_set_attribute`, `razor_add_element`, `razor_remove_element`, `razor_set_directive` —
  compile-gated Razor edits.** An element is addressed by the path `razor_outline` prints or by
  `#ref`, formatting outside the edited span survives byte-for-byte, the result is re-parsed, and the
  **Razor generator re-runs** so an edit that introduces a compile error is rolled back with the error
  at its `.razor` line (~170 ms per regeneration). `dryRun` and `allowErrors` behave exactly as they do
  for C# edits.

- **The C# edit tools reach into `@code`.** `replace_symbol_body`, `replace_symbol`, `delete_symbol`
  and `add_member` now recognise a member whose declaration maps into a `.razor` file, edit the Razor
  source through that mapping, and go through the same regeneration gate. `add_member` on a component
  inserts into its `@code` block, creating one when the file has none.
- **`rename_symbol` renames a component properly.** A Blazor component's class name comes from its
  file name, so renaming the type alone is meaningless: the file, its `.razor.cs`, `.razor.css` and
  `.razor.js` siblings, the partial class inside the code-behind and every `<Card …>` / `</Card>` in
  markup are renamed together, all-or-nothing, with `dryRun` support.

### Fixed

- **Razor answers pointed into `obj/`.** `get_diagnostics` and `analyze` reported a `@code` error at
  `obj/…/Home_razor.g.cs:117` where `dotnet build` says `Home.razor:13`, and `find_usages` reported a
  component used in markup inside the generated file. Both now report the **mapped** location, and no
  response contains a generated `*_razor.g.cs` path — following one meant editing a file the next
  build overwrites. **Response-format change:** locations for Razor-backed symbols now carry the
  `.razor` path and line.
- **`search_symbols` was blind to Blazor components.** Roslyn's source-declaration search skips
  source-generated documents, so `search_symbols Card` returned nothing for a component declared in
  `Card.razor`; components are now listed at their `.razor` path.

### Changed

- **`list_endpoints` includes Razor routes.** Every `@page` template is reported with the component
  it sits in, beside the `Map*` registrations.

- **`workspace_status` reports Razor generator health** — `razor=<n> files generator=ok|unavailable`.
  When the Razor generator does not run (a target SDK newer than the server's Roslyn), Razor
  semantics are reported unavailable rather than silently empty, and `razor_validate` says so as
  `RZR000` instead of reporting component rules it cannot compute.

- **The guard covers Razor.** `.cshtml`, `.razor.css` and `.razor.js` are denied to `Read`/`Edit`
  alongside `.razor`, `Grep type=cshtml` is denied, and the denial names the `razor_*` tool to use
  instead. Plain `.css` and `.js` stay allowed — matching is by extension plus the `.razor.css` /
  `.razor.js` pair. **Behaviour change:** `.cshtml` was previously documented and tested as allowed.

- **The guard intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so

## [0.10.0] - 2026-08-01

### Added

- **`clean` — the `dotnet clean` equivalent, surface 72 → 73.** It deletes the `bin` and `obj` directories of the workspace or of one project and answers with `projects=`, `files=` and `freedBytes=` instead of MSBuild output. Unlike `dotnet clean` it also removes `obj`, which is the case that actually unsticks a stale build, and when the loaded workspace's own MSBuild file locks block the delete it unloads, retries and reloads - the recovery `build` already had, now shared. It refuses any path outside the workspace root, only ever deletes a directory literally named `bin` or `obj`, honours `--read-only`, and `dryRun=true` lists what would go. It is **not** covered by `undo_last_change`, because that history holds Roslyn solutions, not files.

- **`cleanup` applies code fixes: `fix=usings|style|analyzers|all`.** `fix=usings` is the default and is byte-for-byte what `cleanup` did before. `style` applies every `IDE*` code fix, `analyzers` every non-`IDE` one (CA and third-party), `all` both - the in-process equivalent of `dotnet format style` and `dotnet format analyzers`. Fixers come from the project's own analyzer references plus the bundled Roslyn feature assemblies; `ids=` and `severity=` narrow the pass with the same vocabulary as `analyze`, so `analyze` names an id and `cleanup ids=<that id>` fixes it. Every fix goes through the compile gate and is rolled back if it introduces an error, and a diagnostic that no fixer covers - or whose fixer throws or offers nothing - is reported as `UNFIXED <id> x<count> - <reason>` rather than silently skipped.

- **`verify=true` on `format` and `cleanup`.** Replaces `dotnet format --verify-no-changes`: no write, no diff, one verdict line - `clean`, or `VERIFY_FAILED n file(s) would change` followed by the paths. The green case is the common case and now costs a line instead of a diff.

### Changed

- **`format` and `cleanup` take a glob or a directory in `path=`.** A file path still resolves to one document; a path containing `*` or `?` is matched against every document's workspace-relative path, and an existing directory takes everything under it. `path=null` still means the whole solution and an empty `path=""` is still refused with `DocumentNotFound`.

- **`format` and `cleanup` never rewrite generated code.** A whole-solution, glob or directory pass now skips anything under `obj`/`bin` and anything named `*.g.cs`, `*.generated.cs`, `*.Designer.cs`, `AssemblyInfo.cs` or `AssemblyAttributes.cs`. An explicitly named file is still honoured. Rewriting `obj/…GlobalUsings.g.cs` was a real, silent side effect of every whole-solution cleanup.

- **The guard intercepts `dotnet format` and `dotnet clean` as well.** They were allowed because nothing replaced them; `format`, `cleanup fix=…`, `cleanup verify=true` and `clean` now do, so both are denied wherever they appear in a compound command, naming the replacement. `dotnet restore`, `pack`, `publish`, `run` and `tool` stay allowed.

- **`TerseSharp.Core` references `Microsoft.CodeAnalysis.CSharp.Features`.** The SDK ships the IDE code-style analyzers with fixer assemblies that fail to load against the Roslyn version this server runs on (`TypeLoadException` on first use), so the fixers now come from the matching Roslyn feature package instead. This grows the packaged tool.


## [0.9.0] - 2026-08-01

### Added

- **Eight `.resx`/`.resw` localization tools** — the surface goes from 64 to 72. `resx_files` lists every
  resource family with its cultures, entry counts, missing-translation total and designer file;
  `resx_get` prints each key with its value per culture and `MISSING` where a translation is absent
  (`values=false` lists keys only, at a fraction of the cost of reading the file); `resx_find` searches
  key, value or comment across every family; `resx_usages` reports the generated designer property
  resolved through Roslyn as `EXACT` plus `GetString`, localizer indexers, `x:Uid`, `[Display]` and Razor
  literals as `HEURISTIC`, with `composedLookups=N` so "no usages" is never claimed as proof when the
  solution builds keys at runtime; `resx_set` adds or updates one key or a batch of `Key=Value` lines and
  creates a missing culture file from the neutral header; `resx_remove` deletes a key from one culture or
  the whole family and refuses while it is still referenced unless `force=true`; `resx_rename` renames
  across the family and rewrites the references it can prove, all or nothing; `resx_validate` reports
  `RESX001` missing translation, `RESX002` placeholder mismatch (separating the missing-`{n}` case from
  the extra-`{n}` case that makes `string.Format` throw), `RESX003` unused (opt-in, `HEURISTIC`),
  `RESX004` duplicate name, `RESX005` orphan, `RESX006` empty value, `RESX007` whitespace trimmed for
  want of `xml:space`, `RESX008` unsorted and `RESX009` stale designer.
- Every write is **surgical**: only the addressed `<data>` element is rewritten, so the schema header,
  `resheader` rows, entry order, indentation, line endings and byte order mark survive, and a result that
  would not parse is refused before anything is written. Typed and binary entries are reported
  `TYPED`/`BINARY` and passed through untouched. A multi-file edit that fails part way restores the files
  it already wrote.

### Changed

- **`terse guard` covers `.resx` and `.resw`.** A denied read, glob, grep or edit on a resource file now
  names `resx_get`, `resx_find` and `resx_set` instead of the C# tools.
- **`AtomicWrite` preserves the byte order mark of the file it replaces.** Every write went out as UTF-8
  without a BOM, so editing a Visual Studio-written `.resx` or `.xaml` showed a whole-file encoding change
  in git. It now detects the existing preamble and writes the same one; a new file is still BOM-free.
- **`xaml_localization` shares the resource index** instead of carrying its own `.resx` reader, so the two
  cannot drift; its `resourceFiles=` count is unchanged in meaning.
- `SKILL.md` teaches the eight tools, the `RESX00n` rules, and that `resx_*` and `xaml_*` writes are file
  writes and therefore outside `undo_last_change`.
- **The guard also intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so
- **The guard intercepts `dotnet build` and `dotnet test`.** It only ever denied reads and edits, so
  the two shell-outs the server most obviously replaces — `build` and `run_tests` — went straight
  through, and the README even documented `dotnet build App.csproj` as an intentional allow. Now
  `dotnet build`, `dotnet test`, `dotnet msbuild`, `dotnet vstest` and bare `msbuild` are denied
  wherever they appear in a compound command, naming the tool that replaces them. `dotnet`
  `restore`, `pack`, `publish`, `run` and `tool` stay allowed: **no TerseSharp tool replaces
  them**, and a denial that cannot name an alternative is a wall rather than a redirect. The shell
  text-read check is also evaluated per command segment now rather than against the whole string.

- **The README and NuGet page document `terse install --guard`.** It shipped in 0.8.0 but the
  enforcement section still described the hook as something you write yourself; both files now give
  the command, a worked example of a denial, and the exact matrix of what the guard denies, what it
  allows (`.css`, `.csv`, `.cshtml`, `.csx` — matching is by file extension, not substring) and why a
  malformed payload allows rather than blocks. Every row was verified against the shipped binary.

## [0.8.0] - 2026-08-01

### Added

- **`explore_symbol` and `impact_of` — one call where three were needed.** Orienting on a symbol meant
  `get_symbol` + `find_usages` + `find_implementations` and assembling the answer by hand;
  `explore_symbol` returns the signature, the XML doc, the location, the usage count split into `src`
  and `test`, the implementation count, the XAML sites and the files it is used in. `impact_of` adds
  the projects that would recompile, so a rename's blast radius is one call instead of three plus
  reasoning.
- **`find_registrations` and `list_endpoints` — the .NET questions grep structurally cannot answer.**
  `AddScoped(typeof(IRepository<>), …)`, a factory delegate or an `AddMyFeature()` extension means the
  concrete type never appears beside the interface, so a text search finds nothing and the agent
  concludes the service is unregistered. `find_registrations` scans the loaded solution's syntax for
  every container call and, when nothing matches, **says that assembly scanning or a container module
  may be responsible** rather than implying the type is unregistered. `list_endpoints` does the same
  for every `Map*` call.
- **`terse guard` and `terse install --guard`.** Every token the server saves on a call the agent never
  makes is zero, and an agent with TerseSharp installed still reaches for `Read`/`Grep` out of habit.
  `terse install --guard` writes a Claude Code `PreToolUse` hook; `terse guard` is the hook itself —
  it reads the payload on stdin and **denies** a built-in on a `.cs`, `.csproj`, `.xaml` or `.axaml`
  path, naming the tool to use instead. It covers the shell too: `grep`, `cat`, `sed` and friends do
  not escape by running in `Bash`. Malformed input allows rather than blocks, so a hook failure can
  never wedge a session.
- **`xaml_styles`** reports every `Style`, `ControlTemplate` and `DataTemplate` that targets an element
  type — keyed and implicit — with the `BasedOn` chain resolved, so "why does this control look like
  that" stops meaning "read `Generic.xaml` and every theme dictionary".
- **`xaml_localization`** joins every `x:Uid` in the workspace to the `.resx`/`.resw` entries that name
  it. A uid with no entry is reported `UNRESOLVED` rather than omitted, so an untranslated element is
  visible instead of silently absent.
- **`xaml_add_element` and `xaml_remove_element`** complete the structured XAML edit surface, addressed
  the same way as `xaml_set_property` and refusing anything that would not parse. Adding to a
  self-closing element is refused with the reason rather than producing invalid markup.
- **`xaml_validate includeUnused=true`** reports `x:Key` and `x:Name` declarations that no XAML
  attribute and no C# string literal references. It is opt-in and tagged `HEURISTIC`, because
  reflection and `FindResource` can reach a declaration no static scan sees.
- **`analyze sinceLast=true`** reports only the diagnostics that appeared since the previous `analyze`
  of the same scope, plus which ones were fixed, so a red→green loop pays for the delta rather than
  re-printing the unchanged set on every iteration.
- Test count: 267 unit and 330 E2E.

## [0.7.0] - 2026-07-31

> **This is a MAJOR change — several tools changed their response format.**
> `get_file_outline` and `get_type_outline` print short member references instead of documentation
> comment ids (`ids=full` restores them); `find_usages` gained a `src`/`test` column and an optional
> `in <Type>.<Member>` one; every mutation and `dryRun` carries `errors=N (+D) warnings=N (+D)`; a
> truncated listing appends `- narrow with <parameter>`; and the XAML tools print workspace-relative
> paths, carry a `dialect=` note, report `HEURISTIC` where `xaml_find` used to claim `EXACT`, and count
> the whole tree in `total` rather than only what they printed.

### Fixed

- **Dialect detection could not fire for Avalonia or MAUI.** `DetectDialect` matched substrings that do
  not occur in either framework's root namespace — `avaloniaui.net` (the documentation site, not the
  markup namespace `https://github.com/avaloniaui`) and `dotnet/maui` (the real one is
  `http://schemas.microsoft.com/dotnet/2021/maui`). Every Avalonia and MAUI file was reported as
  `dialect=wpf`, and so was every WinUI file that did not happen to declare a `Microsoft.UI.Xaml`
  prefix. Detection now matches the real namespaces, treats the UWP/WinUI `using:` prefix form as
  WinUI, and falls back to Avalonia for `.axaml`/`.paml`. No fixture existed for any dialect but WPF,
  which is why no test could fail; there is one per dialect now.
- **`xaml_validate` reported a resource as unresolved when it was declared in another file.**
  Resolution was file-local, so on any real application — where keys live in `App.xaml`,
  `Themes/Generic.xaml` or a chain of `MergedDictionaries` — `XAML003` fired on keys that resolve
  perfectly at runtime. A confident false error is worse than no check: it sends an agent hunting for
  a declaration that exists and invites it to "fix" working markup. `XAML003` now consults every XAML
  file under the workspace root and reports a key only when it is declared nowhere.
- **`xaml_bindings` printed the file name instead of the workspace-relative path**, so two views of
  the same name were indistinguishable. Every XAML record is workspace-relative now, like the rest of
  the surface.
- **`xaml_find` tagged a substring match on an element's type name `EXACT`.** `EXACT` means
  Roslyn-resolved; a text match is `HEURISTIC` and now says so.
- **`xaml_outline` counted elements it did not print.** With a `depth` cut the summary reported the
  whole tree as shown, so `truncated` read `false` on a truncated answer.
- **`xaml_find` aborted on one unreadable file or denied directory.** It walked with a single
  `EnumerateFiles`, the same defect fixed for `search_text` in 0.4.0. Enumeration is isolated per
  directory now, and `bin`, `obj`, `.git` and `node_modules` are pruned during the walk rather than
  filtered afterwards.

### Added

- **`xaml_resolve` — where a resource key actually comes from.** One call reports every declaration of
  an `x:Key` across the workspace with its file, line, type and scope (`local`, `app`, `theme`),
  ordered nearest-first, instead of the agent reading `App.xaml` and each merged dictionary in turn.
  A key declared nowhere says so explicitly rather than answering with an empty list.
- **`xaml_bindings validate=true` — binding paths checked against the real type.** The data context is
  resolved from `x:DataType` (Avalonia, MAUI, WinUI) or `d:DataContext="{d:DesignInstance …}"` (WPF),
  including inheritance from an ancestor element, the XAML prefix is mapped through its
  `clr-namespace:`/`using:` declaration, and each path segment is resolved against the Roslyn symbol —
  nested paths included. A missing member is reported with the nearest member name as a suggestion.
  WPF has no compile-time binding check at all, so this is the only static answer available there.
  When no data context is in scope, or the declared type is not in the solution, the record says
  `UNRESOLVED_CONTEXT` and stays `HEURISTIC` — it never reports an error it cannot prove.
- **`xaml_validate scope=solution`** checks every XAML file in one call and reports how many it read.
- **`xaml_outline filter=named|keyed`** lists only the elements that carry an `x:Name` or an `x:Key`,
  so a large `ResourceDictionary` does not have to be printed in full.
- **`x:Uid` is a first-class citizen.** `xaml_names` reports it alongside `x:Name`, and `xaml_find`
  takes `kind=uid` — the link between XAML and its localization keys was previously invisible.
- **The binding validator refuses to guess.** A path it cannot resolve member by member — `{Binding .}`,
  an indexer, a WPF current-item `/` path, an attached property in parentheses — is reported
  `UNSUPPORTED`, never `ERROR`. Interfaces are searched through `AllInterfaces`, so an interface-typed
  data context does not report every valid binding as missing. A prefixed type name whose `xmlns` does
  not resolve, or whose simple name is ambiguous across the solution, answers `UNRESOLVED_CONTEXT`
  rather than validating against a same-named type from an unrelated namespace.
- **A XAML file that cannot be parsed is never silently dropped.** It would otherwise remove its keys
  from the resource index and make every one of them look unresolved. `xaml_validate` and `xaml_resolve`
  report how many files were unreadable and switch unresolved-resource checking off while any are;
  `scope=solution` reports the unparseable file itself as `XAML000`.
- **The XAML walk does not follow directory junctions or symlinks**, which a self-referential link would
  otherwise turn into an unbounded traversal.
- **`find_usages` names the member each usage sits in, and whether it is production or test code.**
  A record was `path  EXACT  ref  12:5, 40:9` — enough to find the file, never enough to end the
  investigation, so the agent opened the file anyway. It is now
  `path  EXACT  ref  src  12:5, 40:9`, and with `containers=true`
  `path  EXACT  ref  src  in OrderRouter.Route  12:5, 40:9`. The containing declaration comes from the
  document's syntax tree, which is already parsed, so it costs no compilation; the `src`/`test` column
  comes from whether the owning project references a test framework. Naming the member splits the
  answer into one line per member rather than per file, which on a widely-used symbol measured 3× the
  tokens, so it is off by default. This is a response-format change.
- **Every edit reports the diagnostics it leaves behind.** `EditGate` already compiled the changed
  projects and their dependents to decide whether to roll back, then threw the numbers away. Each
  mutation — and each `dryRun` — now carries `errors=N (+D) warnings=N (+D)`, so an agent stops issuing
  a separate `analyze` after every edit, and `dryRun` becomes a real preview. The delta alone is not a
  rollback oracle — one error can disappear while another appears, leaving `(+0)` on an edit that would
  be refused — so a `dryRun` that would be rolled back also says `WARNING … would be rolled back` and
  names the errors it introduces. `allowErrors=true` still skips the analysis and reports no counts;
  it is also the way to get the old cheap diff-only preview back, since the gate now compiles the
  changed projects and their dependents on `dryRun` too.
- **A symbol can be addressed by name, not only by its documentation id.** `M:Trading.OrderService.Submit(Trading.Order)`
  is 60 characters an agent has to reproduce byte-exactly, and one typo cost a whole round trip. Every
  tool that takes a `symbolId` now also accepts `OrderService.Submit`, `Submit`, or
  `OrderService.Submit(Order)` when a parameter count disambiguates an overload. A name that matches
  one symbol resolves; a name that matches several returns `ERROR AmbiguousSymbol` listing their full
  ids — which is the disambiguation call the agent would have had to make anyway — and a name that
  matches nothing names the nearest symbols. Documentation ids keep working exactly as before.
  The qualifier may be as long as you like: `OrderService.Submit`, `Trading.OrderService.Submit` and
  `Fixture.Trading.OrderService.Submit` all resolve, so pasting back an id with the `M:` removed works.
  A name is never resolved by guessing: a qualifier only matches a containing **type** (or a namespace
  when the symbol is itself a type), a parameter list is counted at nesting depth zero so a generic
  argument's comma cannot select the wrong overload, the candidate list declares how many of the total
  it is showing, and a name matching more than 100 symbols is refused outright rather than resolved
  from a truncated search.
- **The token budget suite covers the widest symbol, not only the narrow one.** `find_usages` was
  asserted against a 4-usage fixture symbol, which a format change that tripled the cost on a
  46-usage symbol passed unchanged. There is now a budget on the widest symbol in the fixture and an
  assertion that the default answer costs less than the `containers=true` one.
- **Outlines name members the short way, and the name they print is a name every tool accepts.**
  `get_file_outline` and `get_type_outline` emitted a documentation comment id on every line —
  `M:TerseSharp.Core.ReferenceService.FindUsagesAsync(TerseSharp.Core.LoadedWorkspace,Microsoft.CodeAnalysis.ISymbol,System.Int32,System.Threading.CancellationToken)~System.Threading.Tasks.Task{System.String}`
  is 205 characters against 125 for the signature beside it, so roughly 60% of every member line was
  an id derivable from the rest of the line. They now print `ReferenceService.FindUsagesAsync(LoadedWorkspace, ISymbol, int, CancellationToken)`,
  which resolves back to the same symbol through the name resolution above. The short form is used
  **only where it round-trips**: a constructor, destructor, operator, indexer, explicit interface
  implementation, generic method or member of a generic type keeps its documentation id, because a
  name cannot address those — an E2E test feeds every reference each outline prints back into
  `get_symbol` and asserts none of them errors. `ids=full` prints documentation ids for everything,
  and any other value is refused rather than silently treated as `short`. The outline budget test
  tightened from two thirds of the file it replaces to half. This is a response-format change.
- **A truncated answer names the parameter that narrows it.** `truncated=true, total=412` told an
  agent it was missing results without saying what to do, so the usual next move was to re-run with a
  bigger `maxResults` and pay for the whole list. Every listing tool now appends
  `- narrow with <parameter>` when, and only when, it truncated: `glob=` for text search, `severity=`,
  `ids=` or `path=` for diagnostics, `depth=` or `filter=` for a XAML outline, `kind=` for XAML search.
- **`rename_symbol` rewrites the XAML that names the symbol, and `find_usages` shows it.** Renaming a
  code-behind handler left `Click="OnSubmit"` pointing at a method that no longer exists, and renaming
  a bound property left `{Binding Symbol}` bound to nothing — neither is a compile error in WPF, so the
  compile gate certified a broken UI as clean. Both now travel with the rename, and both appear in
  `find_usages` so the blast radius is visible first. The rewrite happens **only** where an `x:Class`
  or an `x:DataType`/`d:DataContext` proves the reference is to that member; a binding with no declared
  context is listed as `NOT rewritten` rather than rewritten on a guess.
- **`xaml_codebehind`** reports the `x:Class` a file binds to and every event handler it names, with
  the element and event each sits on, instead of reading the `.xaml.cs` to find out what the markup
  wires up.
- **`xaml_set_property`** sets or adds one attribute on one element, addressed by the element path
  `xaml_outline` prints, `#Name` or `key=Key`. It edits the tag in place so the file's formatting
  survives, returns a diff like every other mutation, honours `dryRun` and `--read-only`, and refuses
  an edit whose result would not parse rather than writing broken markup. This replaces line-based
  `Edit` on the file shape agents are measured worst at.
- Test count: 232 unit and 285 E2E.

## [0.6.0] - 2026-07-31

### Fixed

- **Central Package Management was inferred from a file name.** Any `Directory.Packages.props` on the
  way up made `package_add` write the version there and leave the reference version-less — even when
  the file sets `ManagePackageVersionsCentrally` to `false`, or does not set it at all, in which case
  NuGet is not managing versions centrally and a version-less `PackageReference` does not restore.
  The property must now say so, and only the nearest file is consulted, as MSBuild does. The property
  is an ordinary MSBuild property, so the project file and every `Directory.Build.props` up to the
  workspace root are consulted too; a value that is an unresolved MSBuild expression is treated as
  enabled, because writing a version into a CPM project fails the restore with NU1008.
- **`find_implementations` had no result cap.** Every other listing tool declares `truncated`/`total`
  and caps; this one returned every implementation of an interface, which on a wide abstraction is an
  unbounded response. It takes `maxResults` (default 100) like its siblings.
- **A single enormous line could blow the `read_text` response budget** by its own length, because the
  budget was charged after the line was appended. A line that would exceed the remaining budget is now
  truncated with a `(+N chars)` marker.
- **A multi-gigabyte file could exhaust memory during a text search.** `StreamReader.ReadLine`
  materialises one line at a time, which is no protection against a file with no newlines. Content
  search skips files over 16 MB and says how many it skipped; `find_files` still lists them.
- **`PositionFormat.Relative` returned an empty string** for a diagnostic with no file, where the rest
  of the codebase renders `-`.
- **Two more E2E suites leaked a server process each.** The fixture leak fixed in 0.4.0 was fixed only
  in the shared fixture; `CompileGateE2ETests` and `ReadOnlyServerE2ETests` each start their own
  server and still relied on disposing the client alone. All three now go through one
  `TerseServerProcess` helper that owns the process and kills the tree on teardown — including when
  the MCP handshake itself fails, which is the case that used to strand a server holding MSBuild
  locks on the fixtures.

### Changed

- **`undo_last_change` and `unload_workspace` answer with a header line** like every other tool,
  instead of a bare sentence. This is a response-format change; the text of the outcome is unchanged
  and still on its own line.

### Added

- **A positive-path matrix over the whole tool surface.** `ToolHappyPathE2ETests` calls every tool
  with valid arguments and asserts a non-`ERROR` response headed by the tool's own name. Until now the
  robustness sweep only proved that tools *fail* well — a server that answered `ERROR` to everything
  would have passed it. A completeness test forces every advertised tool to be either on the matrix or
  in a named exclusion list, so a new tool cannot arrive untested. Mutating tools run with
  `dryRun: true`. Each case asserts a record only that tool can produce, so a tool that resolves
  nothing and returns an empty body fails; a header alone is not a pass. The four process-spawning
  tools and four whose success path the fixture cannot express are listed explicitly, and a second
  test fails if that list names a tool the server no longer advertises.
- **A read-only sweep.** Every one of the 22 mutating tools is called against a `--read-only` server
  and must answer `ERROR ReadOnly`, so a new mutating tool that forgets its `RejectWrite()` gate is
  caught rather than silently writing.
- **Negative coverage for `build`, `run_tests` and `list_tests`**, which every sweep had excluded:
  a project outside the workspace, and `test` combined with `filter`.
- Test count: 167 unit and 237 E2E.

## [0.5.0] - 2026-07-31

### Fixed

- **`package_add` could write outside the workspace it was given.** With a blank `project` the path
  resolved to the workspace root itself, passed the containment check, and the Central Package
  Management lookup then walked *parent directories without any boundary* until it found a
  `Directory.Packages.props` — in a nested checkout that is the outer repository's file, which it
  edited. Found by the new robustness sweep, which corrupted this repository's own
  `Directory.Packages.props` on its first run. The lookup now stops at the workspace root, a blank
  package id or path is refused, and a sentinel test asserts no tool writes outside the workspace.
- **`solution_add_project` accepted anything.** A blank path added `<Project Path="." />` to the
  solution. A blank path is refused and the target must end in `.csproj`, `.fsproj` or `.vbproj`.
- **`package_list` reported success for a project that does not exist**, answering `0 references`
  instead of `ERROR DocumentNotFound` — an agent would conclude the project had no dependencies.
- **`package_list` and `project_properties` read project files outside the workspace.** Unlike every
  write tool they never went through the containment guard, so
  `package_list(project:"../../../elsewhere/App.csproj")` returned that file's references — and, once
  `package_list` learned to fail on a missing file, became a filesystem-existence probe. Both are
  contained now.
- **A parallel failure could escape as an untyped error.** `Parallel.ForEachAsync` surfaces
  `AggregateException`, which `ToolBoundary` did not recognise, so an expected inner failure would
  have been rethrown instead of rendered. Aggregates are unwrapped and rendered like any other.

### Changed

- **Every reported path is workspace-relative.** `find_usages`, `find_implementations`,
  `search_symbols`, `get_symbol`, `get_symbol_source`, `analyze`, `get_diagnostics` and the dead-code
  findings printed absolute paths, repeating the workspace root on every record. Paths outside the
  workspace are still printed in full. This is a response-format change.
- **`get_file_outline` and `get_type_outline` take `signatures` (default `true`).** With
  `signatures=false` the outline is ids, accessibility and line ranges only — measured on
  `EditGate.cs` at 50% of the raw file against 71% with signatures. The default is unchanged.
- **`build` recovers from its own file locks.** It now runs without holding the workspace lease, and
  when MSB3021/MSB3027 or "being used by another process" appears it unloads the workspace, retries
  the build, reloads, and says so. Symbol ids are unaffected; `undo_last_change` history is
  discarded, which the response states. When the retry is still blocked the response says that too,
  and names the real cause: a running process that owns the file — a server started from the output
  directory being rebuilt — which unloading a workspace cannot release.
  The retry only runs when exactly one workspace is loaded: unloading one of several would let an
  unhinted call silently resolve to the wrong checkout during the rebuild, which is the one failure
  `AmbiguousWorkspace` exists to prevent. A reload that fails is reported rather than swallowed.
- **The advertised tool schema is smaller.** Repeated parameter descriptions were trimmed:
  `tools/list` went from 7,488 to 7,121 tokens — a fixed cost paid on every session.

### Performance

- **The per-project loops run in parallel** in `analyze`, `get_diagnostics`, dead-code analysis,
  `search_symbols` and symbol-id resolution, bounded by processor count. Dead-code analysis also
  parallelises across candidate members, and its outer project loop is sequential so the two levels
  cannot multiply into `ProcessorCount²` concurrent solution-wide searches. Output is unchanged and
  deterministic: results are collected per project and flattened in project order — never in
  completion order — then grouped and sorted before rendering. Four stress tests assert byte-for-byte
  identical answers across repeated runs, verified on this repository's own solution as well as the
  fixture.

### Added

- **A robustness sweep over the whole advertised surface.** `ToolRobustnessE2ETests` reads
  `tools/list` from the running server and calls every tool with garbage arguments, with no
  arguments and with empty strings, asserting each answers a structured response with a `remedy:`
  line, never a stack trace, and that the server is still healthy afterwards. New tools are covered
  automatically. Alongside it, `ToolEdgeCaseE2ETests` (inverted ranges, ranges past EOF, negative
  line numbers, invalid and catastrophic regexes, malformed symbol ids, non-C# files, blank
  arguments, out-of-workspace paths) and `ToolStressE2ETests` (determinism under repetition,
  40 concurrent calls, oversized `maxResults`, a 20,000-character pattern).
- Test count: 144 unit and 159 E2E.

## [0.4.0] - 2026-07-31

### Fixed

- **`read_text` refused every file over 64 KB, including when a line range was asked for.** The size
  check ran before the range was applied, so a 194 KB file answered
  `'…' is 194048 bytes, over the 65536 byte cap` with the remedy `pass startLine and endLine to read
  a range` — advice the caller had already followed. An agent that hit this had no way forward inside
  the server and fell back to reading the file with a built-in tool, which is the one outcome this
  project exists to prevent. The cap is gone: `read_text` streams the file, returns the lines asked
  for, and truncates instead of refusing. It never materialises the whole file, so a multi-gigabyte
  file costs a scan rather than the memory.
- **`analyze` scoped to one file reported findings from other files, all of them generated.**
  `analyze path=src/…/FileGlob.cs` answered with five `CS8019` diagnostics, every one of them in
  `obj/Debug/net10.0/*.g.cs`, and none in the file that was asked about: the dead-code findings never
  received the path filter, and generated output was never excluded. The tool that the "check every
  file you touched" workflow depends on was returning pure noise. Dead-code findings now honour the
  path, and `obj/`, `bin/`, `*.g.cs`, `*.designer.cs`, `AssemblyInfo.cs` and `AssemblyAttributes.cs`
  are excluded from `analyze` and `get_diagnostics` alike — **except at `Error` severity**, where a
  generated file's diagnostic is a real build break and is always reported, and except when the
  generated file is the one named in `path`.
- **`get_file_outline` could not see enums or delegates.** The outline collected
  `TypeDeclarationSyntax` only, so a file declaring nothing but an enum answered `0 types` and an
  agent reasonably concluded the file was empty — then read it with a built-in tool. Enums, their
  members, and delegate declarations are now listed.
- **One unreadable file failed an entire search.** `search_text`, `search_regex` and `find_files`
  walked with a single `EnumerateFiles`, so an `IOException` on one locked file — or a denied
  directory — aborted the whole call. Directory and file enumeration, opening and reading are each
  isolated now; an unreadable entry is skipped and the search completes.
- **A workspace evicted while a call was using it was disposed under that call's feet.** LRU eviction
  and `unload_workspace` disposed the `MSBuildWorkspace` immediately, so an in-flight tool call could
  observe a cleared solution or an `ObjectDisposedException` that carried no error code and no remedy.
  `WorkspaceRegistry.Resolve` now hands out a `WorkspaceLease`; disposal waits for the last lease to
  be released. `ObjectDisposedException` and `OperationCanceledException` are also rendered as proper
  `ERROR` records rather than escaping as untyped failures.
- **The compile gate ignored the projects an edit could break.** `EditGate` compared error counts only
  in the projects holding the changed documents, so changing a public signature broke every dependent
  project while the edit was reported as applied. The gate now also compiles the projects that
  transitively depend on the changed ones.
- **The undo history could interleave.** `TryApply` recorded the previous solution outside the lock
  that guards the history, so two concurrent edits could record the wrong snapshot. Applying and
  recording are one critical section now.
- **The E2E suite left orphaned `terse serve` processes behind**, whose file locks then broke the next
  build. The fixture owns the server process itself and kills the process tree on teardown.

### Changed

- **`find_usages` groups its results per file.** A file with twelve usages was twelve lines, each
  repeating the full path; it is now one line — `path  EXACT  ref  12:5, 40:9, 77:3` — with a separate
  line per distinct confidence and reference kind. This is a response-format change.
- **`read_text` takes `maxLines`** (default 2000) and caps a response at 128 KB of text, reporting the
  cut through the existing `truncated`/`total` fields instead of returning an unbounded file.
- **Search results no longer carry a whole minified line.** A match line over 200 characters is cut
  and annotated with how many characters were dropped.
- **`search_regex` runs on the non-backtracking engine** where the pattern allows it, so a
  catastrophic pattern costs linear time rather than a two-second timeout per line; patterns needing
  backreferences or lookaround fall back to the backtracking engine with that timeout.
- **`get_symbol_source` returns every part of a partial declaration** instead of an arbitrary one.
- **`MsBuildBootstrap` prefers the MSBuild instance matching the running runtime's major version**
  rather than the highest installed, so a preview SDK on the machine no longer breaks workspace load.
- **`build` names a locked output file when it sees one**, pointing at `unload_workspace` instead of
  leaving MSB3021/MSB3027 to be read out of raw build output.

### Performance

- **Dead-code analysis no longer searches the whole solution per member.** `analyze` runs with
  `includeDeadCode` on by default and issued one solution-wide `FindReferencesAsync` for every private
  member — on a solution with thousands of private members, thousands of full-solution searches. A
  private member can only be referenced inside its containing type's declaring documents, so the
  search is scoped to those.
- **Searches no longer walk `.git`, `bin`, `obj` and `node_modules` before discarding them.** The walk
  prunes excluded directories as it descends instead of enumerating everything and filtering after,
  stops on the first NUL byte, and computes each file's relative path once instead of three times.
  `search_text` and `search_regex` additionally skip known-binary extensions without opening them;
  `find_files` still lists those files, because locating a `.png` is not the same as reading one.
- **Resolving a symbol id no longer compiles every project.** `SymbolLookup` narrows to the projects
  whose declaration index contains the name before asking for a compilation, falling back to the full
  set when the name cannot be derived from the id.
- **`DocumentLookup` compares file names before normalising paths**, replacing one `Path.GetFullPath`
  per document in the solution with one per same-named document.
- **Server GC and TieredPGO are enabled** for the server process, which holds Roslyn compilations.

### Fixed — test integrity

- **The token-budget test could not fail.** `get_file_outline` was asserted to cost less than *twice*
  the file it replaces, which passes even if the outline is larger than the file. The assertion is now
  a real budget — two thirds of the file — measured against a body-heavy fixture rather than an
  eighteen-line one. On that fixture the outline costs 261 tokens against 456 for the file: a 43%
  saving, well short of what the fully-qualified ids in every member line could allow.

## [0.3.1] - 2026-07-31

### Fixed

- **`replace_symbol` and `add_member` silently dropped every member after the first.**
  `SyntaxFactory.ParseMemberDeclaration` returns only the first member and reports the rest as
  diagnostics on the node, which were never inspected. A declaration holding four methods replaced one
  and discarded three, answering `replace_symbol applied` with `0 files changed` — an agent that did
  not re-read the file believed the edit had landed. Both tools now refuse a declaration that is not
  exactly one member, with `ERROR InvalidArgument` naming the parse errors.
- **A glob with a directory in it made `find_files` and `search_text` fail.** The glob went straight
  to `Directory.EnumerateFiles` as its `searchPattern`, which rejects `**` and path separators, so
  `**/Views/*.xaml` returned `ERROR InvalidArgument IOException: The filename, directory name, or
  volume label syntax is incorrect` instead of matching. Path-shaped globs are now matched against
  each file's workspace-relative path, with `**/` meaning "any directories or none", `*` and `?`
  confined to one segment; a bare glob such as `*.csproj` still matches on the file name.

### Changed

- **A bare glob now follows glob rules rather than Win32 wildcard rules.** Matching no longer goes
  through `Directory.EnumerateFiles`, so the DOS quirks it inherited are gone: `*.*` matches only
  names that contain a dot (it used to match every file, extensionless ones included), `Order?.cs`
  no longer matches `Order.cs` (`?` now requires exactly one character), and a trailing `.` is
  literal. Common globs — `*.cs`, `*`, `Order*.cs`, `*.c?` — are unaffected.

## [0.3.0] - 2026-07-31

### Added

- **`run_tests` reports statistics.** Every run now carries
  `passed= failed= skipped= total= durationMs= exitCode= elapsedMs=`, on green runs too.
- **`run_tests` selects what to run.** `test=` takes a fully-qualified test name or a class or
  namespace prefix, `filter=` still takes a raw VSTest expression, and passing both is refused with
  `ERROR InvalidArgument`. `noBuild=true` reuses the existing binaries, `includePassed=true` lists
  passing tests, `slowest=N` ranks the slowest, and `timeoutSeconds=` replaces the fixed 10-minute cap.
- **`rerun_failed`** re-runs only the tests that failed in the previous `run_tests` call.
- **`list_tests`** names the tests a project or solution contains without running them, with an
  optional `contains=` substring.

### Fixed

- **The server was unreachable on a large solution.** `serve` loaded the whole workspace before it
  started the stdio transport, so `initialize` went unanswered until the load finished. The MCP
  client cancels `initialize` after a fixed 60 s - which `MCP_TIMEOUT` does not raise - so a
  158-project solution failed to connect with `-32001 Request timed out` while small ones were fine.
  The transport now starts first and the workspace loads in the background: `initialize` answers in
  ~1 s regardless of solution size, and the first tool call that needs the workspace waits for the
  load to finish rather than reporting `WorkspaceNotLoaded`. A preload that fails is reported by
  `list_workspaces` instead of being lost.
- **`run_tests` counted output lines, not tests.** A run with 2 failures reported `5 failures`,
  because the header, the message and the final summary line each matched the failure regex. Counts
  now come from the run's TRX report.
- **`run_tests` dropped everything an agent needs to fix a test.** The exception type and message,
  xunit's `Expected:`/`Actual:` values and the whole stack trace were discarded, leaving
  `Error Message:` with nothing after it. Each failure now reports its message (capped at 12 lines)
  and one workspace-relative `file:line` frame, with framework frames skipped.
- **`run_tests` merged two tests that failed with the same message** into one line, and printed the
  run summary twice.
- **A filter that matched nothing looked like a green run** — `0 failures`, `exitCode=0`. It now says
  `WARNING no test matched filter '<expr>'; this is not a green run`.
- **A run that produced no results still printed a `0 failures` headline.** A missing project or a
  crashed runner now reports `FAILED …, no test results were produced` followed by the output tail.
- **`terse install` honours `CLAUDE_CONFIG_DIR`.** Claude Code reads `$CLAUDE_CONFIG_DIR/.claude.json`
  when that variable is set, so registering into `~/.claude.json` left the server invisible to the
  agent. The skill from `install --skill` follows the same directory (`$CLAUDE_CONFIG_DIR/skills`).
- **`terse doctor` verifies registration, not file existence.** The `clients` line now reports only
  clients whose config actually contains the `terse-sharp` entry, and names the config path it read.
  A config that is not valid JSON is reported as such instead of ending the whole diagnostic.
- **`terse install` with no `--client` no longer exits silently.** A client whose config directory
  does not exist yet is still registered, and a run that matches nothing says `no MCP clients matched`
  rather than printing an empty line.
- **A client config that is not valid JSON is skipped, not overwritten.** `install` and `uninstall`
  report `skipped <client> (not valid JSON: <path>)` and carry on with the other clients instead of
  ending on an unhandled parser exception; `doctor` reports the registered clients and the invalid
  files in the same line.

## [0.2.2] - 2026-07-30

### Changed

- The README and NuGet README license sections just say MIT and link the licence file.

## [0.2.1] - 2026-07-30

### Changed

- **`find_dead_code` is gone; `analyze` reports dead code itself.** One call now returns compiler
  diagnostics, analyzer diagnostics and dead code in a single deduplicated list. Unreferenced private
  members appear as `TERSE001` in category `DeadCode`, alongside the compiler's own unused-field and
  unreachable-code hints, and can be isolated with `ids=TERSE001`. Pass `includeDeadCode=false` to
  skip the reference scan on a very large solution.
- README and the NuGet README are in sync; the keyword blob is gone and the license section reads
  like one.

## [0.2.0] - 2026-07-30

Doubles the tool surface from 26 to 52, all Roslyn-only.

### Added

- **Analysis and cleanup, without any external tool or licence** — `analyze` runs the compiler plus
  every analyzer the project already references, down to `info` and `hidden` severity that a normal
  build hides; `format` applies the Roslyn formatter to your `.editorconfig`; `cleanup` removes
  unused `using` directives, sorts the rest System-first and reformats; `find_dead_code` reports
  unreferenced private members, unused fields and unreachable code.
- **Refactorings** — `extract_interface`, `move_type_to_file`, `move_type_to_namespace`,
  `change_signature`, and `undo_last_change` backed by a 10-deep solution snapshot history.
- **Projects and solutions** — `solution_projects`, `solution_add_project`, `solution_remove_project`
  with full `.slnx` support, `project_create`, `project_properties`, `project_set_property`,
  `project_add_reference`, `project_remove_reference`, and Central-Package-Management-aware
  `package_list` / `package_add` / `package_remove`.
- **XAML** — `xaml_outline`, `xaml_names`, `xaml_resources`, `xaml_bindings`, `xaml_validate` and
  `xaml_find`, with WPF, Avalonia, WinUI and MAUI dialect detection. Validation reports duplicate
  `x:Key` and `x:Name` and unresolved `StaticResource` references.
- **Token-budget suite** — the response sizes advertised in the README are now asserted in CI rather
  than estimated.

### Changed

- Debugging and profiling are dropped from the roadmap. A debugger needs a live session and a
  profiler needs a trace host; both are separate products.

## [0.1.1] - 2026-07-30

### Fixed

- The NuGet package README rendered as literal HTML markup on nuget.org. The repository README is
  written with centred HTML for GitHub, which nuget.org's renderer does not support, so the package
  now ships a dedicated pure-Markdown README with absolute links.
- Releases authenticate to nuget.org with **trusted publishing** (GitHub OIDC) rather than a stored
  API key. The release job runs in the `production` environment with `id-token: write`.
- The release action took its tag from `github.ref`, so a `workflow_dispatch` run would have created
  a GitHub release named after the branch instead of the tag. The tag is resolved once for both
  triggers and passed explicitly, which also fixes the prerelease flag on dispatched runs.
- `PathBoundary` compared paths case-insensitively on every platform. On Linux, where the file system
  is case-sensitive, that widened containment: `/repo` would accept a path under `/REPO`. Comparison
  is now ordinal on Linux and case-insensitive elsewhere.
- `SECURITY.md` claimed `--read-only` removes the mutating tools; it refuses them at call time.

## [0.1.0] - 2026-07-30

First release. A Roslyn-backed MCP server that lets a coding agent navigate, read, edit and refactor
a .NET solution semantically instead of reading whole files.

### Added

- **26 MCP tools** over stdio:
  - workspace — `load_workspace`, `workspace_status`, `list_workspaces`, `unload_workspace`, `list_projects`
  - navigation — `search_symbols`, `get_symbol`, `get_file_outline`, `get_type_outline`, `get_symbol_source`, `find_usages`, `find_implementations`
  - diagnostics — `get_diagnostics`
  - editing — `replace_symbol_body`, `replace_symbol`, `add_member`, `delete_symbol`, `rename_symbol`
  - files — `read_text`, `write_text`, `edit_text`, `find_files`, `search_text`, `search_regex`
  - build — `build`, `run_tests`
- **Symbol addressing** by Roslyn `DocumentationCommentId`, so edits survive line drift.
- **Multi-workspace registry** with LRU eviction, git worktree and branch awareness, and an explicit
  `AmbiguousWorkspace` error instead of guessing between checkouts of one repo.
- **Compact responses** — one record per line, explicit `truncated`/`total`, `EXACT`/`HEURISTIC`
  confidence tag on every record.
- **Edit safety** — `dryRun`, unified-diff-only responses, rollback when an edit introduces a new
  compile error, `allowErrors` to opt out, workspace-root containment on every path.
- **`terse` global tool** with `serve`, `install`, `uninstall` and `doctor` commands that write MCP
  client configuration directly, plus `install --skill` for the agent skill.
- **75 tests** — 29 unit and 46 E2E, where each E2E test drives a real server process over the real
  stdio transport against a real solution and asserts response values.

### Known gaps

XAML tooling, ReSharper command-line-tools integration, project/solution/package editing, the
content-addressed index, the trigram text index, debug and profiling modules, and the token/latency
benchmark harnesses are specified but not implemented.

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.40.0...HEAD
[0.40.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.40.0
[0.39.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.39.0
[0.38.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.38.0
[0.37.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.37.0
[0.36.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.36.0
[0.35.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.35.0
[0.34.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.34.0
[0.33.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.33.0
[0.32.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.32.0
[0.31.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.31.0
[0.30.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.30.0
[0.29.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.29.0
[0.28.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.28.0
[0.27.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.27.0
[0.26.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.26.0
[0.25.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.25.0
[0.24.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.24.0
[0.23.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.23.0
[0.22.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.22.0
[0.21.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.21.0
[0.20.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.20.0
[0.19.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.19.0
[0.18.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.18.0
[0.17.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.17.1
[0.17.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.17.0
[0.16.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.16.0
[0.15.2]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.15.2
[0.15.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.15.0
[0.14.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.14.0
[0.13.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.13.0
[0.12.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.12.0
[0.11.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.11.0
[0.10.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.10.0
[0.9.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.9.0
[0.8.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.8.0
[0.7.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.7.0
[0.6.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.6.0
[0.5.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.5.0
[0.4.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.4.0
[0.3.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.1
[0.3.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.3.0
[0.2.2]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.2
[0.2.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.1
[0.2.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.2.0
[0.1.1]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.1
[0.1.0]: https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/v0.1.0
- **`package_add` refuses when Central Package Management sits above the workspace root.** Bounding
  the lookup fixed the escape, but left a worse failure available: with the file out of reach the
  tool would have written `<PackageReference Include="X" Version="Y" />` into a CPM-managed project,
  which is an NU1008 build break reported as a successful diff. It now says where the file is and
  what to do instead. Loading the workspace at the repository root restores the normal behaviour.
