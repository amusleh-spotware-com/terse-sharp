using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class SyntaxDeclaration
{
    public static async Task<SyntaxNode?> UniqueAsync(LoadedWorkspace workspace, string text, string path, CancellationToken cancellationToken)
    {
        if (Addressable(text) is not { } query || DocumentLookup.Find(workspace, path) is not { } document)
            return null;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        return root is null ? null : Only(root, query);
    }

    private static SymbolQuery? Addressable(string text)
    {
        var requested = SymbolReference.Unescaped(text);

        return SymbolReference.IsDocumentationId(requested) || SymbolReference.Parse(requested) is not { Parameters: null } query
            ? null
            : Plain(query) ? query : null;
    }

    private static bool Plain(SymbolQuery query) =>
        SyntaxFacts.IsValidIdentifier(query.Member) && query.ContainingType?.Contains('<', StringComparison.Ordinal) is not true;

    private static MemberDeclarationSyntax? Only(SyntaxNode root, SymbolQuery query)
    {
        MemberDeclarationSyntax[] named = [.. root
            .DescendantNodes(Descends)
            .OfType<MemberDeclarationSyntax>()
            .Where(member => Name(member) == query.Member && Contained(member, query.ContainingType))
            .Take(2)];

        return named is [var only] && Readable(only) ? only : null;
    }

    private static bool Descends(SyntaxNode node) =>
        node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax;

    private static string? Name(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        BaseFieldDeclarationSyntax { Declaration.Variables: [var only] } => only.Identifier.ValueText,
        _ => null,
    };

    private static bool Readable(MemberDeclarationSyntax member) =>
        member is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null }
            or PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: null }
            or EventDeclarationSyntax { ExplicitInterfaceSpecifier: null }
            or BaseFieldDeclarationSyntax
        && !member.Modifiers.Any(SyntaxKind.PartialKeyword);

    private static bool Contained(MemberDeclarationSyntax member, string? qualifier)
    {
        if (qualifier is null)
            return true;

        var containers = Containers(member).AsSpan();

        return containers.EndsWith(qualifier, StringComparison.Ordinal)
            && (containers.Length == qualifier.Length || containers[^(qualifier.Length + 1)] is '.');
    }

    private static string Containers(MemberDeclarationSyntax member) => string.Join('.', member.Ancestors().Reverse().Select(Segment).OfType<string>());

    private static string? Segment(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax space => space.Name.ToString(),
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        _ => null,
    };
}
