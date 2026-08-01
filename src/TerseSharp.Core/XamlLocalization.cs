namespace TerseSharp.Core;

public sealed record ResourceEntry(string File, string Name, string Value);

public static class XamlLocalization
{
    public static Result<string> Render(LoadedWorkspace workspace, int maxResults)
    {
        var graph = workspace.Indexes.Xaml();
        var index = workspace.Indexes.Resx();
        var entries = Entries(index);
        var uids = graph.Uids.ToArray();
        var response = new ResponseBuilder("xaml_localization", "solution");

        response.Summary(Math.Min(maxResults, uids.Length), uids.Length, "x:Uid declarations", "maxResults=");
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"resourceFiles={index.Families.Sum(family => family.Files.Count())} entries={entries.Count}"));

        foreach (var uid in uids.Take(maxResults))
            response.Line(Describe(uid, entries));

        return Result.Ok(response.ToString());
    }

    private static string Describe(XamlUidSite uid, ILookup<string, ResourceEntry> entries)
    {
        var matches = entries[uid.Uid].Concat(entries.Where(group => group.Key.StartsWith(uid.Uid + ".", StringComparison.Ordinal)).SelectMany(group => group)).ToArray();

        var resolution = matches.Length is 0
            ? "UNRESOLVED no .resx or .resw entry is named for this uid"
            : string.Create(CultureInfo.InvariantCulture, $"{matches.Length} entry(s): {string.Join(", ", matches.Take(4).Select(entry => entry.File + "#" + entry.Name))}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{uid.File}:{uid.Line}  {(matches.Length is 0 ? "HEURISTIC" : "EXACT")}  {uid.TypeName}  uid={uid.Uid}  {resolution}");
    }

    private static ILookup<string, ResourceEntry> Entries(ResxIndex index) => index
        .Families
        .SelectMany(family => family.Files)
        .SelectMany(file => index.Entries(file).Select(entry => new ResourceEntry(file.Relative, entry.Name, entry.Value)))
        .ToLookup(entry => entry.Name, StringComparer.Ordinal);
}
