using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TerseSharp.Core;

public sealed record XamlResourceDeclaration(string File, int Line, string Key, string TypeName, string Scope);

public sealed record XamlElementFact(int Line, string TypeName, string? Key, string? Name);

public sealed record XamlResourceReference(int Line, string Key);

public sealed record XamlUidSite(string File, int Line, string Uid, string TypeName);

public sealed record XamlFileRecord(
    string FullPath,
    string Relative,
    FileStamp Stamp,
    string? Failure,
    string Dialect,
    IReadOnlyList<XamlElementFact> Elements,
    IReadOnlyList<XamlResourceReference> References,
    IReadOnlyList<XamlStyle> Styles,
    IReadOnlyList<XamlUidSite> Uids);

public sealed partial class XamlResourceGraph
{
    private readonly Dictionary<string, List<XamlResourceDeclaration>> declarations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XamlFileRecord> byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ParsedDocumentCache documents;

    private XamlResourceGraph(ParsedDocumentCache documents, IReadOnlyList<XamlFileRecord> files)
    {
        this.documents = documents;
        Files = files;
        Styles = [.. files.SelectMany(file => file.Styles)];

        foreach (var file in files)
            Index(file);
    }

    public IReadOnlyList<XamlFileRecord> Files { get; }

    public IReadOnlyList<XamlStyle> Styles { get; }

    public int FileCount => Files.Count;

    public int SkippedCount => Files.Count(file => file.Failure is not null);

    public IEnumerable<XamlUidSite> Uids => Files.SelectMany(file => file.Uids);

    public static XamlResourceGraph Build(
        string root,
        ParsedDocumentCache documents,
        XamlResourceGraph? previous)
    {
        var records = new List<XamlFileRecord>();

        foreach (var file in XamlFiles.Enumerate(root))
            records.Add(Reuse(file, root, documents, previous));

        return new XamlResourceGraph(documents, records);
    }

    public static XamlFileRecord Scan(string fullPath, string root, ParsedDocumentCache documents) =>
        Parse(fullPath, root, FileStamp.Of(fullPath), documents);

    public bool Declares(string key) => declarations.ContainsKey(key);

    public IReadOnlyList<XamlResourceDeclaration> Of(string key) =>
        declarations.TryGetValue(key, out var found) ? found : [];

    public XamlFileRecord? Record(string fullPath) =>
        byPath.TryGetValue(fullPath, out var found) ? found : null;

    public XamlDocument? Document(XamlFileRecord file) =>
        documents.Load(file.FullPath, file.Stamp, ParsedDocumentCache.MarkupFactor, XamlDocument.Load) is
        { IsOk: true, Value: { } loaded }
            ? loaded
            : null;

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource|ThemeResource)\s+([^}\s]+)\s*\}")]
    internal static partial Regex ResourceReference();

    private static XamlFileRecord Reuse(
        string file,
        string root,
        ParsedDocumentCache documents,
        XamlResourceGraph? previous)
    {
        var stamp = FileStamp.Of(file);

        return previous?.Record(file) is { } known && known.Stamp == stamp
            ? known
            : Parse(file, root, stamp, documents);
    }

    private static XamlFileRecord Parse(string file, string root, FileStamp stamp, ParsedDocumentCache documents)
    {
        var relative = PositionFormat.Relative(root, file);
        var loaded = documents.Load(file, stamp, ParsedDocumentCache.MarkupFactor, XamlDocument.Load);

        return loaded is { IsOk: true, Value: { } document }
            ? Facts(file, relative, stamp, document)
            : new XamlFileRecord(file, relative, stamp, loaded.Error!.Message, "unknown", [], [], [], []);
    }

    private static XamlFileRecord Facts(string file, string relative, FileStamp stamp, XamlDocument document)
    {
        var found = new Collected([], [], [], []);

        foreach (var element in document.Elements())
            Collect(relative, element, found);

        return new XamlFileRecord(
            file,
            relative,
            stamp,
            null,
            document.Dialect,
            found.Elements,
            found.References,
            found.Styles,
            found.Uids);
    }

    private static void Collect(string relative, XamlElementInfo element, Collected found)
    {
        found.Elements.Add(new XamlElementFact(element.Line, element.TypeName, element.Key, element.Name));

        if (element.Uid is { } uid)
            found.Uids.Add(new XamlUidSite(relative, element.Line, uid, element.TypeName));

        if (XamlStyleGraph.Of(relative, element) is { } style)
            found.Styles.Add(style);

        foreach (var attribute in element.Element.Attributes())
            Reference(attribute, found);
    }

    private static void Reference(XAttribute attribute, Collected found)
    {
        var match = ResourceReference().Match(attribute.Value);

        if (match.Success)
            found.References.Add(new XamlResourceReference(XamlDocument.Line(attribute), match.Groups[1].Value));
    }

    private void Index(XamlFileRecord file)
    {
        byPath[file.FullPath] = file;

        var scope = ScopeOf(file.Relative);

        foreach (var fact in file.Elements.Where(fact => fact.Key is not null))
            Add(new XamlResourceDeclaration(file.Relative, fact.Line, fact.Key!, fact.TypeName, scope));
    }

    private void Add(XamlResourceDeclaration declaration)
    {
        if (!declarations.TryGetValue(declaration.Key, out var found))
            declarations[declaration.Key] = found = [];

        found.Add(declaration);
    }

    private static string ScopeOf(string relative) => Path.GetFileNameWithoutExtension(relative) switch
    {
        "App" or "Application" => "app",
        "Generic" => "theme",
        _ => Segments(relative).Any(segment => segment.Equals("Themes", StringComparison.OrdinalIgnoreCase))
            ? "theme"
            : "local",
    };

    private static string[] Segments(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record Collected(
        List<XamlElementFact> Elements,
        List<XamlResourceReference> References,
        List<XamlStyle> Styles,
        List<XamlUidSite> Uids);
}
