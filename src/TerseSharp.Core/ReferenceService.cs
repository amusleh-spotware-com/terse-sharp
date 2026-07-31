using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class ReferenceService
{
    public static async Task<string> FindUsagesAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        var locations = references
            .SelectMany(reference => reference.Locations)
            .Where(location => !location.IsImplicit)
            .OrderBy(location => location.Document.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Location.SourceSpan.Start)
            .ToArray();

        return Render(workspace.Root, symbol, locations, maxResults);
    }

    public static async Task<string> FindImplementationsAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var implementations = await SymbolFinder
            .FindImplementationsAsync(symbol, workspace.Solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var found = implementations.ToArray();
        var response = new ResponseBuilder("find_implementations", SymbolId.From(symbol).Value);

        response.Summary(found.Length, found.Length, "implementations");

        foreach (var implementation in found)
            response.Line(Describe(workspace.Root, implementation));

        return response.ToString();
    }

    private static string Describe(string root, ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"{SymbolFormat.Location(root, symbol)}  EXACT  {SymbolId.From(symbol)}  {SymbolFormat.Describe(symbol)}");

    private static string Render(string root, ISymbol symbol, ReferenceLocation[] locations, int maxResults)
    {
        var shown = Math.Min(maxResults, locations.Length);
        var files = locations.Select(location => location.Document.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var response = new ResponseBuilder("find_usages", SymbolId.From(symbol).Value);

        response.Summary(shown, locations.Length, string.Create(CultureInfo.InvariantCulture, $"usages in {files} files"));

        foreach (var group in locations.Take(shown).GroupBy(location => UsageGroup.Of(root, location)))
            response.Line(Describe(group.Key, group));

        return response.ToString();
    }

    private static string Describe(UsageGroup group, IEnumerable<ReferenceLocation> locations) => string.Create(
        CultureInfo.InvariantCulture,
        $"{group.Path}  {group.Confidence}  {group.Kind}  {Positions(locations)}");

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

    private readonly record struct UsageGroup(string Path, string Confidence, string Kind)
    {
        public static UsageGroup Of(string root, ReferenceLocation location) => new(
            PositionFormat.Relative(root, location.Location.GetLineSpan().Path),
            ConfidenceTag.Of(ConfidenceOf(location)),
            ClassifyKind(location));
    }
}
