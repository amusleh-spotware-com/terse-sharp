namespace TerseSharp.Core;

public static class ResxService
{
    private const int ValueWidth = 80;

    public static Result<string> Files(LoadedWorkspace workspace, string? filter, int maxResults)
    {
        var index = workspace.Indexes.Resx();
        var families = index.Families;
        var matched = filter is { Length: > 0 }
            ? families.Where(family => family.Relative.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray()
            : [.. families];

        var response = new ResponseBuilder("resx_files", "solution");

        response.Summary(ResultCap.Shown(matched.Length, maxResults), matched.Length, "families", "filter=");

        foreach (var family in matched.Capped(maxResults))
            response.Line(Describe(index, family));

        return Result.Ok(response.ToString());
    }

    public static Result<string> Get(
        LoadedWorkspace workspace,
        string path,
        string cultures,
        string? prefix,
        string? key,
        bool values,
        int maxResults)
    {
        var located = ResxTarget.Locate(workspace, path);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var family = located.Value!.Family;
        var selected = Selected(family, cultures);

        return selected.Count is 0
            ? Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"no file in the family matches cultures='{cultures}'"),
                "pass neutral, all, or a comma-separated list of the cultures resx_files printed"))
            : Result.Ok(Rendered(located.Value!.Index, family, selected, new Selection(prefix, key, values), maxResults));
    }

    public static Result<string> Find(
        LoadedWorkspace workspace,
        string query,
        string scope,
        string? culture,
        int maxResults)
    {
        var hits = Hits(workspace.Indexes.Resx(), query, scope, culture).ToArray();
        var response = new ResponseBuilder("resx_find", query);

        response.Summary(ResultCap.Shown(hits.Length, maxResults), hits.Length, "entries", "culture=");

        foreach (var hit in hits.Capped(maxResults))
            response.Line(hit);

        return Result.Ok(response.ToString());
    }

    private static IEnumerable<string> Hits(ResxIndex index, string query, string scope, string? culture)
    {
        foreach (var file in index.Families.SelectMany(family => family.Files).Where(file => InCulture(file, culture)))
        {
            foreach (var entry in index.Entries(file).Where(entry => Matches(entry, query, scope)))
                yield return Hit(file, entry, scope);
        }
    }

    private static bool InCulture(ResxFile file, string? culture) => culture switch
    {
        null or "" => true,
        "neutral" => file.Culture is null,
        _ => string.Equals(file.Culture, culture, StringComparison.OrdinalIgnoreCase),
    };

    private static bool Matches(ResxEntry entry, string query, string scope) => scope switch
    {
        "value" => Contains(entry.Value, query),
        "comment" => Contains(entry.Comment, query),
        "all" => Contains(entry.Name, query) || Contains(entry.Value, query) || Contains(entry.Comment, query),
        _ => Contains(entry.Name, query),
    };

    private static bool Contains(string? text, string query) =>
        text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Hit(ResxFile file, ResxEntry entry, string scope) => string.Create(
        CultureInfo.InvariantCulture,
        $"{file.Relative}#{entry.Name}:{entry.Line}  {ConfidenceTag.Of(Confidence.Exact)}  {Field(entry, scope)}");

    private static string Field(ResxEntry entry, string scope) => scope switch
    {
        "comment" => string.Create(CultureInfo.InvariantCulture, $"comment=\"{Shorten(entry.Comment ?? string.Empty)}\""),
        _ => string.Create(CultureInfo.InvariantCulture, $"value=\"{Shorten(entry.Value)}\""),
    };

    private static string Rendered(
        ResxIndex index,
        ResxFamily family,
        IReadOnlyList<ResxFile> selected,
        Selection selection,
        int maxResults)
    {
        var keys = Keys(index, family, selection);
        var response = new ResponseBuilder("resx_get", family.Relative);

        response.Summary(ResultCap.Shown(keys.Count, maxResults), keys.Count, "keys", "prefix=");
        response.Note(Header(family, selected));

        foreach (var name in keys.Capped(maxResults))
            response.Line(Row(index, name, selected, selection.Values));

        return response.ToString();
    }

    private static IReadOnlyList<string> Keys(ResxIndex index, ResxFamily family, Selection selection) =>
    [
        .. index
            .Entries(family.Neutral)
            .Select(entry => entry.Name)
            .Where(name => selection.Key is not { Length: > 0 } || string.Equals(name, selection.Key, StringComparison.Ordinal))
            .Where(name => selection.Prefix is not { Length: > 0 } || name.StartsWith(selection.Prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal),
    ];

    private static string Header(ResxFamily family, IReadOnlyList<ResxFile> selected) => string.Create(
        CultureInfo.InvariantCulture,
        $"cultures={string.Join(",", selected.Select(file => file.Culture ?? "neutral"))}  designer={family.Designer ?? "-"}  kind={Kind(family)}");

    private static string Row(ResxIndex index, string name, IReadOnlyList<ResxFile> selected, bool values)
    {
        var entries = selected.Select(file => (File: file, Entry: index.Entries(file).FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal)))).ToArray();
        var kind = entries[0].Entry?.Kind ?? ResxEntryKind.Text;
        var columns = values ? entries.Select(pair => Column(pair.File, pair.Entry)) : [];

        return string.Join("  ", new[] { name, Tag(kind) }.Concat(columns));
    }

    private static string Tag(ResxEntryKind kind) => kind switch
    {
        ResxEntryKind.Binary => "BINARY",
        ResxEntryKind.Typed => "TYPED",
        _ => ConfidenceTag.Of(Confidence.Exact),
    };

    private static string Column(ResxFile file, ResxEntry? entry) => entry is null
        ? string.Create(CultureInfo.InvariantCulture, $"{file.Culture ?? "neutral"}=MISSING")
        : string.Create(CultureInfo.InvariantCulture, $"{file.Culture ?? "neutral"}=\"{Shorten(entry.Value)}\"");

    private static IReadOnlyList<ResxFile> Selected(ResxFamily family, string cultures) => cultures switch
    {
        "all" => [.. family.Files],
        null or "" or "neutral" => [family.Neutral],
        _ => [.. family.Files.Where(file => cultures
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(wanted => string.Equals(file.Culture ?? "neutral", wanted, StringComparison.OrdinalIgnoreCase)))],
    };

    private static string Describe(ResxIndex index, ResxFamily family)
    {
        var neutral = Names(index, family.Neutral).Distinct(StringComparer.Ordinal).Count();
        var cultures = family.Cultures.Count is 0
            ? "-"
            : string.Join(" ", family.Cultures.Select(file => string.Create(CultureInfo.InvariantCulture, $"{file.Culture}={Names(index, file).Distinct(StringComparer.Ordinal).Count()}")));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{family.Relative}  {Kind(family)}  neutral={neutral}  {cultures}  missing={Missing(index, family)}  designer={family.Designer ?? "-"}  project={Project(family)}");
    }

    private static string Kind(ResxFamily family) => family.Kind switch
    {
        ResxKind.WinForms => "winforms",
        ResxKind.Resw => "resw",
        _ => "localization",
    };

    public static int Missing(ResxIndex index, ResxFamily family)
    {
        if (family.Kind is ResxKind.WinForms)
        {
            return 0;
        }

        var neutral = Names(index, family.Neutral).ToHashSet(StringComparer.Ordinal);

        return family.Cultures.Sum(file => neutral.Except(Names(index, file), StringComparer.Ordinal).Count());
    }

    private static IEnumerable<string> Names(ResxIndex index, ResxFile file) => index.TranslatableNames(file);

    private static string Shorten(string value)
    {
        var single = value.ReplaceLineEndings(" ");

        return single.Length <= ValueWidth ? single : single[..ValueWidth] + "...";
    }

    private static string Project(ResxFamily family) =>
        ResxProject.Nearest(Path.GetDirectoryName(family.Neutral.Path)) is { } project
            ? Path.GetFileNameWithoutExtension(project)
            : "-";

    private readonly record struct Selection(string? Prefix, string? Key, bool Values);
}
