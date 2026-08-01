using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record RazorParameter(string Name, string Type, bool Required, bool Cascading, bool CaptureUnmatched)
{
    public string Kind => (Required, Cascading) switch
    {
        (_, true) => "[CascadingParameter]",
        (true, _) => "[Parameter, EditorRequired]",
        _ => "[Parameter]",
    };
}

public sealed record RazorComponent(
    INamedTypeSymbol Type,
    IReadOnlyList<RazorParameter> Parameters,
    IReadOnlyList<string> Routes)
{
    public string FullName => Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    public RazorParameter? Parameter(string name) => Parameters
        .FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal));
}

public static class RazorSemantics
{
    private const string ComponentInterface = "Microsoft.AspNetCore.Components.IComponent";

    private static readonly string[] AmbientScopes =
    [
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Web",
        "Microsoft.AspNetCore.Components.Forms",
        "Microsoft.AspNetCore.Components.Routing",
    ];

    public static IReadOnlyList<string> Scopes(RazorDocument document, IEnumerable<RazorDocument> imports, string? ownNamespace)
    {
        var scopes = new List<string>(AmbientScopes.Length + 8);

        scopes.AddRange(imports.SelectMany(import => import.Usings).Select(Normalize));
        scopes.AddRange(document.Usings.Select(Normalize));
        scopes.AddRange(AmbientScopes);

        if (ownNamespace is { Length: > 0 })
            scopes.Add(ownNamespace);

        return [.. scopes.Where(scope => scope.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    public static INamedTypeSymbol? Resolve(Compilation compilation, IReadOnlyList<string> scopes, string tagName)
    {
        if (Qualified(compilation, tagName) is { } qualified)
            return qualified;

        foreach (var scope in scopes)
        {
            if (Qualified(compilation, scope + "." + tagName) is { } found)
                return found;
        }

        return null;
    }

    public static bool IsComponent(INamedTypeSymbol type) => type.AllInterfaces
        .Any(contract => string.Equals(
            contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "global::" + ComponentInterface,
            StringComparison.Ordinal));

    public static RazorComponent Describe(INamedTypeSymbol type) =>
        new(type, [.. Parameters(type)], [.. Routes(type)]);

    public static IEnumerable<string> Routes(INamedTypeSymbol type) => type
        .GetAttributes()
        .Where(attribute => string.Equals(attribute.AttributeClass?.Name, "RouteAttribute", StringComparison.Ordinal))
        .SelectMany(attribute => attribute.ConstructorArguments)
        .Select(argument => argument.Value as string)
        .OfType<string>();

    private static IEnumerable<RazorParameter> Parameters(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>().Where(IsParameter))
                yield return Describe(property);
        }
    }

    private static bool IsParameter(IPropertySymbol property) =>
        property.GetAttributes().Any(attribute => Attribute(attribute) is "ParameterAttribute" or "CascadingParameterAttribute");

    private static RazorParameter Describe(IPropertySymbol property) => new(
        property.Name,
        property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        Marked(property, "EditorRequiredAttribute"),
        Marked(property, "CascadingParameterAttribute"),
        Unmatched(property));

    private static bool Marked(IPropertySymbol property, string attributeName) =>
        property.GetAttributes().Any(attribute => string.Equals(Attribute(attribute), attributeName, StringComparison.Ordinal));

    private static bool Unmatched(IPropertySymbol property) => property
        .GetAttributes()
        .Where(attribute => string.Equals(Attribute(attribute), "ParameterAttribute", StringComparison.Ordinal))
        .SelectMany(attribute => attribute.NamedArguments)
        .Any(argument => string.Equals(argument.Key, "CaptureUnmatchedValues", StringComparison.Ordinal)
                         && argument.Value.Value is true);

    private static string? Attribute(AttributeData attribute) => attribute.AttributeClass?.Name;

    private static INamedTypeSymbol? Qualified(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name)
        ?? compilation.GetTypeByMetadataName(name + "`1")
        ?? compilation.GetTypeByMetadataName(name + "`2");

    private static string Normalize(string declaration) => declaration
        .Replace("static ", string.Empty, StringComparison.Ordinal)
        .TrimEnd(';', ' ')
        .Trim();
}
