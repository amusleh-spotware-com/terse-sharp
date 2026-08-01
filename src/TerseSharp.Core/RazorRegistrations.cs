using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class RazorRegistrations
{
    public static async Task<IReadOnlySet<string>> NamesAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in Documents(workspace))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is not null)
                names.UnionWith(In(root));
        }

        return names;
    }

    private static IEnumerable<Document> Documents(LoadedWorkspace workspace) => workspace
        .Solution
        .Projects
        .SelectMany(project => project.Documents)
        .Where(document => document.FilePath is { Length: > 0 } && !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static IEnumerable<string> In(SyntaxNode root) => root
        .DescendantNodes()
        .OfType<InvocationExpressionSyntax>()
        .Where(Registers)
        .SelectMany(Named);

    private static bool Registers(InvocationExpressionSyntax invocation) =>
        Method(invocation) is { } name
        && (name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("TryAdd", StringComparison.Ordinal));

    private static string? Method(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    private static IEnumerable<string> Named(InvocationExpressionSyntax invocation) =>
        Arguments(invocation).Concat(TypeArguments(invocation));

    private static IEnumerable<string> TypeArguments(InvocationExpressionSyntax invocation) => invocation.Expression
        .DescendantNodesAndSelf()
        .OfType<GenericNameSyntax>()
        .SelectMany(generic => generic.TypeArgumentList.Arguments)
        .Select(Simple);

    private static IEnumerable<string> Arguments(InvocationExpressionSyntax invocation) => invocation.ArgumentList.Arguments
        .Select(argument => argument.Expression)
        .OfType<TypeOfExpressionSyntax>()
        .Select(expression => Simple(expression.Type));

    private static string Simple(TypeSyntax type) => type switch
    {
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => Simple(qualified.Right),
        _ => type.ToString(),
    };
}
