using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

internal static class Cohesion
{
    public const int LowCohesionComponents = 3;
    public const double LowCohesionPenalty = 1.0;

    private const string Call = "()";

    public static IEnumerable<CodeSmell> Smells(SyntaxNode root) => root.DescendantNodes()
        .OfType<TypeDeclarationSyntax>()
        .Where(IsWhole)
        .Where(type => Components(type) >= LowCohesionComponents)
        .Select(type => new CodeSmell("Low Cohesion", CodeHealth.Member(type), LowCohesionPenalty));

    public static int Components(TypeDeclarationSyntax type)
    {
        var functions = type.Members.Where(IsInstanceFunction).ToList();
        var graph = new Graph(functions.Count, State(type), functions.Select(Name).ToHashSet(StringComparer.Ordinal));

        for (var index = 0; index < functions.Count; index++)
            graph.Connect(index, Name(functions[index]), References(functions[index]));

        return graph.Count;
    }

    private static bool IsInstanceFunction(MemberDeclarationSyntax member) =>
        !member.Modifiers.Any(modifier => modifier.ValueText is "static") && IsBodiedFunction(member);

    private static bool IsBodiedFunction(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax { Body: not null } method => !PolicyService.IsTest(method),
        MethodDeclarationSyntax { ExpressionBody: not null } method => !PolicyService.IsTest(method),
        PropertyDeclarationSyntax property => property.ExpressionBody is not null || HasAccessorBody(property),
        _ => false,
    };

    private static bool HasAccessorBody(PropertyDeclarationSyntax property) =>
        property.AccessorList?.Accessors.Any(accessor => accessor.Body is not null || accessor.ExpressionBody is not null) is true;

    private static string Name(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        _ => string.Empty,
    };

    private static HashSet<string> State(TypeDeclarationSyntax type)
    {
        var fields = type.Members.OfType<BaseFieldDeclarationSyntax>()
            .Where(field => !field.Modifiers.Any(modifier => modifier.ValueText is "const"))
            .SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Identifier.ValueText);
        var properties = type.Members.OfType<PropertyDeclarationSyntax>()
            .Where(property => property.ExpressionBody is null && !HasAccessorBody(property))
            .Select(property => property.Identifier.ValueText);
        var parameters = type.ParameterList?.Parameters.Select(parameter => parameter.Identifier.ValueText) ?? [];

        return [.. fields.Concat(properties).Concat(parameters)];
    }

    private static IEnumerable<string> References(MemberDeclarationSyntax member) => member.DescendantNodes()
        .OfType<IdentifierNameSyntax>()
        .Where(IsOwnAccess)
        .Select(identifier => identifier.Identifier.ValueText);

    private static bool IsOwnAccess(IdentifierNameSyntax identifier) =>
        identifier.Parent is not MemberAccessExpressionSyntax access || access.Name != identifier || access.Expression is ThisExpressionSyntax;

    private sealed class Graph(int size, HashSet<string> state, HashSet<string> callable)
    {
        private readonly int[] parents = [.. Enumerable.Range(0, size)];
        private readonly bool[] linked = new bool[size];
        private readonly Dictionary<string, int> owners = new(StringComparer.Ordinal);

        public int Count => Enumerable.Range(0, parents.Length).Where(index => linked[index]).Select(Find).Distinct().Count();

        public void Connect(int index, string name, IEnumerable<string> references)
        {
            Claim(Call + name, index);

            foreach (var reference in references)
            {
                if (state.Contains(reference))
                    Touch(reference, index);
                else if (callable.Contains(reference))
                    Touch(Call + reference, index);
            }
        }

        private void Touch(string name, int index)
        {
            linked[index] = true;
            Claim(name, index);
        }

        private void Claim(string name, int index)
        {
            if (owners.TryGetValue(name, out var owner))
                parents[Find(owner)] = Find(index);
            else
                owners[name] = index;
        }

        private int Find(int index)
        {
            while (parents[index] != index)
                index = parents[index] = parents[parents[index]];

            return index;
        }
    }

    private static bool IsWhole(TypeDeclarationSyntax type) =>
        type is not InterfaceDeclarationSyntax && !type.Modifiers.Any(modifier => modifier.ValueText is "partial");
}
