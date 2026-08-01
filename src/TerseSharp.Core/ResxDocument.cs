using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Core;

public enum ResxEntryKind
{
    Text,
    Typed,
    Binary,
}

public sealed record ResxEntry(
    string Name,
    string Value,
    string? Comment,
    ResxEntryKind Kind,
    bool Preserved,
    int Line,
    int Start,
    int Length)
{
    public int End => Start + Length;

    public bool IsDesignerState => Name.StartsWith(">>", StringComparison.Ordinal) || Name.StartsWith('$');

    public bool IsTranslatable => Kind is ResxEntryKind.Text && !IsDesignerState;
}

public sealed class ResxDocument
{
    private ResxDocument(string path, string text, IReadOnlyList<ResxEntry> entries)
    {
        Path = path;
        Text = text;
        Entries = entries;
    }

    public string Path { get; }

    public string Text { get; }

    public IReadOnlyList<ResxEntry> Entries { get; }

    public string NewLine => Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    public bool HasDesignerState => Entries.Any(entry => entry.IsDesignerState);

    public bool IsSorted => Translatable
        .Select(entry => entry.Name)
        .SequenceEqual(Translatable.Select(entry => entry.Name).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    public IEnumerable<ResxEntry> Translatable => Entries.Where(entry => entry.IsTranslatable);

    public ResxEntry? Find(string name) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));

    public IReadOnlyList<ResxEntry> All(string name) =>
        [.. Entries.Where(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))];

    public static Result<ResxDocument> Parse(string path, string text)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(text, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            return Result.Fail<ResxDocument>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not well-formed XML: {exception.Message}"),
                "repair the file before addressing its keys"));
        }

        return Result.Ok(new ResxDocument(path, text, Read(text, document)));
    }

    public int InsertionPoint(string name)
    {
        var sorted = IsSorted
            ? Translatable.FirstOrDefault(entry => string.CompareOrdinal(entry.Name, name) > 0)
            : null;

        return sorted?.LineStart(Text) ?? EndOfEntries();
    }

    private int EndOfEntries()
    {
        var last = Entries.Count is 0 ? -1 : Entries[^1].End;
        var closing = Text.LastIndexOf("</root>", StringComparison.Ordinal);

        return last < 0 ? LineStartOf(Text, Math.Max(closing, 0)) : LineStartOf(Text, last) + LineLength(Text, last);
    }

    private static int LineLength(string text, int offset)
    {
        var end = text.IndexOf('\n', offset);

        return end < 0 ? text.Length - LineStartOf(text, offset) : end + 1 - LineStartOf(text, offset);
    }

    internal static int LineStartOf(string text, int offset) =>
        text.LastIndexOf('\n', Math.Max(Math.Min(offset, text.Length - 1), 0)) + 1;

    private static IReadOnlyList<ResxEntry> Read(string text, XDocument document)
    {
        if (document.Root is null)
            return [];

        var starts = LineStarts(text);

        return [.. document.Root
            .Elements()
            .Where(element => string.Equals(element.Name.LocalName, "data", StringComparison.Ordinal))
            .Select(element => Entry(text, starts, element))
            .OfType<ResxEntry>()];
    }

    private static ResxEntry? Entry(string text, int[] starts, XElement element)
    {
        if (element.Attribute("name")?.Value is not { Length: > 0 } name)
            return null;

        var start = Offset(starts, element);
        var length = Length(text, start, element.IsEmpty);

        return length is 0 ? null : new ResxEntry(
            name,
            Value(element),
            Child(element, "comment")?.Value,
            KindOf(element),
            element.Attribute(XNamespace.Xml + "space") is not null,
            ((IXmlLineInfo)element).LineNumber,
            start,
            length);
    }

    private static int Offset(int[] starts, XElement element)
    {
        var info = (IXmlLineInfo)element;
        var line = Math.Clamp(info.LineNumber - 1, 0, starts.Length - 1);

        return Math.Max(starts[line] + info.LinePosition - 2, 0);
    }

    private static int Length(string text, int start, bool empty)
    {
        var marker = empty ? "/>" : "</data>";
        var found = text.IndexOf(marker, start, StringComparison.Ordinal);

        return found < 0 ? 0 : found + marker.Length - start;
    }

    private static string Value(XElement element) => Child(element, "value")?.Value ?? element.Value;

    private static XElement? Child(XElement element, string name) => element
        .Elements()
        .FirstOrDefault(child => string.Equals(child.Name.LocalName, name, StringComparison.Ordinal));

    private static ResxEntryKind KindOf(XElement element) => element.Attribute("mimetype") switch
    {
        not null => ResxEntryKind.Binary,
        _ => element.Attribute("type") is null ? ResxEntryKind.Text : ResxEntryKind.Typed,
    };

    private static int[] LineStarts(string text)
    {
        var starts = new List<int>((text.Length / 40) + 1) { 0 };

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\n')
                starts.Add(index + 1);
        }

        return [.. starts];
    }
}

public static class ResxEntryText
{
    public static int LineStart(this ResxEntry entry, string text) => ResxDocument.LineStartOf(text, entry.Start);
}
