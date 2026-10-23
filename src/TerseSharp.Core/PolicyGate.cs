using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class PolicyGate
{
    public static async Task<PolicyVerdict> EvaluateAsync(
            LoadedWorkspace workspace,
            Solution after,
            IReadOnlyList<DocumentId> changed,
            bool overridden,
            CancellationToken cancellationToken)
    {
        var loaded = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);
        var notice = PolicySettings.Notice(loaded);
        var options = loaded.Effective;

        if (!options.Active)
            return PolicyVerdict.Clean with { Notice = notice };

        var baseline = await FindingsAsync(workspace.Solution, changed, workspace.Root, options, cancellationToken).ConfigureAwait(false);
        var current = await FindingsAsync(after, changed, workspace.Root, options, cancellationToken).ConfigureAwait(false);
        var introduced = Introduced(baseline, current);

        return new PolicyVerdict(
            [.. introduced.Where(finding => finding.Action is PolicyAction.Reject)],
            [.. introduced.Where(finding => finding.Action is PolicyAction.Warn)],
            options.AllowOverride,
            overridden,
            notice);
    }

    public static async Task<IReadOnlyList<PolicyFinding>> ScanAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<DocumentId> documents,
        CancellationToken cancellationToken)
    {
        var options = (await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false)).Effective;

        return options.Active
            ? await FindingsAsync(workspace.Solution, documents, workspace.Root, options, cancellationToken).ConfigureAwait(false)
            : [];
    }

    private static async Task<IReadOnlyList<PolicyFinding>> FindingsAsync(
        Solution solution,
        IReadOnlyList<DocumentId> changed,
        string root,
        PolicyOptions options,
        CancellationToken cancellationToken)
    {
        var found = ImmutableArray.CreateBuilder<PolicyFinding>();

        foreach (var id in changed)
        {
            if (solution.GetDocument(id) is not { } document || !Inspectable(document, root))
                continue;

            if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } syntax)
                continue;

            found.AddRange(PolicyService.Inspect(syntax, PositionFormat.Relative(root, document.FilePath), options));
        }

        return found.ToImmutable();
    }

    private static bool Inspectable(Document document, string root) =>
        string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal)
            && !GeneratedCode.IsGenerated(root, document.FilePath);

    public static PolicyFinding[] Introduced(IReadOnlyList<PolicyFinding> before, IReadOnlyList<PolicyFinding> after)
    {
        var exact = Tally(before, static finding => (finding.Key, finding.Measured));
        var named = Tally(before, static finding => finding.Key);
        var unmatched = new List<(PolicyFinding Finding, string Key)>(after.Count);

        foreach (var finding in after)
        {
            var key = finding.Key;

            if (Take(exact, (key, finding.Measured)))
                Take(named, key);
            else
                unmatched.Add((finding, key));
        }

        return [.. unmatched.Where(entry => !Take(named, entry.Key)).Select(entry => entry.Finding)];
    }

    private static Dictionary<TKey, int> Tally<TKey>(IReadOnlyList<PolicyFinding> findings, Func<PolicyFinding, TKey> key)
        where TKey : notnull
    {
        var counts = new Dictionary<TKey, int>(findings.Count);

        foreach (var finding in findings)
            CollectionsMarshal.GetValueRefOrAddDefault(counts, key(finding), out _)++;

        return counts;
    }

    private static bool Take<TKey>(Dictionary<TKey, int> counts, TKey key)
        where TKey : notnull
    {
        if (!counts.TryGetValue(key, out var left) || left is 0)
            return false;

        counts[key] = left - 1;
        return true;
    }
}
