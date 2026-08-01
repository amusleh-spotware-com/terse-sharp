using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record XamlBindingSite(XamlElementInfo Element, XAttribute Attribute, string Expression, int Line);

public static partial class XamlBindingService
{
    public static IEnumerable<XamlBindingSite> Sites(XamlDocument document)
    {
        foreach (var element in document.Elements())
        {
            foreach (var attribute in element.Element.Attributes())
            {
                var match = BindingExpression().Match(attribute.Value);

                if (match.Success)
                    yield return new XamlBindingSite(element, attribute, match.Value, XamlDocument.Line(attribute));
            }
        }
    }

    public static string? PathOf(string expression)
    {
        var body = Inside(expression);

        if (body.Length is 0)
            return string.Empty;

        var explicitPath = PathAssignment().Match(body);

        if (explicitPath.Success)
            return Simple(explicitPath.Groups[1].Value.Trim());

        var first = body.Split(',')[0].Trim();

        return first.Contains('=', StringComparison.Ordinal) ? null : Simple(first);
    }

    private static string? Simple(string path) =>
        path.Length is not 0 && path.All(character => char.IsLetterOrDigit(character) || character is '_' or '.')
        && !path.Split('.').Any(string.IsNullOrEmpty)
            ? path
            : null;

    public static string? ContextTypeName(XamlElementInfo element)
    {
        for (var current = element.Element; current is not null; current = current.Parent)
        {
            var declared = Declared(current);

            if (declared is not null)
                return declared;
        }

        return null;
    }

    public static async Task<INamedTypeSymbol?> ResolveTypeAsync(
        LoadedWorkspace workspace,
        XamlDocument document,
        string qualified,
        CancellationToken cancellationToken)
    {
        var separator = qualified.IndexOf(':', StringComparison.Ordinal);
        var name = separator < 0 ? qualified : qualified[(separator + 1)..];
        var space = separator < 0 ? null : document.ClrNamespaceOf(qualified[..separator]);

        if (separator >= 0 && space is null)
            return null;

        return await FindTypeAsync(workspace, space is null ? name : space + "." + name, space is null ? name : null, cancellationToken)
            .ConfigureAwait(false);
    }

    public static IReadOnlyList<ISymbol> Members(INamedTypeSymbol type, string name)
    {
        foreach (var declaring in Lineage(type))
        {
            var found = declaring.GetMembers(name);

            if (found.Length > 0)
                return found;
        }

        return [];
    }

    private static IEnumerable<INamedTypeSymbol> Lineage(INamedTypeSymbol type)
    {
        for (var current = (INamedTypeSymbol?)type; current is not null; current = current.BaseType)
            yield return current;

        foreach (var contract in type.AllInterfaces)
            yield return contract;
    }

    public static string? Nearest(INamedTypeSymbol type, string name) => Lineage(type)
        .SelectMany(declaring => declaring.GetMembers().Where(Bindable))
        .FirstOrDefault(member => member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || member.Name.StartsWith(name[..Math.Min(3, name.Length)], StringComparison.OrdinalIgnoreCase))
        ?.Name;

    private static bool Bindable(ISymbol member) =>
        member.Kind is SymbolKind.Property or SymbolKind.Field && !member.IsImplicitlyDeclared;

    public static ITypeSymbol? TypeOf(ISymbol member) => member switch
    {
        IPropertySymbol property => property.Type,
        IFieldSymbol field => field.Type,
        _ => null,
    };

    private static string? Declared(XElement element) =>
        element.Attributes().FirstOrDefault(IsDataType)?.Value is { Length: > 0 } dataType
            ? dataType
            : DesignInstance(element);

    private static bool IsDataType(XAttribute attribute) =>
        attribute.Name.LocalName.Equals("DataType", StringComparison.Ordinal) && !attribute.IsNamespaceDeclaration;

    private static string? DesignInstance(XElement element)
    {
        var attribute = element
            .Attributes()
            .FirstOrDefault(candidate => candidate.Name.LocalName.Equals("DataContext", StringComparison.Ordinal));

        var match = attribute is null ? Match.Empty : DesignInstanceExpression().Match(attribute.Value);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<INamedTypeSymbol?> FindTypeAsync(
        LoadedWorkspace workspace,
        string metadataName,
        string? simpleName,
        CancellationToken cancellationToken)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var found = compilation?.GetTypeByMetadataName(metadataName) ?? BySimpleName(compilation, simpleName);

            if (found is not null)
                return found;
        }

        return null;
    }

    private static INamedTypeSymbol? BySimpleName(Compilation? compilation, string? simpleName) => simpleName is null
        ? null
        : Single(compilation?
            .GetSymbolsWithName(name => name.Equals(simpleName, StringComparison.Ordinal), SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Take(2)
            .ToArray());

    private static INamedTypeSymbol? Single(INamedTypeSymbol[]? found) =>
        found is { Length: 1 } ? found[0] : null;

    private static string Inside(string expression)
    {
        var trimmed = expression.Trim('{', '}').Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
    }

    [GeneratedRegex(@"\{(?:x:)?(?:Compiled)?Binding[^}]*\}|\{x:Bind[^}]*\}")]
    private static partial Regex BindingExpression();

    [GeneratedRegex(@"\bPath\s*=\s*([^,}]+)")]
    private static partial Regex PathAssignment();

    [GeneratedRegex(@"\{d:DesignInstance\s+(?:Type\s*=\s*)?([^,}\s]+)")]
    private static partial Regex DesignInstanceExpression();

    public static string? ExpressionIn(string value)
    {
        var match = BindingExpression().Match(value);

        return match.Success ? match.Value : null;
    }
}
