using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DiagnosticDeclarations
{
    public static async Task<Func<Location, string?>> ResolverAsync(
        IEnumerable<Diagnostic> found,
        CancellationToken cancellationToken)
    {
        var roots = new Dictionary<SyntaxTree, SyntaxNode>();

        foreach (var tree in found.Select(diagnostic => diagnostic.Location.SourceTree).OfType<SyntaxTree>().Distinct())
            roots[tree] = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

        return location => Containing(roots, location);
    }

    private static string? Containing(Dictionary<SyntaxTree, SyntaxNode> roots, Location location) =>
        location.SourceTree is { } tree && roots.TryGetValue(tree, out var root)
            ? UsageContainer.Of(root, location.SourceSpan)
            : null;
}
