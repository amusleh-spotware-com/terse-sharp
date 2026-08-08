using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class NavigationTools(ToolContext context)
{
    [McpServerTool(Name = "search_symbols")]
    [Description("Find declarations by name across the solution. Supports substring and CamelHump ('OSvc' finds OrderService). Use instead of Grep for anything that is a type or member.")]
    public Task<string> SearchSymbols(
        [Description("Name or CamelHump pattern.")] string query,
        [Description("Optional kind filter: class, interface, method, property, field, enum.")] string? kind = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (50).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => SearchAsync(loaded, query, kind, Cap(maxResults, 50), cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_file_outline")]
    [Description("List every type and member of a .cs file with signatures and line ranges, without the bodies. Use instead of Read on a .cs file. parameterNames=false prints parameter types without their names, which is about an eighth of the response and still tells every overload apart.")]
    public Task<string> GetFileOutline(
    [Description("Path to the .cs file.")] string path,
    [Description("Include member signatures. false gives ids and line ranges only, ~40% cheaper.")] bool signatures = true,
    [Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Also list the file's own using directives, so a new member's header can be written without reading source.")] bool usings = false,
    [Description("Print parameter names alongside their types. Default true; false keeps the types and drops the names.")] bool parameterNames = true,
    CancellationToken cancellationToken = default) =>
    context.WithWorkspaceAsync(workspace, path, async loaded =>
        Unwrap(await OutlineService.FileAsync(loaded, path, signatures, ids ?? "short", usings, cancellationToken, parameterNames).ConfigureAwait(false)),
        cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_type_outline")]
    [Description("Every member of one type with signatures and line ranges, without the bodies. parameterNames=false prints parameter types without their names, which is about an eighth of the response. path= resolves a NAME inside that file first, so a name an outline just printed round-trips even when the solution holds others like it; a full documentation id already addresses one symbol, so path= does not apply to it.")]
    public Task<string> GetTypeOutline(
[Description("Symbol id of the type.")] string? symbolId = null,
[Description("Include member signatures. false gives ids and line ranges only.")] bool signatures = true,
[Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
[Description("Workspace or worktree name.")] string? workspace = null,
[Description("Alias for symbolId.")] string? symbol = null,
[Description("Print parameter names alongside their types. Default true; false keeps the types and drops the names.")] bool parameterNames = true,
[Description("File the name lives in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
CancellationToken cancellationToken = default) =>
context.WithSymbolAsync(workspace, symbolId ?? symbol, async (loaded, resolved) =>
    Unwrap(await OutlineService.TypeAsync(loaded, resolved, signatures, ids ?? "short", cancellationToken, parameterNames).ConfigureAwait(false)),
    cancellationToken,
    path);

    [McpServerTool(Name = "get_symbol")]
    [Description("Signature, kind, accessibility, location and XML doc of one symbol. path= resolves a NAME inside that file first, so a name an outline just printed round-trips even when the solution holds others like it; a full documentation id already addresses one symbol, so path= does not apply to it.")]
    public Task<string> GetSymbol(
    [Description("Symbol id, e.g. M:Trading.OrderService.Submit(Trading.Order).")] string? symbolId = null,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Return the XML documentation verbatim and echo the request. Default false.")] bool verbose = false,
    [Description("Alias for symbolId.")] string? symbol = null,
    [Description("File the name lives in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
    CancellationToken cancellationToken = default) =>
    context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
        Task.FromResult(SourceService.Describe(loaded.Root, resolved, verbose)), cancellationToken, path);

    [McpServerTool(Name = "get_symbol_source")]
    [Description("Return only that member's source text and line range. Use instead of reading the whole file to see one method. Pass symbolIds to get several members in one response, each id that does not resolve reported inline as NOT_RESOLVED rather than failing the call. path= resolves each name inside that file first, so a name an outline just printed round-trips even when the solution holds others like it. The source is dedented; pass verbose=true for it verbatim, and comments=false to drop the doc comments and inline comments when you are orienting rather than editing - worth about a tenth of the tokens on a documented codebase and nothing on one that carries no comments.")]
    public Task<string> GetSymbolSource(
[Description("Symbol id of the member.")] string? symbolId = null,
[Description("Several symbol ids returned in one response. Replaces one call per member.")] string[]? symbolIds = null,
[Description("Workspace or worktree name.")] string? workspace = null,
[Description("Return the source verbatim, with its original indentation and blank lines. Default false.")] bool verbose = false,
[Description("Alias for symbolId.")] string? symbol = null,
[Description("Include doc comments and inline comments. Default true; false drops them, which is the cheap read when you only need the shape.")] bool comments = true,
[Description("File the names live in. A name is resolved inside it first and only falls back to the solution when the file has no match, and a path naming no document answers DocumentNotFound; a full documentation id ignores it, because it already addresses one symbol.")] string? path = null,
CancellationToken cancellationToken = default) =>
SourceOf(Requested(symbolId ?? symbol, symbolIds), workspace, new SourceFormat(verbose, comments), path, cancellationToken);

    private Task<string> SourceOf(string[] requested, string? workspace, SourceFormat format, string? path, CancellationToken cancellationToken) => requested switch
    {
        [] => Task.FromResult(Errors.Blank("symbolId", "symbol", "symbolIds").Render()),
        [var only] => context.WithSymbolAsync(workspace, only, async (loaded, resolved) =>
            Unwrap(await SourceService.OfSymbolAsync(loaded.Root, resolved, format, cancellationToken).ConfigureAwait(false)), cancellationToken, path),
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

    [McpServerTool(Name = "find_usages")]
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

    [McpServerTool(Name = "find_registrations")]
    [Description("Where a type is registered in a dependency-injection container - AddSingleton, AddScoped, AddTransient, keyed and TryAdd variants - with the member each call sits in. Grep cannot answer this when the registration uses an open generic, a factory delegate or an Add* extension method. Says so explicitly when nothing matches, rather than implying the type is unregistered.")]
    public Task<string> FindRegistrations(
        [Description("Type name to look for, e.g. IOrderRepository.")] string query,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            RegistrationService.RegistrationsAsync(loaded, query, Cap(maxResults, 100), cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "list_endpoints")]
    [Description("Every ASP.NET Core endpoint registration in the solution - MapGet, MapPost, MapControllers, MapHub and friends - with the member each sits in. Use instead of grepping Program.cs and every extension method it calls.")]
    public Task<string> ListEndpoints(
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            RegistrationService.EndpointsAsync(loaded, Cap(maxResults, 200), cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "explore_symbol")]
    [Description("One call to orient on a symbol: signature, XML doc, location, how many usages it has in src and in tests, how many implementations, how many XAML sites, and the files it is used in. Replaces get_symbol + find_usages + find_implementations when you are learning what something is.")]
    public Task<string> ExploreSymbol(
        [Description("Symbol id or name.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ExploreService.ExploreAsync(loaded, resolved, cancellationToken), cancellationToken);

    [McpServerTool(Name = "impact_of")]
    [Description("The blast radius of changing a symbol: every file that references it with a src/test marker, every XAML site, and every project that would recompile. Use before a rename or a signature change.")]
    public Task<string> ImpactOf(
        [Description("Symbol id or name.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max records (200).")] int maxResults = 0,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ExploreService.ImpactAsync(loaded, resolved, Cap(maxResults, 200), cancellationToken), cancellationToken);

    [McpServerTool(Name = "find_implementations")]
    [Description("Implementations of an interface or abstract member, and derived types of a base type.")]
    public Task<string> FindImplementations(
        [Description("Symbol id of the interface, abstract member or base type.")] string? symbolId = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId ?? symbol, (loaded, resolved) =>
            ReferenceService.FindImplementationsAsync(loaded, resolved, Cap(maxResults, 100), cancellationToken), cancellationToken);

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Compiler diagnostics from the Roslyn compilation, deduplicated. Use instead of parsing dotnet build output. Does not yet run the project's analyzers - use build for those.")]
    public Task<string> GetDiagnostics(
        [Description("File to scope to.")] string? path = null,
        [Description("Minimum severity: error, warning, info. Default warning.")] string? minSeverity = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded =>
            DiagnosticsService.CollectAsync(loaded, path, Severity(minSeverity), Cap(maxResults, 100), cancellationToken),
            cancellationToken: cancellationToken);

    private static async Task<string> SearchAsync(
        LoadedWorkspace workspace,
        string query,
        string? kind,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var found = await SymbolSearch.FindAsync(workspace, query, kind, maxResults, cancellationToken).ConfigureAwait(false);
        var components = await RazorUsageService.DeclarationsAsync(workspace, query, cancellationToken).ConfigureAwait(false);
        var budget = ResultCap.Shown(components.Count + found.Total, maxResults);
        var shownComponents = Math.Min(components.Count, budget);
        var shownSymbols = Math.Min(found.Ranked.Count, budget - shownComponents);
        var response = new ResponseBuilder("search_symbols", query);

        response.Summary(shownComponents + shownSymbols, components.Count + found.Total, "symbols", "kind= or maxResults=");

        if (!found.TotalIsExact)
            response.Note("WARNING total counts duplicate declarations across projects; narrow query= for an exact count");

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

    internal static int Cap(int requested, int fallback) => requested <= 0 ? fallback : Math.Min(requested, 1000);

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
}
