using System.Diagnostics.CodeAnalysis;
using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Core;

public sealed record XamlElementInfo(XElement Element, string Path, int Line)
{
    public string TypeName => Element.Name.LocalName;

    public string? Name => Attribute("Name");

    public string? Key => Attribute("Key");

    public string? Uid => Attribute("Uid");

    public string? Attribute(string localName) => Element
        .Attributes()
        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(localName, StringComparison.Ordinal))
        ?.Value;
}

public sealed record XamlDocument(string Path, XDocument Document, string Dialect)
{
    private const string AvaloniaNamespace = "github.com/avaloniaui";
    private const string MauiNamespace = "/dotnet/2021/maui";
    private const string WinUiNamespace = "microsoft.ui.xaml";
    private const string WinUiPrefixForm = "using:";
    private const string ClrPrefixForm = "clr-namespace:";

    private static readonly string[] Extensions = [".xaml", ".axaml", ".paml"];

    public static bool IsXaml(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous XAML index leaf, called from the parsed-document cache every xaml_* tool shares; converting it means an async index, not a local change.")]
    public static Result<XamlDocument> Load(string fullPath)
    {
        if (!File.Exists(fullPath))
            return Result.Fail<XamlDocument>(Errors.DocumentNotFound(fullPath));

        try
        {
            var document = XDocument.Load(fullPath, LoadOptions.SetLineInfo);

            return Result.Ok(new XamlDocument(fullPath, document, DetectDialect(document, fullPath)));
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

    public string? ClrNamespaceOf(string prefix)
    {
        var declaration = Document.Root?
            .Attributes()
            .FirstOrDefault(attribute => attribute.IsNamespaceDeclaration
                && attribute.Name.LocalName.Equals(prefix, StringComparison.Ordinal));

        return declaration is null ? null : ClrNamespace(declaration.Value);
    }

    public static string? ClrNamespace(string declaration)
    {
        var body = Body(declaration);
        var separator = body?.IndexOf(';', StringComparison.Ordinal) ?? -1;

        return separator < 0 ? body : body![..separator];
    }

    private static string? Body(string declaration) => declaration switch
    {
        var value when value.StartsWith(ClrPrefixForm, StringComparison.Ordinal) => value[ClrPrefixForm.Length..],
        var value when value.StartsWith(WinUiPrefixForm, StringComparison.Ordinal) => value[WinUiPrefixForm.Length..],
        _ => null,
    };

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

    private static string DetectDialect(XDocument document, string path) => RootMarkup(document) switch
    {
        var markup when markup.Contains(AvaloniaNamespace, StringComparison.OrdinalIgnoreCase) => "avalonia",
        var markup when markup.Contains(MauiNamespace, StringComparison.OrdinalIgnoreCase) => "maui",
        var markup when markup.Contains(WinUiNamespace, StringComparison.OrdinalIgnoreCase) => "winui",
        var markup when markup.Contains(WinUiPrefixForm, StringComparison.OrdinalIgnoreCase) => "winui",
        _ => ByExtension(path),
    };

    private static string ByExtension(string path) =>
        System.IO.Path.GetExtension(path) is ".axaml" or ".paml" ? "avalonia" : "wpf";

    private static string RootMarkup(XDocument document) => string.Join(
        " ",
        document.Root?.Attributes().Select(attribute => attribute.Value) ?? []);
}
