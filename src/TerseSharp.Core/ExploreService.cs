using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class ExploreService
{
    private const int ExploreFiles = 10;

    public static async Task<string> ExploreAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var reach = await ReachAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("explore_symbol", SymbolReference.Brief(symbol));

        response.Summary(
            Math.Min(ExploreFiles, reach.Files.Count),
            reach.Files.Count,
            "files using it",
            "impact_of for the full list");
        response.Note(Signature(workspace.Root, symbol));
        Documentation(response, symbol);
        response.Note(Counts(reach));

        foreach (var line in reach.Files.Take(ExploreFiles))
            response.Line(line);

        return response.ToString();
    }

    public static async Task<string> ImpactAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var reach = await ReachAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);
        var projects = Dependents(workspace, symbol);
        var records = reach.Files.Concat(reach.Xaml).ToArray();
        var response = new ResponseBuilder("impact_of", SymbolReference.Brief(symbol));

        response.Summary(Math.Min(maxResults, records.Length), records.Length, "affected files", "maxResults=");
        response.Note(Counts(reach));
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"projects that would recompile: {projects.Length} ({string.Join(", ", projects.Take(8))})"));

        foreach (var line in records.Take(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static async Task<Reach> ReachAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        var locations = references
            .SelectMany(reference => reference.Locations)
            .Where(location => !location.IsImplicit)
            .ToArray();

        var implementations = await SymbolFinder
            .FindImplementationsAsync(symbol, workspace.Solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new Reach(
            locations.Length,
            locations.Count(location => TestScope.Of(location.Document.Project) is "test"),
            implementations.Count(),
            [.. Grouped(workspace.Root, locations)],
            [.. XamlUsageService.Find(workspace.Root, symbol, symbol.Name).Select(Describe)]);
    }

    private static IEnumerable<string> Grouped(string root, IReadOnlyList<ReferenceLocation> locations) => locations
        .GroupBy(location => PositionFormat.Relative(root, location.Location.GetLineSpan().Path), StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .Select(group => string.Create(
            CultureInfo.InvariantCulture,
            $"{group.Key}  EXACT  {TestScope.Of(group.First().Document.Project)}  {group.Count()} usage(s)"));

    private static string Describe(XamlUsage usage) => string.Create(
        CultureInfo.InvariantCulture,
        $"{usage.File}:{usage.Line}  {usage.Confidence}  xaml {usage.Kind}  {usage.Text}");

    private static string Counts(Reach reach) => string.Create(
        CultureInfo.InvariantCulture,
        $"usages={reach.Usages} (test={reach.TestUsages}) files={reach.Files.Count} implementations={reach.Implementations} xaml={reach.Xaml.Count}");

    private static string Signature(string root, ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"{SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)} at {SymbolFormat.Location(root, symbol)}");

    private static void Documentation(ResponseBuilder response, ISymbol symbol)
    {
        var documentation = symbol.GetDocumentationCommentXml(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(documentation))
            response.Note(documentation.Trim());
    }

    private static string[] Dependents(LoadedWorkspace workspace, ISymbol symbol)
    {
        var owner = workspace.Solution.GetProject(symbol.ContainingAssembly);

        if (owner is null)
            return [];

        var graph = workspace.Solution.GetProjectDependencyGraph();

        return [.. graph
            .GetProjectsThatTransitivelyDependOnThisProject(owner.Id)
            .Append(owner.Id)
            .Select(id => workspace.Solution.GetProject(id)?.Name)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];
    }

    private readonly record struct Reach(
        int Usages,
        int TestUsages,
        int Implementations,
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Xaml);
}
