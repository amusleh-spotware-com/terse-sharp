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

    private static bool IsSuffix(ReadOnlySpan<char> container, ReadOnlySpan<char> qualifier) =>
        container.Equals(qualifier, StringComparison.Ordinal)
        || (container.Length > qualifier.Length
            && container[container.Length - qualifier.Length - 1] is '.'
            && container[^qualifier.Length..].Equals(qualifier, StringComparison.Ordinal));

    private static bool MatchesParameters(ISymbol symbol, IReadOnlyList<string>? parameters)
    {
        if (parameters is null)
            return true;

        if (symbol is not IMethodSymbol method || method.Parameters.Length != parameters.Count)
            return false;

        return !parameters.Where((text, index) => !MatchesType(method.Parameters[index], text)).Any();
    }

    private static bool MatchesType(IParameterSymbol parameter, string text) =>
        text.Length is 0 || SameType(Normalize(parameter.Type.ToDisplayString(ParameterType)), Normalize(text));

    private static string Normalize(string text)
    {
        if (!text.Contains(' ', StringComparison.Ordinal))
            return text;

        var kept = new System.Text.StringBuilder(text.Length);

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is ' ' && !Separates(kept, text, index))
                continue;

            kept.Append(text[index]);
        }

        return kept.ToString();
    }

    private static string[] Split(string parameters)
    {
        var text = parameters.AsSpan().Trim();
        var inside = text.Length > 1 && text[0] is '(' && text[^1] is ')' ? text[1..^1].Trim() : text;

        if (inside.Length is 0)
            return [];

        var parts = new List<string>();
        var more = true;

        while (more)
        {
            var end = EndOfArgument(inside);

            parts.Add(inside[..end].Trim().ToString());
            more = end < inside.Length;
            inside = more ? inside[(end + 1)..] : default;
        }

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

    private static bool SameType(ReadOnlySpan<char> actual, ReadOnlySpan<char> requested)
    {
        var actualOpen = actual.IndexOfAny('<', '(');
        var requestedOpen = requested.IndexOfAny('<', '(');

        if (actualOpen < 0 || requestedOpen < 0)
            return IsSuffix(Leaf(actual, actualOpen), Leaf(requested, requestedOpen));

        var actualClose = Closing(actual, actualOpen);
        var requestedClose = Closing(requested, requestedOpen);

        return actualClose > 0
            && requestedClose > 0
            && IsSuffix(actual[..actualOpen], requested[..requestedOpen])
            && Unnamed(actual[(actualClose + 1)..]).SequenceEqual(Unnamed(requested[(requestedClose + 1)..]))
            && SameArguments(actual[(actualOpen + 1)..actualClose], requested[(requestedOpen + 1)..requestedClose]);
    }

    private static bool SameArguments(ReadOnlySpan<char> actual, ReadOnlySpan<char> requested)
    {
        var actualMore = true;
        var requestedMore = true;

        while (actualMore && requestedMore)
        {
            if (!SameType(NextArgument(ref actual, ref actualMore), NextArgument(ref requested, ref requestedMore)))
                return false;
        }

        return actualMore == requestedMore;
    }

    private static ReadOnlySpan<char> NextArgument(ref ReadOnlySpan<char> text, ref bool more)
    {
        var end = EndOfArgument(text);
        var argument = text[..end];

        more = end < text.Length;
        text = more ? text[(end + 1)..] : default;

        return argument;
    }

    private static int EndOfArgument(ReadOnlySpan<char> text)
    {
        var depth = 0;

        for (var index = 0; index < text.Length; index++)
        {
            depth += Nesting(text[index]);

            if (text[index] is ',' && depth is 0)
                return index;
        }

        return text.Length;
    }

    private static int Closing(ReadOnlySpan<char> text, int open)
    {
        var depth = 0;

        for (var index = open; index < text.Length; index++)
        {
            depth += Nesting(text[index]);

            if (depth is 0)
                return index;
        }

        return -1;
    }

    private static ReadOnlySpan<char> Unnamed(ReadOnlySpan<char> element)
    {
        var name = element.LastIndexOf(' ');

        return name < 0 ? element : element[..name].TrimEnd();
    }

    private static bool Separates(System.Text.StringBuilder kept, string text, int index) =>
        kept.Length > 0
        && EndsAType(kept[^1])
        && index + 1 < text.Length
        && StartsAName(text[index + 1]);

    private static ReadOnlySpan<char> Leaf(ReadOnlySpan<char> text, int open) =>
        open < 0 ? Unnamed(text) : text;

    private static bool EndsAType(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '>' or ')' or ']' or '?';

    private static bool StartsAName(char character) =>
        char.IsLetter(character) || character is '_';
}
