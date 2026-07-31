using System.Xml.Linq;

namespace TerseSharp.Core;

public sealed record ResourceEntry(string File, string Name, string Value);

public static class XamlLocalization
{
    private static readonly string[] Extensions = [".resx", ".resw"];

    public static Result<string> Render(LoadedWorkspace workspace, int maxResults)
    {
        var graph = XamlResourceGraph.Build(workspace.Root);
        var entries = Entries(workspace.Root);
        var uids = Uids(graph).ToArray();
        var response = new ResponseBuilder("xaml_localization", "solution");

        response.Summary(Math.Min(maxResults, uids.Length), uids.Length, "x:Uid declarations", "maxResults=");
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"resourceFiles={entries.Files} entries={entries.ByName.Count}"));

        foreach (var uid in uids.Take(maxResults))
            response.Line(Describe(uid, entries.ByName));

        return Result.Ok(response.ToString());
    }

    private static IEnumerable<UidSite> Uids(XamlResourceGraph graph)
    {
        foreach (var file in graph.Files.Where(file => file.Document is not null))
        {
            foreach (var element in file.Document!.Elements().Where(element => element.Uid is not null))
                yield return new UidSite(file.Relative, element.Line, element.Uid!, element.TypeName);
        }
    }

    private static string Describe(UidSite uid, ILookup<string, ResourceEntry> entries)
    {
        var matches = entries[uid.Uid].Concat(entries.Where(group => group.Key.StartsWith(uid.Uid + ".", StringComparison.Ordinal)).SelectMany(group => group)).ToArray();

        var resolution = matches.Length is 0
            ? "UNRESOLVED no .resx or .resw entry is named for this uid"
            : string.Create(CultureInfo.InvariantCulture, $"{matches.Length} entry(s): {string.Join(", ", matches.Take(4).Select(entry => entry.File + "#" + entry.Name))}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{uid.File}:{uid.Line}  {(matches.Length is 0 ? "HEURISTIC" : "EXACT")}  {uid.Element}  uid={uid.Uid}  {resolution}");
    }

    private static ResourceIndex Entries(string root)
    {
        var found = new List<ResourceEntry>();
        var files = 0;

        foreach (var file in Files(root))
        {
            files++;
            found.AddRange(Read(file, root));
        }

        return new ResourceIndex(files, found.ToLookup(entry => entry.Name, StringComparer.Ordinal));
    }

    private static IEnumerable<string> Files(string root)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, "*.res*", SearchOption.AllDirectories)
                .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Where(file => !XamlFiles.IsExcluded(file, root));
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<ResourceEntry> Read(string file, string root)
    {
        XDocument document;

        try
        {
            document = XDocument.Load(file);
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }

        var relative = PositionFormat.Relative(root, file);

        foreach (var data in document.Descendants("data"))
        {
            if (data.Attribute("name")?.Value is { Length: > 0 } name)
                yield return new ResourceEntry(relative, name, data.Element("value")?.Value ?? string.Empty);
        }
    }

    private readonly record struct UidSite(string File, int Line, string Uid, string Element);

    private readonly record struct ResourceIndex(int Files, ILookup<string, ResourceEntry> ByName);
}
