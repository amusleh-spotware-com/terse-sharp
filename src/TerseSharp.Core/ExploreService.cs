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
        bool tests,
        CancellationToken cancellationToken)
    {
        var reach = await ReachAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);
        var projects = Dependents(workspace, symbol);
        var records = reach.Files.Concat(reach.Xaml).ToArray();
        var response = new ResponseBuilder("impact_of", SymbolReference.Brief(symbol));
        var named = string.Join(", ", projects.Take(8));

        response.Summary(ResultCap.Shown(records.Length, maxResults), records.Length, "affected files", "maxResults=");
        response.Note(Counts(reach));
        response.Note(string.Create(CultureInfo.InvariantCulture, $"projects that would recompile: {projects.Length} ({named})"));

        if (tests)
            Reaching(response, await TestClassesAsync(workspace, reach.Locations, cancellationToken).ConfigureAwait(false));

        foreach (var line in records.Capped(maxResults))
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
            locations.Count(location => TestScope.Of(workspace.Root, location.Document) is "test"),
            implementations.Count(),
            [.. Grouped(workspace.Root, locations)],
            [.. XamlUsageService.Find(workspace, symbol, symbol.Name).Select(Describe)],
            locations);
    }

    private static IEnumerable<string> Grouped(string root, IReadOnlyList<ReferenceLocation> locations) => locations
        .GroupBy(location => PositionFormat.Relative(root, location.Location.GetLineSpan().Path), StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .Select(group => string.Create(
            CultureInfo.InvariantCulture,
            $"{group.Key}  EXACT  {TestScope.Of(root, group.First().Document)}  {group.Count()} usage(s)"));

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
            IReadOnlyList<string> Xaml,
            IReadOnlyList<ReferenceLocation> Locations);

    private const int MaxTestClasses = 10;

    private static void Reaching(ResponseBuilder response, IReadOnlyList<string> classes)
    {
        if (classes.Count is 0)
        {
            response.Note("no test declaration references this symbol directly - run the whole suite, because a test can break without naming it");

            return;
        }

        foreach (var name in classes.Take(MaxTestClasses))
            response.Note("tests: run_tests test=" + name);

        if (classes.Count > MaxTestClasses)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"tests: {MaxTestClasses} of {classes.Count} test classes shown - run the whole suite, because the rest are not listed"));

        response.Note("HEURISTIC these are the test classes referencing the symbol DIRECTLY; a test reaching it through a helper is not listed, so this narrows a run, it does not replace one");
    }

    private static async Task<IReadOnlyList<string>> TestClassesAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<ReferenceLocation> locations,
        CancellationToken cancellationToken)
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var location in locations)
        {
            if (TestScope.Of(workspace.Root, location.Document) is not "test")
                continue;

            var root = await location.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (DeclaringTestType(root, location.Location.SourceSpan) is { Length: > 0 } type)
                classes.Add(type);
        }

        return [.. classes];
    }

    private static string? DeclaringTestType(SyntaxNode? root, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var declaration = root?.FindNode(span, getInnermostNodeForTie: true)
            ?.AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .LastOrDefault();

        return declaration is not null && DeclaresATest(declaration) ? declaration.Identifier.ValueText : null;
    }

    private static readonly string[] TestAttributes = ["Fact", "Theory", "Test", "TestMethod", "TestCase"];

    private static bool DeclaresATest(Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration) =>
        declaration.Members
            .SelectMany(member => member.AttributeLists)
            .SelectMany(list => list.Attributes)
            .Any(attribute => TestAttributes.Contains(Simple(attribute), StringComparer.Ordinal));

    private static string Simple(Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        var dot = name.LastIndexOf('.');
        var trimmed = dot < 0 ? name : name[(dot + 1)..];

        return trimmed.EndsWith("Attribute", StringComparison.Ordinal) ? trimmed[..^9] : trimmed;
    }
}
