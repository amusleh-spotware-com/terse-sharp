using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record XamlRenameResult(int Files, int Sites, IReadOnlyList<XamlUsage> Skipped);

public static class XamlRename
{
    public static async Task<XamlRenameResult> Apply(LoadedWorkspace workspace, ISymbol symbol, string newName, bool dryRun)
    {
        var usages = XamlUsageService.Find(workspace, symbol, newName);
        var exact = usages.Where(usage => usage.Confidence is "EXACT").ToArray();
        var skipped = usages.Where(usage => usage.Confidence is not "EXACT").ToArray();
        var files = exact.Select(usage => usage.File).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (!dryRun)
        {
            foreach (var file in files)
                await Rewrite(Path.Combine(workspace.Root, file), exact.Where(usage => Same(usage.File, file))).ConfigureAwait(false);
        }

        return new XamlRenameResult(files.Length, exact.Length, skipped);
    }

    private static async Task Rewrite(string full, IEnumerable<XamlUsage> usages)
    {
        var text = await File.ReadAllTextAsync(full).ConfigureAwait(false);

        foreach (var usage in usages.Where(usage => !Same(usage.Text, usage.Replacement)))
            text = text.Replace('"' + usage.Text + '"', '"' + usage.Replacement + '"', StringComparison.Ordinal);

        await AtomicWrite.TextAsync(full, text).ConfigureAwait(false);
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
