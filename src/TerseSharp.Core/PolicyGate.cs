using System.Collections.Immutable;
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
        var options = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);
        var notice = PolicySettings.Notice(options);

        if (!options.Active)
            return PolicyVerdict.Clean with { Notice = notice };

        var baseline = await KeysAsync(workspace.Solution, changed, workspace.Root, options, cancellationToken).ConfigureAwait(false);
        var current = await FindingsAsync(after, changed, workspace.Root, options, cancellationToken).ConfigureAwait(false);
        var introduced = current.Where(finding => !baseline.Contains(finding.Key)).ToArray();

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
        var options = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);

        return options.Active
            ? await FindingsAsync(workspace.Solution, documents, workspace.Root, options, cancellationToken).ConfigureAwait(false)
            : [];
    }

    private static async Task<HashSet<string>> KeysAsync(
        Solution solution,
        IReadOnlyList<DocumentId> changed,
        string root,
        PolicyOptions options,
        CancellationToken cancellationToken)
    {
        var found = await FindingsAsync(solution, changed, root, options, cancellationToken).ConfigureAwait(false);

        return [.. found.Select(finding => finding.Key)];
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
}
