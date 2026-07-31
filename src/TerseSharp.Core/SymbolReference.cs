using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct SymbolQuery(string? ContainingType, string Member, int? ParameterCount);

public static class SymbolReference
{
    private static readonly SymbolDisplayFormat Compact = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string Brief(ISymbol symbol) => symbol.ToDisplayString(Compact);

    public static bool IsDocumentationId(string text) =>
        text.Length > 2 && text[1] is ':' && char.IsUpper(text[0]);

    public static SymbolQuery? Parse(string text)
    {
        var trimmed = text.Trim();
        var open = trimmed.IndexOf('(', StringComparison.Ordinal);
        var name = open < 0 ? trimmed : trimmed[..open];
        var separator = name.LastIndexOf('.');

        return name.Length is 0
            ? null
            : new SymbolQuery(
                separator < 0 ? null : name[..separator],
                separator < 0 ? name : name[(separator + 1)..],
                open < 0 ? null : Arity(trimmed[open..]));
    }

    public static bool Matches(ISymbol symbol, SymbolQuery query) =>
        MatchesContainer(symbol, query.ContainingType) && MatchesArity(symbol, query.ParameterCount);

    private static bool MatchesContainer(ISymbol symbol, string? containingType) =>
        containingType is null
        || string.Equals(symbol.ContainingType?.Name, containingType, StringComparison.Ordinal)
        || symbol is INamedTypeSymbol
        && string.Equals(symbol.ContainingNamespace?.Name, containingType, StringComparison.Ordinal);

    private static bool MatchesArity(ISymbol symbol, int? parameterCount) =>
        parameterCount is null || symbol is IMethodSymbol method && method.Parameters.Length == parameterCount;

    private static int Arity(string parameters)
    {
        var inside = parameters.Trim('(', ')').Trim();

        if (inside.Length is 0)
            return 0;

        var depth = 0;
        var count = 1;

        foreach (var character in inside)
        {
            depth += Nesting(character);

            if (character is ',' && depth is 0)
                count++;
        }

        return count;
    }

    private static int Nesting(char character) => character switch
    {
        '<' or '(' or '[' => 1,
        '>' or ')' or ']' => -1,
        _ => 0,
    };
}
