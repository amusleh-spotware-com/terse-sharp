using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class ReferenceService
{
    public static async Task<string> FindUsagesAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        int maxResults,
        bool containers,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        var locations = references
            .SelectMany(reference => reference.Locations)
            .Where(location => !location.IsImplicit && !HiddenInGenerated(location))
            .OrderBy(location => location.Document.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Location.SourceSpan.Start)
            .ToArray();

        var razor = await RazorUsageService.MarkupAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        return await RenderAsync(workspace, symbol, locations, razor, maxResults, containers, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> FindImplementationsAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder
            .FindImplementationsAsync(symbol, workspace.Solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var found = implementations.ToArray();
        var shown = ResultCap.Shown(found.Length, maxResults);
        var response = new ResponseBuilder("find_implementations", SymbolId.From(symbol).Value);

        response.Summary(shown, found.Length, "implementations", "a more specific symbol, or raise maxResults=");

        foreach (var implementation in found.Take(shown))
            response.Line(Describe(workspace.Root, implementation));

        return response.ToString();
    }

    private static bool HiddenInGenerated(ReferenceLocation location) =>
        RazorFiles.IsGenerated(location.Document.FilePath ?? location.Document.Name)
        && !location.Location.GetMappedLineSpan().HasMappedPath;

    private static string Describe(string root, ISymbol symbol)
    {
        var described = SymbolFormat.Describe(symbol);
        var detail = string.Equals(described, symbol.Name, StringComparison.Ordinal) ? SymbolFormat.Kind(symbol) : described;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolFormat.Location(root, symbol)}  EXACT  {SymbolId.From(symbol)}  {detail}");
    }

    private static async Task<string> RenderAsync(
            LoadedWorkspace workspace,
            ISymbol symbol,
            ReferenceLocation[] locations,
            IReadOnlyList<RazorUsage> razor,
            int maxResults,
            bool containers,
            CancellationToken cancellationToken)
    {
        var shown = ResultCap.Shown(locations.Length, maxResults);
        var files = locations
            .Select(location => PositionFormat.Source(location.Location).Path)
            .Concat(razor.Select(usage => usage.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var response = new ResponseBuilder("find_usages", SymbolId.From(symbol).Value);
        var records = new List<string>();

        response.Summary(
            shown,
            locations.Length + razor.Count,
            string.Create(CultureInfo.InvariantCulture, $"usages in {files} files"),
            "a more specific symbol, or raise maxResults=");

        var grouped = await GroupAsync(workspace.Root, locations.Take(shown), containers, cancellationToken).ConfigureAwait(false);

        foreach (var group in grouped.GroupBy(entry => entry.Group))
            records.Add(Describe(group.Key, group.Select(entry => entry.Location)));

        foreach (var usage in razor)
            records.Add(RazorUsageService.Describe(usage));

        foreach (var usage in XamlUsageService.Find(workspace, symbol, symbol.Name))
            records.Add(DescribeXaml(usage));

        foreach (var record in records)
            response.Line(record);

        if (ArgumentLine.Paths(records) is { } batch)
            response.Note(batch);

        return response.ToString();
    }

    private static async Task<List<UsageEntry>> GroupAsync(
        string root,
        IEnumerable<ReferenceLocation> locations,
        bool containers,
        CancellationToken cancellationToken)
    {
        var entries = new List<UsageEntry>();
        var roots = new Dictionary<DocumentId, SyntaxNode?>();

        foreach (var location in locations)
        {
            var syntax = containers
                ? await RootOfAsync(roots, location.Document, cancellationToken).ConfigureAwait(false)
                : null;

            entries.Add(new UsageEntry(UsageGroup.Of(root, location, syntax), location));
        }

        return entries;
    }

    private static async Task<SyntaxNode?> RootOfAsync(
        Dictionary<DocumentId, SyntaxNode?> roots,
        Document document,
        CancellationToken cancellationToken)
    {
        if (roots.TryGetValue(document.Id, out var cached))
            return cached;

        return roots[document.Id] = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Describe(UsageGroup group, IEnumerable<ReferenceLocation> locations)
    {
        var container = group.Container is null ? string.Empty : "in " + group.Container + "  ";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{group.Path}  {group.Confidence}  {group.Kind}  {group.Scope}  {container}{Positions(locations)}");
    }

    private static string DescribeXaml(XamlUsage usage) => string.Create(
        CultureInfo.InvariantCulture,
        $"{usage.File}:{usage.Line}  {usage.Confidence}  xaml {usage.Kind}  {usage.Text}");

    private static string Positions(IEnumerable<ReferenceLocation> locations) =>
        string.Join(", ", locations.Select(Position));

    private static string Position(ReferenceLocation location)
    {
        var start = location.Location.GetLineSpan().StartLinePosition;

        return string.Create(CultureInfo.InvariantCulture, $"{start.Line + 1}:{start.Character + 1}");
    }

    private static string ClassifyKind(ReferenceLocation location) =>
        location.CandidateReason is CandidateReason.None
            ? "ref"
            : location.CandidateReason.ToString().ToLowerInvariant();

    private static Confidence ConfidenceOf(ReferenceLocation location) =>
        location.CandidateReason is CandidateReason.None ? Confidence.Exact : Confidence.Heuristic;

    private readonly record struct UsageEntry(UsageGroup Group, ReferenceLocation Location);

    private readonly record struct UsageGroup(string Path, string Confidence, string Kind, string Scope, string? Container)
    {
        public static UsageGroup Of(string root, ReferenceLocation location, SyntaxNode? syntax) => new(
            PositionFormat.Relative(root, PositionFormat.Source(location.Location).Path),
            ConfidenceTag.Of(ConfidenceOf(location)),
            ClassifyKind(location),
            TestScope.Of(root, location.Document),
            UsageContainer.Of(syntax, location.Location.SourceSpan));
    }
}
