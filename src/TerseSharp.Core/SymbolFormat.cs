using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SymbolFormat
{
    private static readonly SymbolDisplayFormat Signature = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string Describe(ISymbol symbol) => symbol.ToDisplayString(Signature);

    public static string Kind(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol named => named.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "ctor",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    public static string Accessibility(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.NotApplicable => "-",
        var accessibility => accessibility.ToString().ToLowerInvariant(),
    };

    public static string Location(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);

        return location is null ? "-" : PositionFormat.Describe(location);
    }

    public static string Location(string root, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);

        return location is null ? "-" : PositionFormat.Describe(root, location);
    }
}
