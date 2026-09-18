using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class VisibleShapeFixes
{
    public const string Reason =
        "its fix rewrites the shape of an externally visible member, which a Razor template, a data binding, a serializer or reflection can break on without the compiler or this compile gate seeing it - apply it by hand, make the member private or internal first, or run cleanup fix=analyzers, which mirrors CI and applies it";

    private static readonly FrozenSet<string> Guarded = new[] { "CA1822" }.ToFrozenSet(StringComparer.Ordinal);

    public static bool Guards(string identifier) => Guarded.Contains(identifier);

    public static async Task<ImmutableArray<Diagnostic>> FixableAsync(
        Project project,
        ImmutableArray<Diagnostic> pending,
        CancellationToken cancellationToken)
    {
        if (pending.IsEmpty || !Guards(pending[0].Id))
            return pending;

        var fixable = ImmutableArray.CreateBuilder<Diagnostic>(pending.Length);

        foreach (var diagnostic in pending)
        {
            if (!await VisibleAsync(project, diagnostic, cancellationToken).ConfigureAwait(false))
                fixable.Add(diagnostic);
        }

        return fixable.ToImmutable();
    }

    private static async Task<bool> VisibleAsync(Project project, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        if (Declaring(project, diagnostic) is not { } document)
            return true;

        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (model is null || root is null || Member(root, diagnostic.Location.SourceSpan) is not { } member)
            return true;

        return model.GetDeclaredSymbol(member, cancellationToken) is not { } symbol || Visible(symbol);
    }

    private static Document? Declaring(Project project, Diagnostic diagnostic) =>
            diagnostic.Location.SourceTree is { } tree ? project.GetDocument(tree) : null;

    private static MemberDeclarationSyntax? Member(SyntaxNode root, TextSpan span) => root.FullSpan.Contains(span)
        ? root.FindNode(span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault()
        : null;

    public static bool Visible(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (!Reachable(current))
                return false;
        }

        return true;
    }

    private static bool Reachable(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        Accessibility.Protected or Accessibility.ProtectedOrInternal => Derivable(symbol.ContainingType),
        _ => false,
    };

    private static bool Derivable(INamedTypeSymbol? type) => type is not null && !type.IsSealed && !type.IsStatic;
}
