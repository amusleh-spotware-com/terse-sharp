using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class DiffSymbolService
{
    public static async Task<string> MapAsync(
    LoadedWorkspace workspace,
    string unifiedDiff,
    int maxResults,
    CancellationToken cancellationToken)
    {
        var hunks = DiffParser.Hunks(unifiedDiff);
        var records = new List<string>(Math.Min(Math.Max(hunks.Count, 1), 512));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in hunks.GroupBy(hunk => hunk.Path, StringComparer.Ordinal))
            await AppendFileAsync(workspace, group.Key, group, records, seen, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("diff_symbols", string.Empty);
        response.Summary(ResultCap.Shown(records.Count, maxResults), records.Count, "declarations", "path= or maxResults=");
        if (hunks.Count is 0)
            response.Note("the diff carried no hunks; nothing changed in the compared range");
        Steer(response, records, maxResults);
        foreach (var record in records.Capped(maxResults))
            response.Line(record);
        return response.ToString();
    }

    private static async Task AppendFileAsync(
        LoadedWorkspace workspace,
        string path,
        IEnumerable<DiffHunk> hunks,
        List<string> records,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var document = SourceFile.IsCSharp(path) ? DocumentLookup.Find(workspace, path) : null;

        if (document is null)
        {
            foreach (var hunk in hunks)
                Add(records, seen, Raw(path, hunk, "not a C# document in this workspace"));

            return;
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var folded = new List<Folded>();

        foreach (var hunk in hunks)
            Place(folded, records, seen, Declared(root, model, text, path, hunk, cancellationToken), hunk);

        foreach (var entry in folded)
            Add(records, seen, string.Create(CultureInfo.InvariantCulture, $"{path}:{string.Join(',', entry.Ranges)}  EXACT  {entry.Id}"));
    }

    private static void Add(List<string> records, HashSet<string> seen, string record)
    {
        if (seen.Add(record))
            records.Add(record);
    }

    private static Mapped Declared(
        SyntaxNode? root,
        SemanticModel? model,
        SourceText text,
        string path,
        DiffHunk hunk,
        CancellationToken cancellationToken)
    {
        if (root is null || model is null || Span(text, hunk) is not { } span)
            return new Mapped(null, Raw(path, hunk, "the hunk covers no line of the current file"));

        var containing = root.FindNode(span, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault();

        if (containing is null)
            return new Mapped(null, Raw(path, hunk, "the hunk spans no single declaration"));

        var symbol = model.GetDeclaredSymbol(containing, cancellationToken);

        return symbol is null
            ? new Mapped(null, Raw(path, hunk, "the declaration has no symbol"))
            : new Mapped(SymbolId.From(symbol).Value, null);
    }

    private static TextSpan? Span(SourceText text, DiffHunk hunk)
    {
        if (text.Lines.Count is 0)
            return null;

        var start = Math.Clamp(hunk.Start, 1, text.Lines.Count);

        if (hunk.Count is 0)
            return new TextSpan(text.Lines[start - 1].Start, 0);

        var first = text.Lines[start - 1];
        var last = text.Lines[Math.Clamp(hunk.End, start, text.Lines.Count) - 1];

        return TextSpan.FromBounds(first.Start, last.End);
    }

    private static string Raw(string path, DiffHunk hunk, string reason) => string.Create(
        CultureInfo.InvariantCulture,
        $"{path}:{hunk.Start}-{hunk.End}  HEURISTIC  {reason}");

    private const int MaxSteeredPaths = 3;

    private static void Steer(ResponseBuilder response, IReadOnlyList<string> records, int maxResults)
    {
        var unmapped = records
            .Capped(maxResults)
            .Where(record => record.Contains("  HEURISTIC  ", StringComparison.Ordinal))
            .Select(record => record[..record.IndexOf(':', StringComparison.Ordinal)])
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSteeredPaths)
            .ToArray();
        if (unmapped.Length is 0)
            return;
        response.Note("for the hunk text these could not map, call " + string.Join(" then ", unmapped.Select(path => "diff_text path=" + path)));
    }

    private static void Place(List<Folded> folded, List<string> records, HashSet<string> seen, Mapped mapped, DiffHunk hunk)
    {
        if (mapped.Raw is { Length: > 0 } raw)
        {
            Add(records, seen, raw);

            return;
        }

        var range = string.Create(CultureInfo.InvariantCulture, $"{hunk.Start}-{hunk.End}");

        if (folded.Find(entry => string.Equals(entry.Id, mapped.Id, StringComparison.Ordinal)).Ranges is { } ranges)
            ranges.Add(range);
        else
            folded.Add(new Folded(mapped.Id!, [range]));
    }

    private readonly record struct Mapped(string? Id, string? Raw);

    private readonly record struct Folded(string Id, List<string> Ranges);
}
