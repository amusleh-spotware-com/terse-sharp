using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class NavigationTools(ToolContext context)
{
    [McpServerTool(Name = "search_symbols", ReadOnly = true)]
    [Description("Find declarations by name across the solution. Supports substring and CamelHump ('OSvc' finds OrderService). scope=src or scope=test keeps only the projects of that half, which is how a name the tests declare dozens of times stops burying the one production declaration. path= answers the matches that file declares first and searches the solution only when it declares none. Use instead of Grep for anything that is a type or member.")]
    public Task<string> SearchSymbols(
            [Description("Name or CamelHump pattern.")] string query,
            [Description("Optional kind filter: class, interface, method, property, field, enum.")] string? kind = null,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Max results (50).")] int maxResults = 0,
            [Description("Keep only one half of the solution: src for the production projects, test for the ones that reference a test framework. Empty searches both.")] string? scope = null,
            [Description("File the declarations are expected in. Its matches are answered first and the solution is searched only when it declares none; a path naming no document answers DocumentNotFound.")] string? path = null,
            CancellationToken cancellationToken = default)
    {
        var half = scope?.ToLowerInvariant();

        return half is null or "" or "src" or "test"
            ? context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => SearchAsync(loaded, query, kind, half, Cap(maxResults, 50), path, cancellationToken),
                cancellationToken: cancellationToken)
            : Task.FromResult(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"scope='{scope}' is not a known half of the solution"),
                "pass scope=src, scope=test, or leave it empty to search both").Render());
    }

    [McpServerTool(Name = "get_file_outline", ReadOnly = true)]
    [Description("List every type and member of a .cs file with signatures and line ranges, without the bodies. Use instead of Read on a .cs file. Pass paths to outline up to 10 files in ONE response. Replaces one call per file: each is rendered under its own path line and a path that does not resolve is reported inline as NOT_FOUND instead of failing the call. ref= outlines the file as it was at a git ref instead of in the working tree, so the pre-change shape of a file costs an outline rather than the whole text a git show returns; it takes one path. An unfiltered outline lists at most 40 members per type and counts the rest as 'N of M members - contains= or all=true'. parameterNames=false prints parameter types without their names, which is about an eighth of the response.")]
    public Task<string> GetFileOutline(
        [Description("Path to the .cs file.")] string? path = null,
        [Description("Several .cs files outlined in one response, at most 10. Replaces one call per file. Combines with path, which is taken first; a blank entry and an 11th entry are refused by name rather than dropped.")] string?[]? paths = null,
        [Description("Include member signatures. false gives ids and line ranges only, ~40% cheaper.")] bool signatures = true,
        [Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Also list the file's own using directives, so a new member's header can be written without reading source.")] bool usings = false,
        [Description("Print parameter names alongside their types. Default true; false keeps the types and drops the names.")] bool parameterNames = true,
        [Description("Keep only the members whose name contains this text, case-insensitively, under their declaring type with an 'N of M members' line.")] string? contains = null,
        [Description("List every member instead of the first 40 of each type; the default counts the rest.")] bool all = false,
        [Description("Git ref to outline the file at, e.g. main or HEAD~3, instead of shelling out for that revision's text. Takes one path, and the members are parsed from that revision's own text.")] string? @ref = null,
        CancellationToken cancellationToken = default)
    {
        var combined = PluralPaths.Combine(path, paths, "paths");

        if (!combined.IsOk)
            return Task.FromResult(combined.Error!.Render());

        if (@ref is { Length: > 0 } reference)
        {
            return combined.Value is [var only]
                ? context.WithWorkspaceAsync(
                    workspace,
                    only,
                    loaded => RefRead.OutlineAsync(loaded, only, reference, new OutlineOptions(signatures, ids ?? "short", usings, parameterNames, contains, all), cancellationToken),
                    semantic: false,
                    cancellationToken)
                : Task.FromResult(RefRead.Batched("paths=").Render());
        }

        return combined.Value is [var single]
            ? OutlinedAsync(single, signatures, ids, usings, parameterNames, contains, all, workspace, cancellationToken)
            : OutlinedManyAsync(combined.Value, signatures, ids, usings, parameterNames, contains, all, workspace, cancellationToken);
    }

    [McpServerTool(Name = "get_type_outline", ReadOnly = true)]
    [Description("Every member of one type with signatures and line ranges, without the bodies. Pass symbolIds to outline up to 20 types in ONE response. Replaces one call per type. An unfiltered outline lists at most 40 members and counts the rest as 'N of M members - contains= or all=true'. parameterNames=false prints parameter types without their names, which is about an eighth of the response. path= resolves a NAME inside that file first, so a name an outline just printed round-trips even when the solution holds others like it; a full documentation id already addresses one symbol, so path= does not apply to it.")]
    public Task<string> GetTypeOutline(
        [Description("Symbol id of the type.")] string? symbolId = null,
        [Description("Include member signatures. false gives ids and line ranges only.")] bool signatures = true,
        [Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        [Description("Print parameter names alongside their types. Default true; false keeps the types and drops the names.")] bool parameterNames = true,
        [Description("File the name lives in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
        [Description("Keep only the members whose name contains this text, case-insensitively, with an 'N of M members' line so nothing is dropped silently.")] string? contains = null,
        [Description("List every member instead of the first 40; the default counts the rest.")] bool all = false,
        [Description("Several types in ONE response, at most 20. Replaces one call per type: each under its own header line, an id that does not resolve reported inline as NOT_RESOLVED.")] string[]? symbolIds = null,
        CancellationToken cancellationToken = default) =>
        Outlined(
            Requested(symbolId ?? symbol, symbolIds),
            symbolIds is { Length: > 0 },
            workspace,
            new OutlineService.TypeOutlineFormat(signatures, ids ?? "short", parameterNames, contains, all),
            path,
            cancellationToken);

    [McpServerTool(Name = "get_symbol", ReadOnly = true)]
    [Description("Signature, kind, accessibility, location and XML doc of one symbol. Pass symbolIds to describe several in ONE response. Replaces one call per symbol, and it is the batch shape get_symbol_source and get_type_outline already take: each under its own block, with an id that does not resolve reported inline as NOT_RESOLVED instead of failing the call, and a summary counting the ids that RESOLVED. path= resolves a NAME inside that file first, so a name an outline just printed round-trips even when the solution holds others like it; a full documentation id already addresses one symbol, so path= does not apply to it.")]
    public Task<string> GetSymbol(
    [Description("Symbol id, e.g. M:Trading.OrderService.Submit(Trading.Order).")] string? symbolId = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Return the XML documentation verbatim and echo the request. Default false.")] bool verbose = false,
    [Description("Alias for symbolId.")] string? symbol = null,
    [Description("File the name lives in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
    [Description("Several symbol ids described in one response. Replaces one call per symbol; an id that does not resolve is reported inline as NOT_RESOLVED rather than failing the call.")] string[]? symbolIds = null,
    CancellationToken cancellationToken = default) =>
    Described(Requested(symbolId ?? symbol, symbolIds), symbolIds is { Length: > 0 }, workspace, verbose, path, cancellationToken);

    [McpServerTool(Name = "get_symbol_source", ReadOnly = true)]
    [Description("Return only that member's source text and line range. Use instead of reading the whole file to see one method. A **type** id answers get_type_outline's member list plus a steer to one member instead of the whole class's source, because that is almost never the question; verbose=true returns the type's source. Pass symbolIds to get several members in one response. Replaces one call per member, and each id that does not resolve is reported inline as NOT_RESOLVED rather than failing the call. path= resolves each name inside that file first, so a name an outline just printed round-trips even when the solution holds others like it. The source is dedented; pass verbose=true for it verbatim, and comments=false to drop the doc comments and inline comments when you are orienting rather than editing - worth about a tenth of the tokens on a documented codebase and nothing on one that carries no comments.")]
    public Task<string> GetSymbolSource(
[Description("Symbol id of the member.")] string? symbolId = null,
[Description("Several symbol ids returned in one response. Replaces one call per member; an entry that does not resolve answers NOT_RESOLVED inline rather than failing the call.")] string[]? symbolIds = null,
[Description("Workspace or worktree name.")] string? workspace = null,
[Description("Return the source verbatim, with its original indentation and blank lines. Default false.")] bool verbose = false,
[Description("Alias for symbolId.")] string? symbol = null,
[Description("Include doc comments and inline comments. Default true; false drops them, which is the cheap read when you only need the shape.")] bool comments = true,
[Description("File the names live in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
CancellationToken cancellationToken = default) =>
SourceOf(Requested(symbolId ?? symbol, symbolIds), symbolIds is { Length: > 0 }, workspace, new SourceFormat(verbose, comments), path, cancellationToken);

    private Task<string> SourceOf(string[] requested, bool batched, string? workspace, SourceFormat format, string? path, CancellationToken cancellationToken) => requested switch
    {
        [] => Task.FromResult(Errors.Blank("symbolId", "symbol", "symbolIds").Render()),
        [var only] when !batched => context.WithSymbolAsync(workspace, only, async (loaded, resolved) =>
            Unwrap(await SourceService.OfSymbolAsync(loaded, resolved, format, cancellationToken).ConfigureAwait(false)), cancellationToken, path, referenced: true),
        _ => context.WithWorkspaceAsync(
            workspace,
            path,
            loaded => SourceService.OfSymbolsAsync(loaded, requested[..Math.Min(requested.Length, MaxBatchedSymbols)], format, cancellationToken, path),
            cancellationToken: cancellationToken),
    };

    private const int MaxBatchedSymbols = 20;

    private static string[] Requested(string? single, string[]? many) =>
    [
        .. single is { Length: > 0 } ? new[] { single } : [],
        .. (many ?? []).Where(id => id is { Length: > 0 }),
    ];

    [McpServerTool(Name = "find_usages", ReadOnly = true)]
    [Description("Every real reference to a symbol, resolved semantically, one line per file with a src/test marker. Use instead of Grep for a type or member name; comments and unrelated matches are excluded.")]
    public Task<string> FindUsages(
        [Description("Symbol id to find references for.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Also name the member each usage sits in, one line per member instead of per file (default false).")] bool containers = false,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ReferenceService.FindUsagesAsync(loaded, resolved, Cap(maxResults, 100), containers, cancellationToken), cancellationToken);

    [McpServerTool(Name = "find_registrations", ReadOnly = true)]
    [Description("Where a type is registered in a dependency-injection container - AddSingleton, AddScoped, AddTransient, keyed and TryAdd variants - with the member each call sits in. Grep cannot answer this when the registration uses an open generic, a factory delegate or an Add* extension method. Says so explicitly when nothing matches, rather than implying the type is unregistered. It takes symbol= and name= as aliases for query=, the names the symbol-addressed tools beside it declare; an empty query still lists every registration, and a call carrying none of the three is refused naming all three.")]
    public Task<string> FindRegistrations(
        [Description("Type name to look for, e.g. IOrderRepository. Empty lists every registration.")] string? query = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Alias for query.")] string? symbol = null,
        [Description("Alias for query.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        (query ?? symbol ?? name) is { } wanted
            ? context.WithWorkspaceAsync(workspace, null, loaded =>
                RegistrationService.RegistrationsAsync(loaded, wanted, Cap(maxResults, 100), cancellationToken),
                cancellationToken: cancellationToken)
            : Task.FromResult(Errors.Blank("query", "symbol", "name").Render());

    [McpServerTool(Name = "list_endpoints", ReadOnly = true)]
    [Description("Every ASP.NET Core endpoint registration in the solution - MapGet, MapPost, MapControllers, MapHub and friends - with the member each sits in. Use instead of grepping Program.cs and every extension method it calls.")]
    public Task<string> ListEndpoints(
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            RegistrationService.EndpointsAsync(loaded, Cap(maxResults, 200), cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "explore_symbol", ReadOnly = true)]
    [Description("Replaces the get_file_outline then get_symbol_source pair, and the search_symbols then get_symbol_source pair, when you are learning what a symbol IS rather than editing it: one call gives its signature, XML doc and location, how many usages it has in src and in tests, how many implementations and XAML sites, and the files it is used in.")]
    public Task<string> ExploreSymbol(
        [Description("Symbol id or name.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ExploreService.ExploreAsync(loaded, resolved, cancellationToken), cancellationToken);

    [McpServerTool(Name = "impact_of", ReadOnly = true)]
    [Description("Replaces find_usages then find_implementations then reading the project graph, before a rename or a signature change: one call gives every file that references the symbol with a src/test marker, every XAML site, and every project that would recompile. tests=true adds the test classes that reference it, each as a ready run_tests test= argument, so a targeted suite run needs no second search.")]
    public Task<string> ImpactOf(
        [Description("Symbol id or name.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max records (200).")] int maxResults = 0,
        [Description("Alias for symbolId.")] string? symbol = null,
        [Description("Also list the test classes that reference this symbol, each as a ready run_tests test= argument. They are the DIRECT references only, so they narrow a run rather than replacing one. Default false.")] bool tests = false,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ExploreService.ImpactAsync(loaded, resolved, Cap(maxResults, 200), tests, cancellationToken), cancellationToken);

    [McpServerTool(Name = "find_implementations", ReadOnly = true)]
    [Description("Implementations of an interface or abstract member, and derived types of a base type.")]
    public Task<string> FindImplementations(
        [Description("Symbol id of the interface, abstract member or base type.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ReferenceService.FindImplementationsAsync(loaded, resolved, Cap(maxResults, 100), cancellationToken), cancellationToken);

    [McpServerTool(Name = "get_diagnostics", ReadOnly = true)]
    [Description("Compiler diagnostics from the Roslyn compilation, deduplicated. Use instead of parsing dotnet build output. Does not yet run the project's analyzers - use build for those.")]
    public Task<string> GetDiagnostics(
        [Description("File to scope to.")] string? path = null,
        [Description("Minimum severity: error, warning, info. Default warning.")] string? minSeverity = null,
        [Description("Alias for minSeverity.")] string? severity = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded =>
            DiagnosticsService.CollectAsync(loaded, path, Severity(minSeverity ?? severity), Cap(maxResults, 100), cancellationToken),
            cancellationToken: cancellationToken);

    private static async Task<string> SearchAsync(
            LoadedWorkspace workspace,
            string query,
            string? kind,
            string? scope,
            int maxResults,
            string? path,
            CancellationToken cancellationToken)
    {
        var scoped = ScopedFile(workspace, path);

        if (!scoped.IsOk)
            return scoped.Error!.Render();

        var found = await SymbolSearch.FindAsync(workspace, query, kind, scope, maxResults, cancellationToken, foldTests: true, inFile: scoped.Value).ConfigureAwait(false);

        var components = found.Scoped
            ? []
            : await RazorUsageService.DeclarationsAsync(workspace, query, cancellationToken).ConfigureAwait(false);

        var declared = scope is "test" ? 0 : components.Count;
        var unscoped = scoped.Value is { Length: > 0 } && !found.Scoped;

        if (declared + found.Total is 0)
            return await NoneAsync(workspace, query, kind, scope, maxResults, unscoped, cancellationToken).ConfigureAwait(false);

        var budget = ResultCap.Shown(declared + found.Total, maxResults);
        var shownComponents = Math.Min(declared, budget);
        var shownSymbols = Math.Min(found.Ranked.Count, budget - shownComponents);
        var response = new ResponseBuilder("search_symbols", query);

        response.Summary(shownComponents + shownSymbols, declared + found.Total, "symbols", "kind=, scope=, path= or maxResults=");

        if (unscoped)
            response.Note(FellBack);

        if (!found.TotalIsExact)
            response.Note("WARNING total counts duplicate declarations across projects; narrow query= for an exact count");

        if (found.Withheld > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{found.Withheld} more in test projects - scope=test"));

        foreach (var component in components.Take(shownComponents))
            response.Line(RazorUsageService.Describe(component));

        foreach (var symbol in found.Ranked.Take(shownSymbols))
            response.Line(Describe(workspace, symbol));

        return response.ToString();
    }

    private static DiagnosticSeverity Severity(string? minSeverity) => minSeverity?.ToLowerInvariant() switch
    {
        "error" => DiagnosticSeverity.Error,
        "info" or "suggestion" => DiagnosticSeverity.Info,
        "hidden" => DiagnosticSeverity.Hidden,
        _ => DiagnosticSeverity.Warning,
    };

    internal static int Cap(int requested, int fallback) => requested <= 0 ? fallback : Math.Min(requested, MaxCap);

    internal static string Unwrap(Result<string> result) => result.IsOk ? result.Value! : result.Error!.Render();

    private static string Describe(LoadedWorkspace workspace, ISymbol symbol)
    {
        var described = SymbolFormat.Describe(symbol);
        var detail = string.Equals(described, symbol.Name, StringComparison.Ordinal)
            ? SymbolFormat.Kind(symbol)
            : string.Create(CultureInfo.InvariantCulture, $"{SymbolFormat.Kind(symbol)} {described}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Location(workspace.Root, symbol)}  EXACT  {SymbolId.From(symbol)}  {detail}");
    }

    private Task<string> OutlinedAsync(
        string path,
        bool signatures,
        string? ids,
        bool usings,
        bool parameterNames,
        string? contains,
        bool all,
        string? workspace,
        CancellationToken cancellationToken) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            async loaded => Unwrap(await OutlineService.FileAsync(
                loaded, path, signatures, ids ?? "short", usings, cancellationToken, parameterNames, contains, all).ConfigureAwait(false)),
            cancellationToken: cancellationToken);

    private async Task<string> OutlinedManyAsync(
        ImmutableArray<string> paths,
        bool signatures,
        string? ids,
        bool usings,
        bool parameterNames,
        string? contains,
        bool all,
        string? workspace,
        CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("get_file_outline", string.Empty);

        response.Summary(paths.Length, paths.Length, "files");

        foreach (var path in paths)
        {
            var answer = await OutlinedAsync(path, signatures, ids, usings, parameterNames, contains, all, workspace, cancellationToken).ConfigureAwait(false);

            response.Line(Outlined(path, answer));
        }

        return response.ToString();
    }

    private static string Outlined(string path, string answer) => answer.StartsWith("ERROR", StringComparison.Ordinal)
        ? string.Create(CultureInfo.InvariantCulture, $"{(answer.Contains("DocumentNotFound", StringComparison.Ordinal) ? "NOT_FOUND" : "FAILED")} {path}\n{answer}")
        : string.Create(CultureInfo.InvariantCulture, $"{path}\n{answer}");

    private static async Task<string> ReferencedAsync(
        LoadedWorkspace workspace,
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var cap = Cap(maxResults, 100);
        var matches = await MetadataSearch.FindAsync(workspace, query, cap, cancellationToken, exhaustive: true).ConfigureAwait(false);
        var response = new ResponseBuilder("search_symbols", query);

        response.Summary(matches.Found.Count, matches.Total, "symbols", "a narrower query= or maxResults=");

        if (matches.Total > 0)
            response.Note("from referenced assemblies - no source declaration matched; the name must match exactly");

        foreach (var type in matches.Found)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{MetadataSearch.Origin(type)}  EXACT  {SymbolId.From(type).Value}  {SymbolFormat.Kind(type)} {SymbolFormat.Accessibility(type)} {SymbolFormat.Describe(type)}"));
        }

        return response.ToString();
    }

    private static string? Declined(string query, string? kind, string? scope)
    {
        if (Undeliverable(kind, scope) is not { } reason)
            return null;

        var response = new ResponseBuilder("search_symbols", query);

        response.Summary(0, 0, "symbols");
        response.Note(reason);

        return response.ToString();
    }

    private static string? Undeliverable(string? kind, string? scope) => (NamesAType(kind), scope) switch
    {
        (false, _) => string.Create(
            CultureInfo.InvariantCulture,
            $"no source declaration matched, and the referenced-assembly fallback holds types only, so it cannot answer kind={kind} - drop kind= to search them"),
        (_, { Length: > 0 }) => string.Create(
            CultureInfo.InvariantCulture,
            $"no source declaration matched, and a referenced assembly is neither src nor test, so scope={scope} excludes the fallback - drop scope= to search referenced assemblies"),
        _ => null,
    };

    private static bool NamesAType(string? kind) => kind switch
    {
        null or "" or "class" or "interface" or "enum" or "struct" or "record" or "delegate" => true,
        _ => false,
    };

    private static Result<string?> ScopedFile(LoadedWorkspace workspace, string? path) => path is { Length: > 0 }
            ? DocumentLookup.Find(workspace, path) is { } document
                ? Result.Ok<string?>(document.FilePath)
                : Result.Fail<string?>(Errors.DocumentNotFound(path))
            : Result.Ok<string?>(null);

    private const string FellBack = "NOTE path= declared no match - the whole solution was searched";

    private static async Task<string> NoneAsync(
            LoadedWorkspace workspace,
            string query,
            string? kind,
            string? scope,
            int maxResults,
            bool unscoped,
            CancellationToken cancellationToken)
    {
        if (Declined(query, kind, scope) is { } refusal)
            return refusal;

        var referenced = await ReferencedAsync(workspace, query, maxResults, cancellationToken).ConfigureAwait(false);

        return unscoped ? referenced + "\n" + FellBack : referenced;
    }

    internal const int MaxCap = 1000;

    private Task<string> Outlined(
            string[] requested,
            bool batched,
            string? workspace,
            OutlineService.TypeOutlineFormat format,
            string? path,
            CancellationToken cancellationToken) => requested switch
            {
                [] => Task.FromResult(Errors.Blank("symbolId", "symbol", "symbolIds").Render()),
                [var only] when !batched => context.WithSymbolAsync(workspace, only, async (loaded, resolved) =>
                Unwrap(await OutlineService.TypeAsync(loaded, resolved, format.Signatures, format.Ids, cancellationToken, format.ParameterNames, format.Contains, format.All).ConfigureAwait(false)),
                cancellationToken,
                path,
                referenced: true),
                _ => context.WithWorkspaceAsync(
                workspace,
                path,
                loaded => OutlineService.TypesAsync(loaded, requested[..Math.Min(requested.Length, MaxBatchedSymbols)], format, path, cancellationToken),
                cancellationToken: cancellationToken),
            };

    private Task<string> Described(string[] requested, bool batched, string? workspace, bool verbose, string? path, CancellationToken cancellationToken) => requested switch
    {
        [] => Task.FromResult(Errors.Blank("symbolId", "symbol", "symbolIds").Render()),
        [var only] when !batched => context.WithSymbolAsync(workspace, only, (loaded, resolved) =>
            Task.FromResult(SourceService.Describe(loaded.Root, resolved, verbose)), cancellationToken, path, referenced: true),
        _ => context.WithWorkspaceAsync(
            workspace,
            path,
            loaded => SourceService.DescribeManyAsync(loaded, requested[..Math.Min(requested.Length, MaxBatchedSymbols)], verbose, path, cancellationToken),
            cancellationToken: cancellationToken),
    };
}
