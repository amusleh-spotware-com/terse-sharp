using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct SymbolQuery(string? ContainingType, string Member, IReadOnlyList<string>? Parameters);

public static class SymbolReference
{
    private static readonly SymbolDisplayFormat Compact = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly SymbolDisplayFormat Qualified = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    private static readonly SymbolDisplayFormat ParameterType = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string Brief(ISymbol symbol) => symbol.ToDisplayString(Compact);

    public static bool RoundTrips(ISymbol symbol) =>
        IsAddressableName(symbol) && !IsGeneric(symbol) && ContainerIsAddressable(symbol);

    private static bool IsAddressableName(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.MethodKind is MethodKind.Ordinary && method.ExplicitInterfaceImplementations.IsEmpty,
        IPropertySymbol property => !property.IsIndexer && property.ExplicitInterfaceImplementations.IsEmpty,
        IEventSymbol @event => @event.ExplicitInterfaceImplementations.IsEmpty,
        IFieldSymbol or INamedTypeSymbol => true,
        _ => false,
    };

    private static bool IsGeneric(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.IsGenericMethod,
        INamedTypeSymbol type => type.IsGenericType,
        _ => false,
    };

    private static bool ContainerIsAddressable(ISymbol symbol) =>
        symbol.ContainingType is not { IsGenericType: true };

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
                open < 0 ? null : Split(trimmed[open..]));
    }

    public static bool Matches(ISymbol symbol, SymbolQuery query) =>
        MatchesContainer(symbol, query.ContainingType) && MatchesParameters(symbol, query.Parameters);

    private static bool MatchesContainer(ISymbol symbol, string? qualifier) =>
        qualifier is null || Containers(symbol).Any(container => IsSuffix(container, qualifier));

    private static IEnumerable<string> Containers(ISymbol symbol)
    {
        if (symbol.ContainingType is { } type)
            yield return type.ToDisplayString(Qualified);

        if (symbol is INamedTypeSymbol && symbol.ContainingNamespace is { IsGlobalNamespace: false } space)
            yield return space.ToDisplayString();
    }

    private static bool IsSuffix(string container, string qualifier) =>
        string.Equals(container, qualifier, StringComparison.Ordinal)
        || container.EndsWith("." + qualifier, StringComparison.Ordinal);

    private static bool MatchesParameters(ISymbol symbol, IReadOnlyList<string>? parameters)
    {
        if (parameters is null)
            return true;

        if (symbol is not IMethodSymbol method || method.Parameters.Length != parameters.Count)
            return false;

        return !parameters.Where((text, index) => !MatchesType(method.Parameters[index], text)).Any();
    }

    private static bool MatchesType(IParameterSymbol parameter, string text) =>
        text.Length is 0 || IsSuffix(Normalize(parameter.Type.ToDisplayString(ParameterType)), Normalize(text));

    private static string Normalize(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string[] Split(string parameters)
    {
        var inside = parameters.Trim('(', ')').Trim();

        if (inside.Length is 0)
            return [];

        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < inside.Length; index++)
        {
            depth += Nesting(inside[index]);

            if (inside[index] is ',' && depth is 0)
            {
                parts.Add(inside[start..index].Trim());
                start = index + 1;
            }
        }

        parts.Add(inside[start..].Trim());

        return [.. parts];
    }

    private static int Nesting(char character) => character switch
    {
        '<' or '(' or '[' => 1,
        '>' or ')' or ']' => -1,
        _ => 0,
    };

    private static readonly SymbolDisplayFormat Named = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string Simple(ISymbol symbol) => symbol.ToDisplayString(Named);

    public static string Unescaped(string text) => text.Contains('&', StringComparison.Ordinal) ? System.Net.WebUtility.HtmlDecode(text) : text;
}
