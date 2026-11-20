using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

internal readonly record struct LandedBatch(Result<Solution> Solution, string Note);

internal static class BatchUsings
{
    public static async Task<Dictionary<DocumentId, HashSet<string>>> ImportedAsync(
        Solution solution,
        IReadOnlyCollection<DocumentId> documents,
        ImmutableArray<string> usings,
        CancellationToken cancellationToken)
    {
        Dictionary<DocumentId, HashSet<string>> imported = [with(documents.Count)];

        foreach (var id in documents)
        {
            imported[id] = solution.GetDocument(id) is { } document
                && await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is CompilationUnitSyntax unit
                    ? Declared(unit, usings)
                    : [];
        }

        return imported;
    }

    public static string Skipped(
        ImmutableArray<string> usings,
        IReadOnlyCollection<DocumentId> documents,
        Func<DocumentId, ImmutableArray<string>> landed,
        Dictionary<DocumentId, HashSet<string>> imported)
    {
        List<string> skipped = [with(usings.Length)];

        foreach (var entry in usings)
        {
            var count = documents.Count(document => !landed(document).Contains(entry) && !imported[document].Contains(entry));

            if (count > 0)
                skipped.Add(string.Create(CultureInfo.InvariantCulture, $"{entry.Trim()} in {count} file(s)"));
        }

        return skipped.Count is 0 ? string.Empty : "\nNOTE usings= skipped in files that do not use them: " + string.Join(", ", skipped);
    }

    private static HashSet<string> Declared(CompilationUnitSyntax unit, ImmutableArray<string> usings)
    {
        HashSet<string> declared = [with(StringComparer.Ordinal)];

        foreach (var directive in unit.Usings)
        {
            if (directive is { Alias: null, StaticKeyword.RawKind: 0, Name: { } name })
                Matched(usings, name.ToString(), declared);
        }

        return declared;
    }

    private static void Matched(ImmutableArray<string> usings, string named, HashSet<string> declared)
    {
        foreach (var entry in usings)
        {
            if (entry.AsSpan().Trim().SequenceEqual(named))
                declared.Add(entry);
        }
    }
}
