using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class WorkspaceDivergence
{
    public const int MaxProbed = 50;

    public static async Task<Divergence> FindAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var edited = workspace.EditedPaths;

        if (edited.Count is 0)
            return new Divergence([], 0, Probed: false);

        var divergent = new List<string>();

        foreach (var file in edited.Take(MaxProbed))
        {
            if (DocumentLookup.Find(workspace, file) is not { } document)
                continue;

            if (await DiffersAsync(document, file, cancellationToken).ConfigureAwait(false))
                divergent.Add(PositionFormat.Relative(workspace.Root, file));
        }

        divergent.Sort(StringComparer.Ordinal);

        return new Divergence(divergent, edited.Count, Probed: true);
    }

    public static string? Describe(Divergence divergence, bool verbose)
    {
        if (!divergence.Probed)
            return verbose ? "disk=not probed - this server has applied no edit since the workspace loaded" : null;

        if (divergence.Files.Count is 0)
        {
            return verbose
                ? string.Create(CultureInfo.InvariantCulture, $"disk=in-sync  probed={Math.Min(divergence.Changed, MaxProbed)} of {divergence.Changed} document(s) changed since load")
                : null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"WARNING workspace=diverged - {divergence.Files.Count} document(s) differ from disk: {string.Join(", ", divergence.Files)}")
            + "\n  every read answers from the in-memory text, so re-apply the edit or call load_workspace reload=true"
            + (divergence.Changed > MaxProbed
                ? string.Create(CultureInfo.InvariantCulture, $"\n  probed the first {MaxProbed} of {divergence.Changed} documents changed since load")
                : string.Empty);
    }

    private static async Task<bool> DiffersAsync(Document document, string file, CancellationToken cancellationToken)
    {
        try
        {
            var memory = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var disk = SourceText.From(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));

            return !memory.ContentEquals(disk);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public readonly record struct Divergence(IReadOnlyList<string> Files, int Changed, bool Probed);
}
