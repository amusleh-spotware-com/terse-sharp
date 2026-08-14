# Contributing to TerseSharp

## Build and test

```bash
dotnet build TerseSharp.slnx
dotnet test  TerseSharp.slnx
```

Requires the **.NET 10 SDK** (see `global.json`). Nothing else — no IDE, no licence, no Node.

## The two rules that are not negotiable

1. **A tool without an E2E test is not done.** Every advertised MCP tool has a named test in
   `tests/TerseSharp.E2ETests` that starts a real server process, talks real stdio JSON-RPC to the
   fixture solution, and asserts the **values** in the response — never "did not throw".
2. **A tool that does not beat the built-in it replaces does not ship.** If `get_type_outline` is not
   dramatically cheaper than `Read`, it has no reason to exist.

## Adding a tool

Read `IMPROVEMENTS.md` first. It is the measured backlog of what the current surface costs an agent,
and it carries the ranking rule: **improving an existing tool or its response format beats adding a
tool**, because every tool costs every session in tool-list tokens and in selection accuracy. A new
tool has to beat the one it splits, with the saving asserted in `TokenBudgetE2ETests`. Anything you
notice while working — a round trip that should have been one call, a response field you never read,
a moment you reached for `Read`/`Grep` instead — belongs in that file as a row.

1. Put the logic in `TerseSharp.Core` as a pure-ish service that returns `Result<string>`.
2. Expose it in `TerseSharp.Server/Tools/*.cs` with `[McpServerTool(Name = "snake_case_name")]`.
3. Give **every optional parameter a C# default** (`string? workspace = null`). Without a default the
   MCP SDK marks the parameter required and the tool fails at call time.
4. Write the `[Description]` for an agent: say what it returns *and which built-in it replaces*.
5. Add unit tests for the formatting and error paths, and one E2E test per tool.
6. Update `CHANGELOG.md` under `## [Unreleased]`.
7. If the change affects what users see on nuget.org, update `NUGET_README.md` too - it is a
   separate, pure-Markdown file because nuget.org does not render the HTML the GitHub README uses.

## Response style

Responses are data, not prose. One count line, one record per line, and nothing the caller did not
ask for - no header echoing the request, no preamble, no explanation, no closing summary, and no
"pass `verbose=true`" hint. The count line names the truncation only when there was one
(`4/17 usages truncated - narrow with maxResults=`). Every record carries `EXACT` or `HEURISTIC`.
`verbose=true` restores the verbatim shape - header and `(truncated=…, total=…)` - on every tool
that takes it. A record's own text is never rewritten to save characters: compression drops framing,
never payload.

## Code style

`Directory.Build.props` sets `TreatWarningsAsErrors`, so analyzer warnings fail the build — that is
deliberate. Immutable records, `sealed` by default, pattern matching over `if`/`else` ladders,
explicit `IFormatProvider` on every culture-sensitive format, and no comments: make the code say it.

Before opening a PR:

```bash
dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info   # or: cleanup verify=true fix=analyzers
dotnet format style     TerseSharp.slnx --verify-no-changes --severity info   # or: cleanup verify=true fix=style
```

## Releasing

See [RELEASING.md](RELEASING.md).
