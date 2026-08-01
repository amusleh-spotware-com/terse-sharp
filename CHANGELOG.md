# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres
to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are derived from git tags
(`vMAJOR.MINOR.PATCH`) by [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

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

[Unreleased]: https://github.com/amusleh-spotware-com/terse-sharp/compare/v0.9.0...HEAD
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
