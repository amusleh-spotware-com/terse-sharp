using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record DocumentDiff(string Path, string Text, int ChangedLines)
{
    public static async Task<DocumentDiff?> CreateAsync(
        Solution before,
        Solution after,
        DocumentId id,
        CancellationToken cancellationToken)
    {
        var original = before.GetDocument(id);
        var updated = after.GetDocument(id);

        if (updated is null)
            return null;

        var originalText = original is null ? string.Empty : await Read(original, cancellationToken).ConfigureAwait(false);
        var updatedText = await Read(updated, cancellationToken).ConfigureAwait(false);

        if (string.Equals(originalText, updatedText, StringComparison.Ordinal))
            return null;

        var path = updated.FilePath ?? updated.Name;
        var report = UnifiedDiff.Report(path, originalText, updatedText);

        return new DocumentDiff(path, report.Text, report.ChangedLines);
    }

    private static async Task<string> Read(Document document, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return text.ToString();
    }
}

public sealed record EditOptions(string Tool, bool DryRun, bool AllowErrors, bool Verbose = false, System.Collections.Immutable.ImmutableArray<string> Usings = default, System.Collections.Immutable.ImmutableArray<string> Add = default, string? AddTo = null);
