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
        context.WithWorkspaceAsync(workspace, null, loaded => SearchAsync(loaded, query, kind, Cap(maxResults, 50), cancellationToken));

    [McpServerTool(Name = "get_file_outline")]
    [Description("List every type and member of a .cs file with signatures and line ranges, without the bodies. Use instead of Read on a .cs file.")]
    public Task<string> GetFileOutline(
        [Description("Path to the .cs file.")] string path,
        [Description("Include member signatures. false gives ids and line ranges only, ~40% cheaper.")] bool signatures = true,
        [Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, async loaded =>
            Unwrap(await OutlineService.FileAsync(loaded, path, signatures, ids ?? "short", cancellationToken).ConfigureAwait(false)));

    [McpServerTool(Name = "get_type_outline")]
    [Description("List a type's members with signatures and line ranges, without the bodies. The cheapest way to learn what a class offers.")]
    public Task<string> GetTypeOutline(
        [Description("Type id, e.g. T:Trading.OrderService.")] string symbolId,
        [Description("Include member signatures. false gives ids and line ranges only, ~40% cheaper.")] bool signatures = true,
        [Description("short (default) names members as Type.Member(Arg), which every tool accepts; full emits documentation ids.")] string? ids = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, async (loaded, symbol) =>
            Unwrap(await OutlineService.TypeAsync(loaded, symbol, signatures, ids ?? "short", cancellationToken).ConfigureAwait(false)), cancellationToken);

    [McpServerTool(Name = "get_symbol")]
    [Description("Signature, kind, accessibility, location and XML doc of one symbol.")]
    public Task<string> GetSymbol(
        [Description("Symbol id, e.g. M:Trading.OrderService.Submit(Trading.Order).")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, (loaded, symbol) =>
            Task.FromResult(SourceService.Describe(loaded.Root, symbol)), cancellationToken);

    [McpServerTool(Name = "get_symbol_source")]
    [Description("Return only that member's source text and line range. Use instead of reading the whole file to see one method.")]
    public Task<string> GetSymbolSource(
        [Description("Symbol id of the member.")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, async (loaded, symbol) =>
            Unwrap(await SourceService.OfSymbolAsync(loaded.Root, symbol, cancellationToken).ConfigureAwait(false)), cancellationToken);

    [McpServerTool(Name = "find_usages")]
    [Description("Every real reference to a symbol, resolved semantically, one line per file with a src/test marker. Use instead of Grep for a type or member name; comments and unrelated matches are excluded.")]
    public Task<string> FindUsages(
        [Description("Symbol id to find references for.")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        [Description("Also name the member each usage sits in, one line per member instead of per file (default false).")] bool containers = false,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, (loaded, symbol) =>
            ReferenceService.FindUsagesAsync(loaded, symbol, Cap(maxResults, 100), containers, cancellationToken), cancellationToken);

    [McpServerTool(Name = "find_registrations")]
    [Description("Where a type is registered in a dependency-injection container - AddSingleton, AddScoped, AddTransient, keyed and TryAdd variants - with the member each call sits in. Grep cannot answer this when the registration uses an open generic, a factory delegate or an Add* extension method. Says so explicitly when nothing matches, rather than implying the type is unregistered.")]
    public Task<string> FindRegistrations(
        [Description("Type name to look for, e.g. IOrderRepository.")] string query,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            RegistrationService.RegistrationsAsync(loaded, query, Cap(maxResults, 100), cancellationToken));

    [McpServerTool(Name = "list_endpoints")]
    [Description("Every ASP.NET Core endpoint registration in the solution - MapGet, MapPost, MapControllers, MapHub and friends - with the member each sits in. Use instead of grepping Program.cs and every extension method it calls.")]
    public Task<string> ListEndpoints(
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (200).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, null, loaded =>
            RegistrationService.EndpointsAsync(loaded, Cap(maxResults, 200), cancellationToken));

    [McpServerTool(Name = "explore_symbol")]
    [Description("One call to orient on a symbol: signature, XML doc, location, how many usages it has in src and in tests, how many implementations, how many XAML sites, and the files it is used in. Replaces get_symbol + find_usages + find_implementations when you are learning what something is.")]
    public Task<string> ExploreSymbol(
        [Description("Symbol id or name.")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, (loaded, symbol) =>
            ExploreService.ExploreAsync(loaded, symbol, cancellationToken), cancellationToken);

    [McpServerTool(Name = "impact_of")]
    [Description("The blast radius of changing a symbol: every file that references it with a src/test marker, every XAML site, and every project that would recompile. Use before a rename or a signature change.")]
    public Task<string> ImpactOf(
        [Description("Symbol id or name.")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max records (200).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, (loaded, symbol) =>
            ExploreService.ImpactAsync(loaded, symbol, Cap(maxResults, 200), cancellationToken), cancellationToken);

    [McpServerTool(Name = "find_implementations")]
    [Description("Implementations of an interface or abstract member, and derived types of a base type.")]
    public Task<string> FindImplementations(
        [Description("Symbol id of the interface, abstract member or base type.")] string symbolId,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithSymbolAsync(workspace, symbolId, (loaded, symbol) =>
            ReferenceService.FindImplementationsAsync(loaded, symbol, Cap(maxResults, 100), cancellationToken), cancellationToken);

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Compiler diagnostics from the Roslyn compilation, deduplicated. Use instead of parsing dotnet build output. Does not yet run the project's analyzers - use build for those.")]
    public Task<string> GetDiagnostics(
        [Description("File to scope to.")] string? path = null,
        [Description("Minimum severity: error, warning, info. Default warning.")] string? minSeverity = null,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Max results (100).")] int maxResults = 0,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(workspace, path, loaded =>
            DiagnosticsService.CollectAsync(loaded, path, Severity(minSeverity), Cap(maxResults, 100), cancellationToken));

    private static async Task<string> SearchAsync(
        LoadedWorkspace workspace,
        string query,
        string? kind,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var found = await SymbolSearch.FindAsync(workspace, query, kind, maxResults, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("search_symbols", query);

        var components = await RazorUsageService.DeclarationsAsync(workspace, query, cancellationToken).ConfigureAwait(false);

        response.Summary(found.Count + components.Count, found.Count + components.Count, "symbols");

        foreach (var component in components)
            response.Line(RazorUsageService.Describe(component));

        foreach (var symbol in found)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{SymbolFormat.Location(workspace.Root, symbol)}  EXACT  {SymbolId.From(symbol)}  {SymbolFormat.Kind(symbol)} {SymbolFormat.Describe(symbol)}"));
        }

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
}
