using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class UsageContainer
{
    public static string? Of(SyntaxNode? root, TextSpan span)
    {
        var node = root?.FindNode(span, getInnermostNodeForTie: true);
        var declaration = node?.AncestorsAndSelf().FirstOrDefault(candidate => Identifier(candidate) is not null);

        return declaration is null ? null : Qualified(declaration);
    }

    private static string Qualified(SyntaxNode declaration)
    {
        var name = Identifier(declaration)!;
        var type = declaration.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();

        return type is null ? name : type.Identifier.ValueText + "." + name;
    }

    private static string? Identifier(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.ValueText,
        OperatorDeclarationSyntax @operator => "operator " + @operator.OperatorToken.ValueText,
        IndexerDeclarationSyntax => "this[]",
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
        EnumMemberDeclarationSyntax member => member.Identifier.ValueText,
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
        _ => null,
    };
}
