using System.Xml.Linq;

namespace TerseSharp.Core;

public sealed record XamlHandler(string Element, string Event, string Method, int Line);

public static class XamlCodeBehind
{
    public static string? ClassOf(XamlDocument document) => document
        .Document
        .Root?
        .Attributes()
        .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Class", StringComparison.Ordinal))
        ?.Value;

    public static IEnumerable<XamlHandler> Handlers(XamlDocument document)
    {
        foreach (var element in document.Elements())
        {
            foreach (var attribute in element.Element.Attributes().Where(IsHandler))
                yield return new XamlHandler(element.TypeName, attribute.Name.LocalName, attribute.Value, XamlDocument.Line(attribute));
        }
    }

    private static bool IsHandler(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration
        && attribute.Name.NamespaceName.Length is 0
        && attribute.Value.Length > 0
        && !attribute.Value.StartsWith('{')
        && IsIdentifier(attribute.Value)
        && LooksLikeEvent(attribute.Name.LocalName);

    private static bool LooksLikeEvent(string name) =>
        name.StartsWith("On", StringComparison.Ordinal)
        || name is "Click" or "Loaded" or "Unloaded" or "Checked" or "Unchecked" or "SelectionChanged"
            or "TextChanged" or "Tapped" or "Closing" or "Closed" or "GotFocus" or "LostFocus"
            or "MouseDown" or "MouseUp" or "KeyDown" or "KeyUp" or "Completed" or "ValueChanged";

    private static bool IsIdentifier(string value) =>
        char.IsLetter(value[0]) is true
        && value.All(character => char.IsLetterOrDigit(character) || character is '_');
}
