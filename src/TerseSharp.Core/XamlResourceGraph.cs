namespace TerseSharp.Core;

public sealed record XamlResourceDeclaration(string File, int Line, string Key, string TypeName, string Scope);

public sealed record XamlIndexedFile(string Relative, XamlDocument? Document, string? Failure);

public sealed class XamlResourceGraph
{
    private readonly Dictionary<string, List<XamlResourceDeclaration>> declarations = new(StringComparer.Ordinal);
    private readonly List<XamlIndexedFile> files = [];

    private XamlResourceGraph()
    {
    }

    public IReadOnlyList<XamlIndexedFile> Files => files;

    public int FileCount => files.Count;

    public int SkippedCount => files.Count(file => file.Failure is not null);

    public static XamlResourceGraph Build(string root)
    {
        var graph = new XamlResourceGraph();

        foreach (var file in XamlFiles.Enumerate(root))
            graph.Index(file, root);

        return graph;
    }

    public bool Declares(string key) => declarations.ContainsKey(key);

    public IReadOnlyList<XamlResourceDeclaration> Of(string key) =>
        declarations.TryGetValue(key, out var found) ? found : [];

    private void Index(string file, string root)
    {
        var loaded = XamlDocument.Load(file);
        var relative = PositionFormat.Relative(root, file);

        files.Add(new XamlIndexedFile(relative, loaded.Value, loaded.IsOk ? null : loaded.Error!.Message));

        if (!loaded.IsOk)
            return;

        var scope = ScopeOf(relative);

        foreach (var element in loaded.Value!.Elements().Where(element => element.Key is not null))
            Add(new XamlResourceDeclaration(relative, element.Line, element.Key!, element.TypeName, scope));
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
}
