using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Core;

public sealed record XamlElementInfo(XElement Element, string Path, int Line)
{
    public string TypeName => Element.Name.LocalName;

    public string? Name => Attribute("Name");

    public string? Key => Attribute("Key");

    public string? Attribute(string localName) => Element
        .Attributes()
        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.Ordinal))
        ?.Value;
}

public sealed record XamlDocument(string Path, XDocument Document, string Dialect)
{
    private static readonly string[] Extensions = [".xaml", ".axaml", ".paml"];

    public static bool IsXaml(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static Result<XamlDocument> Load(string fullPath)
    {
        if (!File.Exists(fullPath))
            return Result.Fail<XamlDocument>(Errors.DocumentNotFound(fullPath));

        try
        {
            var document = XDocument.Load(fullPath, LoadOptions.SetLineInfo);

            return Result.Ok(new XamlDocument(fullPath, document, DetectDialect(document)));
        }
        catch (XmlException exception)
        {
            return Result.Fail<XamlDocument>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"{fullPath} is not well-formed XAML: {exception.Message}"),
                "fix the markup, then retry"));
        }
    }

    public IEnumerable<XamlElementInfo> Elements()
    {
        var counters = new Dictionary<XElement, int>();

        return Document.Root is null ? [] : Walk(Document.Root, string.Empty, counters);
    }

    private static IEnumerable<XamlElementInfo> Walk(XElement element, string parentPath, Dictionary<XElement, int> counters)
    {
        var path = parentPath.Length is 0 ? element.Name.LocalName : parentPath + "/" + Segment(element, counters);

        yield return new XamlElementInfo(element, path, Line(element));

        foreach (var child in element.Elements())
        {
            foreach (var descendant in Walk(child, path, counters))
                yield return descendant;
        }
    }

    private static string Segment(XElement element, Dictionary<XElement, int> counters)
    {
        var siblings = element.Parent?.Elements(element.Name).ToArray() ?? [];
        var index = Array.IndexOf(siblings, element);
        var name = element.Name.LocalName;

        counters[element] = index;

        return siblings.Length > 1 ? string.Create(CultureInfo.InvariantCulture, $"{name}[{index}]") : name;
    }

    public static int Line(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    private static string DetectDialect(XDocument document)
    {
        var namespaces = document.Root?.Attributes().Select(attribute => attribute.Value) ?? [];
        var joined = string.Join(" ", namespaces);

        if (joined.Contains("avaloniaui.net", StringComparison.OrdinalIgnoreCase))
            return "avalonia";

        if (joined.Contains("microsoft.ui.xaml", StringComparison.OrdinalIgnoreCase))
            return "winui";

        if (joined.Contains("dotnet/maui", StringComparison.OrdinalIgnoreCase))
            return "maui";

        return "wpf";
    }
}
