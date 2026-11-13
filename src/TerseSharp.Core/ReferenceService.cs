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
        bool chosen,
        CancellationToken cancellationToken,
        string? scope = null)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        var all = references
            .SelectMany(reference => reference.Locations)
            .Where(location => !location.IsImplicit && !HiddenInGenerated(location))
            .OrderBy(location => location.Document.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Location.SourceSpan.Start)
            .ThenBy(location => location.IsCandidateLocation ? 1 : 0)
            .Distinct(SourcePosition.Instance)
            .ToArray();
        var locations = scope is { Length: > 0 }
            ? Array.FindAll(all, location => string.Equals(TestScope.Of(location.Document.Project), scope, StringComparison.Ordinal))
            : all;
        var markup = await RazorUsageService.MarkupAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RazorUsage> razor = scope is { Length: > 0 } ? [.. markup.Where(usage => InHalf(workspace, usage.Path, scope))] : markup;
        var half = new Half(scope, all.Length - locations.Length + markup.Count - razor.Count);

        return await RenderAsync(workspace, symbol, locations, razor, maxResults, containers, chosen, half, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> FindImplementationsAsync(
            LoadedWorkspace workspace,
            ISymbol symbol,
            int maxResults,
            CancellationToken cancellationToken)
    {
        var implementations = await SymbolDerivation.FindAsync(workspace.Solution, symbol, cancellationToken).ConfigureAwait(false);
        var found = implementations
            .Select(implementation => (Symbol: implementation, Order: Ordinal(workspace.Root, implementation)))
            .OrderBy(entry => entry.Order.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Order.Line)
            .Select(entry => entry.Symbol)
            .ToArray();

        var shown = ResultCap.Shown(found.Length, maxResults);
        var response = new ResponseBuilder("find_implementations", SymbolId.From(symbol).Value);

        response.Summary(shown, found.Length, "implementations", "a more specific symbol, or raise maxResults=");

        foreach (var implementation in found.Take(shown))
            response.Line(Describe(workspace.Root, implementation));

        if (found.Length is 0)
            response.Note(SymbolDerivation.WhyEmpty(symbol));

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
            bool chosen,
            Half half,
            CancellationToken cancellationToken)
    {
        var shown = chosen ? ResultCap.Shown(locations.Length, maxResults) : ResultCap.Whole(locations.Length, maxResults);
        var files = locations
            .Select(location => PositionFormat.Source(location.Location).Path)
            .Concat(razor.Select(usage => usage.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var sites = XamlUsageService.Find(workspace, symbol, symbol.Name).ToArray();
        var xaml = Array.FindAll(sites, usage => InHalf(workspace, usage.File, half.Scope));
        var response = new ResponseBuilder("find_usages", SymbolId.From(symbol).Value).Chosen(chosen);
        var records = new List<string>();

        response.Summary(
            shown,
            locations.Length + razor.Count,
            string.Create(CultureInfo.InvariantCulture, $"usages in {files} files"),
            "a more specific symbol, or raise maxResults=");

        if (ProjectLoadScope(workspace) is { } scope)
            response.Note(scope);

        HiddenNote(response, half, sites.Length - xaml.Length);

        var grouped = await GroupAsync(workspace.Root, locations.Take(shown), containers, cancellationToken).ConfigureAwait(false);

        foreach (var group in grouped.GroupBy(entry => entry.Group))
            records.Add(Describe(group.Key, group.Select(entry => entry.Location)));

        foreach (var usage in razor)
            records.Add(RazorUsageService.Describe(usage));

        foreach (var usage in xaml)
            records.Add(DescribeXaml(usage));

        foreach (var record in records)
            response.Line(record);

        if (ArgumentLine.Paths(records) is { } batch)
            response.Note(batch);

        return response.ToString();
    }

    internal static string? ProjectLoadScope(LoadedWorkspace workspace) =>
            Path.GetExtension(workspace.SolutionPath.AsSpan()) is var extension
            && (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"scope=project load - only the {workspace.Solution.ProjectIds.Count} project(s) {Path.GetFileName(workspace.SolutionPath)} pulls in were searched; a caller in any other project, a test project included, is not counted - load the .sln/.slnx for the whole answer")
                : null;

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

    private sealed class SourcePosition : IEqualityComparer<ReferenceLocation>
    {
        public static readonly SourcePosition Instance = new();

        public bool Equals(ReferenceLocation left, ReferenceLocation right) =>
            left.Location.SourceSpan == right.Location.SourceSpan
            && string.Equals(PathOf(left), PathOf(right), StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ReferenceLocation location) =>
            HashCode.Combine(location.Location.SourceSpan, StringComparer.OrdinalIgnoreCase.GetHashCode(PathOf(location)));

        private static string PathOf(ReferenceLocation location) => location.Document.FilePath ?? location.Document.Name;
    }

    private static (string Path, int Line) Ordinal(string root, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);

        return location is null
            ? (string.Empty, 0)
            : (PositionFormat.Relative(root, PositionFormat.Source(location).Path), location.GetLineSpan().StartLinePosition.Line);
    }

    private readonly record struct Half(string? Scope, int Hidden);

    private static Project? OwnerOf(LoadedWorkspace workspace, string full) => workspace.Solution.Projects
        .Where(project => Path.GetDirectoryName(project.FilePath) is { Length: > 0 } directory && PathBoundary.Contains(directory, full))
        .MaxBy(project => project.FilePath!.Length);

    private static bool InHalf(LoadedWorkspace workspace, string path, string? scope) => scope is not { Length: > 0 }
        || OwnerOf(workspace, Path.GetFullPath(Path.Combine(workspace.Root, path))) is { } owner && string.Equals(TestScope.Of(owner), scope, StringComparison.Ordinal);

    private static void HiddenNote(ResponseBuilder response, Half half, int hiddenXaml)
    {
        if (half is { Scope: { } kept } && half.Hidden + hiddenXaml is > 0 and var hidden)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"scope={kept} hid {hidden} usages in the other half - drop scope= to see them"));
    }
}
