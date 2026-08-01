namespace TerseSharp.Core;

public sealed record XamlStyle(string File, int Line, string? Key, string TargetType, string? BasedOn, string Scope);

public static class XamlStyleGraph
{
    public static XamlStyle? Of(string relative, XamlElementInfo element) =>
        IsStyle(element) && Target(element) is { Length: > 0 } target
            ? new XamlStyle(
                relative,
                element.Line,
                element.Key,
                Simple(target) ?? target,
                Simple(element.Attribute("BasedOn")),
                Scope(element))
            : null;

    public static Result<string> Render(XamlResourceGraph graph, string typeName, int maxResults)
    {
        var styles = graph.Styles;
        var applicable = styles.Where(style => Applies(style, typeName)).ToArray();
        var response = new ResponseBuilder("xaml_styles", typeName);

        response.Summary(Math.Min(applicable.Length, maxResults), applicable.Length, "styles", "maxResults=");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"scanned={graph.FileCount} files"));

        if (applicable.Length is 0)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"no Style targets '{typeName}' in any XAML file under the workspace root"));

        foreach (var style in applicable.OrderBy(style => style.Key is null ? 0 : 1).ThenBy(style => style.File, StringComparer.Ordinal).Take(maxResults))
            response.Line(Describe(style, styles));

        return Result.Ok(response.ToString());
    }

    private static string? Target(XamlElementInfo element) =>
        element.Attribute("TargetType") ?? element.Attribute("DataType");

    private static bool IsStyle(XamlElementInfo element) =>
        element.TypeName is "Style" or "ControlTemplate" or "DataTemplate";

    private static string Scope(XamlElementInfo element) => element.Key is null ? "implicit" : "keyed";

    private static bool Applies(XamlStyle style, string typeName) =>
        string.Equals(style.TargetType, Simple(typeName) ?? typeName, StringComparison.Ordinal);

    private static string Describe(XamlStyle style, IReadOnlyList<XamlStyle> all)
    {
        var chain = Chain(style, all);
        var key = style.Key is null ? "-" : style.Key;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{style.File}:{style.Line}  HEURISTIC  {style.Scope}  key={key}  targets={style.TargetType}{chain}");
    }

    private static string Chain(XamlStyle style, IReadOnlyList<XamlStyle> all)
    {
        var names = new List<string>();
        var current = style.BasedOn;
        var guard = 0;

        while (current is { Length: > 0 } && guard++ < 16 && !names.Contains(current, StringComparer.Ordinal))
        {
            names.Add(current);
            current = all.FirstOrDefault(candidate => string.Equals(candidate.Key, current, StringComparison.Ordinal))?.BasedOn;
        }

        return names.Count is 0 ? string.Empty : "  basedOn=" + string.Join(" -> ", names);
    }

    private static string? Simple(string? value)
    {
        if (value is not { Length: > 0 })
            return null;

        var inner = value.Trim('{', '}').Trim();
        var last = inner.Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? inner;
        var separator = last.LastIndexOfAny(['.', ':']);

        return separator < 0 ? last : last[(separator + 1)..];
    }
}
