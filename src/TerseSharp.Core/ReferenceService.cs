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
            .ToArray();

        return Render(symbol, locations, maxResults);
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
            response.Line(Describe(implementation));

        return response.ToString();
    }

    private static string Describe(ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"{SymbolFormat.Location(symbol)}  EXACT  {SymbolId.From(symbol)}  {SymbolFormat.Describe(symbol)}");

    private static string Render(ISymbol symbol, ReferenceLocation[] locations, int maxResults)
    {
        var shown = Math.Min(maxResults, locations.Length);
        var files = locations.Select(location => location.Document.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var response = new ResponseBuilder("find_usages", SymbolId.From(symbol).Value);

        response.Summary(shown, locations.Length, string.Create(CultureInfo.InvariantCulture, $"usages in {files} files"));

        foreach (var location in locations.Take(shown))
            response.Line(DescribeLocation(location));

        return response.ToString();
    }

    private static string DescribeLocation(ReferenceLocation location) => string.Create(
        CultureInfo.InvariantCulture,
        $"{PositionFormat.Describe(location.Location)}  {ConfidenceTag.Of(ConfidenceOf(location))}  {ClassifyKind(location)}");

    private static string ClassifyKind(ReferenceLocation location) =>
        location.CandidateReason is CandidateReason.None
            ? "ref"
            : location.CandidateReason.ToString().ToLowerInvariant();

    private static Confidence ConfidenceOf(ReferenceLocation location) =>
        location.CandidateReason is CandidateReason.None ? Confidence.Exact : Confidence.Heuristic;
}
