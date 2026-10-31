using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public readonly record struct PrunedText(DocumentId Document, string Path, string Text);

public static class UsingPruning
{
    private const string UnnecessaryUsing = "CS8019";

    public static async Task<IReadOnlyDictionary<string, HashSet<string>>> UnneededAsync(
        Solution solution,
        IReadOnlyList<PrunedText> texts,
        IReadOnlyList<string> usings,
        CancellationToken cancellationToken)
    {
        var batch = texts.Aggregate(solution, (current, entry) => current.WithDocumentText(entry.Document, SourceText.From(entry.Text)));
        var unneeded = new Dictionary<string, HashSet<string>>(texts.Count, StringComparer.Ordinal);

        foreach (var entry in texts)
        {
            if (batch.GetDocument(entry.Document) is { } document)
                unneeded[entry.Path] = [.. (await UnusedAsync(document, cancellationToken).ConfigureAwait(false)).Where(name => usings.Contains(name, StringComparer.Ordinal))];
        }

        var everywhere = usings.Where(name => unneeded.Count > 0 && unneeded.Values.All(set => set.Contains(name))).ToArray();

        foreach (var set in unneeded.Values)
            set.ExceptWith(everywhere);

        return unneeded;
    }

    private static async Task<HashSet<string>> UnusedAsync(Document document, CancellationToken cancellationToken)
    {
        var unused = new HashSet<string>(StringComparer.Ordinal);

        if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } model
            || await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return unused;
        }

        foreach (var diagnostic in model.GetDiagnostics(cancellationToken: cancellationToken))
        {
            if (diagnostic.Id is UnnecessaryUsing && root.FindNode(diagnostic.Location.SourceSpan) is UsingDirectiveSyntax { Name: { } name })
                unused.Add(name.ToString());
        }

        return unused;
    }
}
