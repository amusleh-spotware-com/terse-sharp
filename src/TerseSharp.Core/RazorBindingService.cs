using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record RazorBindingSite(RazorElement Element, RazorAttributeInfo Attribute, string Kind);

public static class RazorBindingService
{
    public static async Task<Result<string>> BindingsAsync(
        LoadedWorkspace workspace,
        string path,
        bool validate,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var context = opened.Value!;
        var sites = Sites(context.Document).ToArray();
        var response = new ResponseBuilder("razor_bindings", context.Relative);

        response.Summary(sites.Length, sites.Length, "bindings");
        response.Note("context=" + (context.Self?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? "UNRESOLVED_CONTEXT"));

        foreach (var site in sites)
            response.Line(validate ? Check(context, site) : Describe(context, site, "HEURISTIC", string.Empty));

        return Result.Ok(response.ToString());
    }

    public static IEnumerable<RazorBindingSite> Sites(RazorDocument document) => document.Elements
        .SelectMany(element => element.Attributes.Select(attribute => new { element, attribute }))
        .Where(pair => KindOf(pair.attribute.Name) is not null)
        .Select(pair => new RazorBindingSite(pair.element, pair.attribute, KindOf(pair.attribute.Name)!));

    public static string? KindOf(string attributeName) => attributeName switch
    {
        "@ref" => "ref",
        ['@', 'b', 'i', 'n', 'd', ..] => "bind",
        ['@', 'o', 'n', ..] => "event",
        ['a', 's', 'p', '-', 'f', 'o', 'r'] => "model",
        _ => null,
    };

    private static string Check(RazorContext context, RazorBindingSite site)
    {
        if (context.Self is null)
            return Describe(context, site, "HEURISTIC", "UNRESOLVED_CONTEXT no component type for this file");

        var expression = site.Attribute.Expression.Trim();

        if (expression.Length is 0 || expression.Contains("=>", StringComparison.Ordinal))
            return Describe(context, site, "HEURISTIC", "INLINE not a member reference");

        return Resolve(context.Self, expression) is { } member
            ? Verdict(context, site, member)
            : Describe(context, site, "EXACT", "UNRESOLVED no member '" + expression + "' on the component");
    }

    private static string Verdict(RazorContext context, RazorBindingSite site, ISymbol member)
    {
        var settable = member is not IPropertySymbol property || property.SetMethod is not null;

        return site.Kind is "bind" && !settable
            ? Describe(context, site, "EXACT", "NO_SETTER " + member.Name + " cannot be written back")
            : Describe(context, site, "EXACT", member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static ISymbol? Resolve(INamedTypeSymbol type, string expression)
    {
        ISymbol? current = type;

        foreach (var range in expression.AsSpan().Split('.'))
        {
            var segment = Trim(expression.AsSpan()[range]);

            if (segment.IsEmpty)
                continue;

            current = Member(Owner(current), new string(segment));

            if (current is null)
                return null;
        }

        return current;
    }

    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> segment) =>
        (segment.IndexOf('(') is var open and >= 0 ? segment[..open] : segment).Trim();

    private static INamedTypeSymbol? Owner(ISymbol? symbol) => symbol switch
    {
        INamedTypeSymbol type => type,
        IPropertySymbol property => property.Type as INamedTypeSymbol,
        IFieldSymbol field => field.Type as INamedTypeSymbol,
        IMethodSymbol method => method.ReturnType as INamedTypeSymbol,
        _ => null,
    };

    private static ISymbol? Member(INamedTypeSymbol? type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).FirstOrDefault() is { } found)
                return found;
        }

        return null;
    }

    private static string Describe(RazorContext context, RazorBindingSite site, string confidence, string detail) => string.Create(
        CultureInfo.InvariantCulture,
        $"{context.Relative}:{site.Attribute.Line}  {confidence}  {site.Kind}  {site.Element.TagName} {site.Attribute.Name}=\"{site.Attribute.Value}\"  {detail}");
}
