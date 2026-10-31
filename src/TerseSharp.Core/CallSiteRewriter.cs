using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public sealed record SiteRewrite(Solution Solution, DocumentId[] Changed, IReadOnlyList<Location> Unplaced);

public static class CallSiteRewriter
{
    public static async Task<SiteRewrite> RewrittenAsync(
        Solution solution,
        SignatureTarget target,
        SignatureChange change,
        CancellationToken cancellationToken)
    {
        var found = ReferenceSites.Moves(change) ? await SymbolFinder.FindReferencesAsync(target.Method, solution, cancellationToken).ConfigureAwait(false) : [];
        var sites = ReferenceSites.Direct(found, target.Method);
        var context = new RewriteContext(target, change, ReferenceSites.Cascaded(found, target.Method));
        var (updated, changed) = await AppliedAsync(solution, sites, context, cancellationToken).ConfigureAwait(false);

        return new SiteRewrite(updated, changed, context.Unplaced);
    }

    private static async Task<(Solution Solution, DocumentId[] Changed)> AppliedAsync(
        Solution solution,
        ILookup<DocumentId, ReferenceLocation> sites,
        RewriteContext context,
        CancellationToken cancellationToken)
    {
        var updated = solution;
        var changed = new List<DocumentId>(sites.Count + 1);

        foreach (var id in sites.Select(group => group.Key).Append(context.Target.Declaring.Id).Distinct())
        {
            if (await RewrittenRootAsync(solution.GetDocument(id), sites[id], context, cancellationToken).ConfigureAwait(false) is not { } root)
                continue;

            updated = updated.WithDocumentSyntaxRoot(id, root);
            changed.Add(id);
        }

        return (updated, [.. changed]);
    }

    private static async Task<SyntaxNode?> RewrittenRootAsync(
        Document? document,
        IEnumerable<ReferenceLocation> sites,
        RewriteContext context,
        CancellationToken cancellationToken)
    {
        var root = document is null ? null : await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (document is null || root is null)
            return null;

        var model = context.Target.Method.IsExtensionMethod ? await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) : null;
        var nodes = Located(root, sites, context);
        var declares = root.SyntaxTree == context.Target.Declaration.SyntaxTree;

        if (declares)
            nodes.Add(context.Target.Declaration);

        var rewritten = root.ReplaceNodes(nodes, (original, current) => Replaced(original, current, context, model));

        return declares || !rewritten.IsEquivalentTo(root) ? rewritten : null;
    }

    private static List<SyntaxNode> Located(SyntaxNode root, IEnumerable<ReferenceLocation> sites, RewriteContext context)
    {
        var nodes = new List<SyntaxNode>();

        foreach (var site in sites)
        {
            if (ArgumentsAt(root, site.Location.SourceSpan) is { } list)
                nodes.Add(list);
            else if (ReferenceSites.BindsArguments(site.Location))
                context.Unplaced.Add(site.Location);
        }

        return nodes;
    }

    private static SyntaxNode Replaced(SyntaxNode original, SyntaxNode current, RewriteContext context, SemanticModel? model) =>
        (original, current) switch
        {
            (MethodDeclarationSyntax, MethodDeclarationSyntax method) when original == context.Target.Declaration => method.WithParameterList(context.Change.Parameters),
            (ArgumentListSyntax list, ArgumentListSyntax rewritten) => Placed(list, rewritten, context, model),
            _ => current,
        };

    private static ArgumentListSyntax Placed(ArgumentListSyntax list, ArgumentListSyntax rewritten, RewriteContext context, SemanticModel? model)
    {
        if (ArgumentOrder.Rearranged(list, rewritten, context.Change, Receiver(model, list)) is { } placed)
            return placed;

        context.Unplaced.Add(list.GetLocation());

        return rewritten;
    }

    private static int Receiver(SemanticModel? model, ArgumentListSyntax arguments) =>
        model is not null
        && arguments.Parent is InvocationExpressionSyntax invocation
        && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension }
            ? 1
            : 0;

    private static ArgumentListSyntax? ArgumentsAt(SyntaxNode root, TextSpan span)
    {
        var node = root.FindToken(span.Start).Parent;

        while (node is not null && Callee(node) is { } outer)
            node = outer;

        return node?.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node ? invocation.ArgumentList : null;
    }

    private static SyntaxNode? Callee(SyntaxNode node) =>
        node.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == node => access,
            MemberBindingExpressionSyntax binding => binding,
            _ => null,
        };

    private sealed record RewriteContext(SignatureTarget Target, SignatureChange Change, List<Location> Unplaced);
}
