using Microsoft.CodeAnalysis.CSharp;

namespace TerseSharp.Core;

public static class TextPolicyGate
{
    public static async Task<PolicyVerdict> EvaluateAsync(
        string? root,
        IReadOnlyList<PolicySource> sources,
        bool overridden,
        CancellationToken cancellationToken)
    {
        var verdict = PolicyVerdict.Clean with { Overridden = overridden };

        foreach (var source in sources)
        {
            if (SourceFile.IsCSharp(source.Path) && !string.Equals(source.Before, source.After, StringComparison.Ordinal))
                verdict = verdict.Joined(await VerdictAsync(root, source, overridden, cancellationToken).ConfigureAwait(false));
        }

        return verdict;
    }

    private static async Task<PolicyVerdict> VerdictAsync(string? root, PolicySource source, bool overridden, CancellationToken cancellationToken)
    {
        var (directory, display) = Located(root, source.Path);
        var loaded = await PolicyCache.ForAsync(directory, cancellationToken).ConfigureAwait(false);
        var options = loaded.Effective;

        if (!options.Active || GeneratedCode.IsGenerated(directory, source.Path))
            return PolicyVerdict.Clean with { Overridden = overridden, Notice = PolicySettings.Notice(loaded) };

        var introduced = PolicyGate.Introduced(Inspected(source.Before, display, options, cancellationToken), Inspected(source.After, display, options, cancellationToken));

        return new PolicyVerdict(
            [.. introduced.Where(finding => finding.Action is PolicyAction.Reject)],
            [.. introduced.Where(finding => finding.Action is PolicyAction.Warn)],
            options.AllowOverride,
            overridden,
            PolicySettings.Notice(loaded));
    }

    private static (string Directory, string Display) Located(string? root, string path) =>
        root is { } workspaceRoot && PathBoundary.Contains(workspaceRoot, path)
            ? (workspaceRoot, PositionFormat.Relative(workspaceRoot, path))
            : (Path.GetDirectoryName(Path.GetFullPath(path)) ?? path, path);

    private static IReadOnlyList<PolicyFinding> Inspected(string text, string display, PolicyOptions options, CancellationToken cancellationToken) =>
        PolicyService.Inspect(CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken).GetRoot(cancellationToken), display, options);
}
