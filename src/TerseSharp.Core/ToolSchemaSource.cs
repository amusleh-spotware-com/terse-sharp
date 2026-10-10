using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record DeclaredTool(string Name, int Basis, int Parameters);

public static class ToolSchemaSource
{
    public static async Task<IReadOnlyList<DeclaredTool>> DeclaredAsync(Solution solution, CancellationToken cancellationToken)
    {
        var declared = new List<DeclaredTool>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
                await CollectAsync(document, declared, cancellationToken).ConfigureAwait(false);
        }

        return declared;
    }

    public static async Task<IReadOnlyList<DeclaredTool>> DeclaredAsync(Document document, CancellationToken cancellationToken)
    {
        var declared = new List<DeclaredTool>();

        await CollectAsync(document, declared, cancellationToken).ConfigureAwait(false);

        return declared;
    }

    private static async Task CollectAsync(Document document, List<DeclaredTool> declared, CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
            return;

        SemanticModel? model = null;

        foreach (var method in root.DescendantNodes(static node => node is BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax or not MemberDeclarationSyntax).OfType<MethodDeclarationSyntax>())
        {
            if (Attribute(method.AttributeLists, "McpServerTool") is not { } tool)
                continue;

            model ??= await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (model is not null && Declared(method, tool, model) is { } found)
                declared.Add(found);
        }
    }

    private static DeclaredTool? Declared(MethodDeclarationSyntax method, AttributeSyntax tool, SemanticModel model)
    {
        if (Named(tool, "Name", model) is not { Length: > 0 } name)
            return null;

        var basis = name.Length + Described(method.AttributeLists, model);
        var parameters = 0;

        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (model.GetDeclaredSymbol(parameter) is { Type.Name: nameof(CancellationToken) })
                continue;

            basis += parameter.Identifier.ValueText.Length + Described(parameter.AttributeLists, model);
            parameters++;
        }

        return new DeclaredTool(name, basis, parameters);
    }

    private static string? Named(AttributeSyntax attribute, string argument, SemanticModel model)
    {
        foreach (var candidate in attribute.ArgumentList?.Arguments ?? default)
        {
            if (string.Equals(candidate.NameEquals?.Name.Identifier.ValueText, argument, StringComparison.Ordinal))
                return model.GetConstantValue(candidate.Expression).Value as string;
        }

        return null;
    }

    private static int Described(SyntaxList<AttributeListSyntax> lists, SemanticModel model) =>
        Attribute(lists, "Description") is { ArgumentList.Arguments: [var text, ..] }
            && model.GetConstantValue(text.Expression).Value is string value
                ? value.Length
                : 0;

    private static AttributeSyntax? Attribute(SyntaxList<AttributeListSyntax> lists, string name)
    {
        foreach (var list in lists)
        {
            foreach (var attribute in list.Attributes)
            {
                if (Matches(attribute.Name, name))
                    return attribute;
            }
        }

        return null;
    }

    private static bool Matches(NameSyntax syntax, string name)
    {
        var simple = syntax switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax plain => plain.Identifier.ValueText,
            _ => string.Empty,
        };

        return simple.StartsWith(name, StringComparison.Ordinal)
            && (simple.Length == name.Length || simple.AsSpan(name.Length).SequenceEqual("Attribute"));
    }
}
