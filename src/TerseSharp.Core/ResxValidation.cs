using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public readonly record struct ResxFinding(string Code, string Relative, string Key, string Kind, string Detail);

public static class ResxValidation
{
    private static readonly string[] Default = ["RESX001", "RESX002", "RESX004", "RESX005", "RESX006", "RESX007", "RESX009"];

    private static readonly Regex Placeholder = new(
        @"\{(\d+)(?:[,:][^}]*)?\}",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    public static Result<string> Validate(
        LoadedWorkspace workspace,
        string? path,
        string? rules,
        bool includeUnused,
        int maxResults)
    {
        var index = workspace.Indexes.Resx();
        var families = Scope(index, workspace, path);

        if (!families.IsOk)
        {
            return Result.Fail<string>(families.Error!);
        }

        var selected = Selected(rules, includeUnused);
        var unused = includeUnused || selected.Contains("RESX003", StringComparer.OrdinalIgnoreCase);
        var checks = new Checks(selected, unused ? ResxUsageService.Tokens(workspace.Root) : [], unused);

        var checkedFamilies = families.Value!.Where(family => family.Kind is not ResxKind.WinForms).ToArray();

        var findings = checkedFamilies
            .SelectMany(family => Findings(index, family, checks))
            .Where(finding => selected.Contains(finding.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return Result.Ok(Render(
            findings,
            path is { Length: > 0 } ? path : "solution",
            maxResults,
            new ResxScope(checkedFamilies.Length, selected)));
    }

    private static Result<IReadOnlyList<ResxFamily>> Scope(ResxIndex index, LoadedWorkspace workspace, string? path)
    {
        if (path is not { Length: > 0 })
            return Result.Ok<IReadOnlyList<ResxFamily>>(index.Families);

        var located = ResxTarget.Locate(workspace, path);

        return located.IsOk
            ? Result.Ok<IReadOnlyList<ResxFamily>>([located.Value!.Family])
            : Result.Fail<IReadOnlyList<ResxFamily>>(located.Error!);
    }

    private static IReadOnlyList<string> Selected(string? rules, bool includeUnused) => rules is { Length: > 0 }
        ? [.. rules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
        : [.. Default, .. includeUnused ? new[] { "RESX003" } : []];

    private static IEnumerable<ResxFinding> Findings(ResxIndex index, ResxFamily family, Checks checks)
    {
        var neutral = index.Read(family.Neutral.Path);

        if (neutral is not { IsOk: true, Value: { } document })
        {
            return [];
        }

        return
        [
            .. Duplicates(index, family),
            .. Whitespace(index, family),
            .. Cultures(index, family, document),
            .. Designer(family, document),
            .. Unsorted(index, family, checks.Selected),
            .. checks.IncludeUnused ? Unused(checks.Tokens, family, document) : [],
        ];
    }

    private static IEnumerable<ResxFinding> Duplicates(ResxIndex index, ResxFamily family) => family
        .Files
        .SelectMany(file => index
            .Entries(file)
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new ResxFinding(
                "RESX004",
                file.Relative,
                group.Key,
                "DUPLICATE",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"declared at lines {string.Join(", ", group.Select(entry => entry.Line))} - GenerateResource fails on a duplicate name"))));

    private static IEnumerable<ResxFinding> Whitespace(ResxIndex index, ResxFamily family) => family
        .Files
        .SelectMany(file => index
            .Translatable(file)
            .Where(entry => !entry.Preserved && Trimmable(entry.Value))
            .Select(entry => new ResxFinding(
                "RESX007",
                file.Relative,
                entry.Name,
                "WHITESPACE",
                "leading or trailing whitespace without xml:space=\"preserve\" is trimmed at runtime")));

    private static bool Trimmable(string value) =>
        value.Length > 0 && !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static IEnumerable<ResxFinding> Cultures(ResxIndex index, ResxFamily family, ResxDocument neutral) => family
        .Cultures
        .SelectMany(file => Culture(index, file, neutral));

    private static IEnumerable<ResxFinding> Culture(ResxIndex index, ResxFile file, ResxDocument neutral)
    {
        var entries = index.Translatable(file);
        var byName = entries.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

        foreach (var source in neutral.Translatable)
        {
            if (!byName.TryGetValue(source.Name, out var translated))
                yield return Missing(file, source);
            else if (Compared(source.Value, translated.Value) is { } detail)
                yield return new ResxFinding("RESX002", file.Relative, source.Name, "PLACEHOLDER", detail);
            else if (translated.Value.Length is 0)
                yield return new ResxFinding("RESX006", file.Relative, source.Name, "EMPTY", "the localized value is empty");
        }

        foreach (var orphan in entries.Where(entry => neutral.Find(entry.Name) is null))
            yield return new ResxFinding("RESX005", file.Relative, orphan.Name, "ORPHAN", "not declared in the neutral file, so it is never loaded");
    }

    private static ResxFinding Missing(ResxFile file, ResxEntry source) => new(
        "RESX001",
        file.Relative,
        source.Name,
        "MISSING",
        string.Create(CultureInfo.InvariantCulture, $"no {file.Culture} value; neutral=\"{Shorten(source.Value)}\""));

    private static string? Compared(string neutral, string translated)
    {
        var expected = Indices(neutral);
        var actual = Indices(translated);
        var extra = actual.Except(expected).Order().ToArray();
        var absent = expected.Except(actual).Order().ToArray();

        return (extra, absent) switch
        {
            ([], []) => null,
            ([], _) => string.Create(CultureInfo.InvariantCulture, $"neutral has {Braces(expected)}, this culture has {Braces(actual)} - {Braces(absent)} is never filled in"),
            _ => string.Create(CultureInfo.InvariantCulture, $"this culture has {Braces(extra)} which the neutral value does not - string.Format throws FormatException"),
        };
    }

    private static IReadOnlyList<int> Indices(string value) =>
    [
        .. Placeholder
            .Matches(value)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct(),
    ];

    private static string Braces(IEnumerable<int> indices) =>
        string.Join(",", indices.Select(index => string.Create(CultureInfo.InvariantCulture, $"{{{index}}}")));

    private static IEnumerable<ResxFinding> Designer(ResxFamily family, ResxDocument neutral)
    {
        if (family.Designer is null)
        {
            return [];
        }

        var text = DesignerText(family);

        return text is null
            ? []
            : neutral
                .Translatable
                .Select(entry => entry.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(name => IsIdentifier(name) && !text.Contains(name, StringComparison.Ordinal))
                .Select(name => new ResxFinding(
                    "RESX009",
                    family.Designer,
                    name,
                    "DESIGNER",
                    "the generated designer does not expose this key - regenerate it before referencing the key from C#"));
    }

    private static string? DesignerText(ResxFamily family)
    {
        var path = Path.Combine(Path.GetDirectoryName(family.Neutral.Path) ?? string.Empty, Path.GetFileName(family.Designer!));

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] is '_') && name.All(character => char.IsLetterOrDigit(character) || character is '_');

    private static IEnumerable<ResxFinding> Unsorted(ResxIndex index, ResxFamily family, IReadOnlyList<string> selected)
    {
        if (!selected.Contains("RESX008", StringComparer.OrdinalIgnoreCase))
            return [];

        return family
            .Files
            .Where(file => index.Read(file.Path) is { IsOk: true, Value.IsSorted: false })
            .Select(file => new ResxFinding("RESX008", file.Relative, "-", "UNSORTED", "the keys are not in ordinal order, which makes merges conflict"));
    }

    private static IEnumerable<ResxFinding> Unused(HashSet<string> tokens, ResxFamily family, ResxDocument neutral) => neutral
        .Translatable
        .Select(entry => entry.Name)
        .Distinct(StringComparer.Ordinal)
        .Where(name => !tokens.Contains(name))
        .Select(name => new ResxFinding(
            "RESX003",
            family.Neutral.Relative,
            name,
            "UNUSED",
            "no C#, XAML or Razor reference found - HEURISTIC, a key composed at runtime cannot be seen"));

    private static string Render(ResxFinding[] findings, string scope, int maxResults, ResxScope checkedScope)
    {
        var response = new ResponseBuilder("resx_validate", scope);

        response.Summary(ResultCap.Shown(findings.Length, maxResults), findings.Length, "findings", "rules=");
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"checked={checkedScope.Families} family(ies) rules={string.Join(',', checkedScope.Rules)}"));

        foreach (var finding in findings.Capped(maxResults))
        {
            response.Line(Describe(finding));
        }

        return response.ToString();
    }

    private static string Describe(ResxFinding finding) => string.Create(
        CultureInfo.InvariantCulture,
        $"{finding.Code}  {finding.Relative}  {finding.Key}  {finding.Kind}  {finding.Detail}");

    private static string Shorten(string value)
    {
        var single = value.ReplaceLineEndings(" ");

        return single.Length <= 40 ? single : single[..40] + "...";
    }

    private sealed record Checks(IReadOnlyList<string> Selected, HashSet<string> Tokens, bool IncludeUnused);

    private readonly record struct ResxScope(int Families, IReadOnlyList<string> Rules);
}
