namespace TerseSharp.Core;

public readonly record struct DocumentSection(string Heading, int Level, int StartLine, int EndLine)
{
    public string Title => Heading.TrimStart('#').Trim();
}

public static class DocumentOutline
{
    public static bool IsMarkdown(ReadOnlySpan<char> path) => Path.GetExtension(path) switch
    {
        ".md" or ".markdown" or ".mdx" => true,
        _ => false,
    };

    public static IReadOnlyList<DocumentSection> Headings(string text)
    {
        var found = new List<DocumentSection>();
        var open = new List<int>();
        var fenced = false;
        var number = 0;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            number++;

            if (IsFence(line))
                fenced = !fenced;
            else if (!fenced && Level(line) is var level and > 0)
                Begin(found, open, new DocumentSection(new string(line.TrimEnd()), level, number, int.MaxValue));
        }

        return Seal(found, number);
    }

    public static Result<DocumentSection> Locate(IReadOnlyList<DocumentSection> sections, string heading, int occurrence = 0)
    {
        var wanted = heading.TrimStart('#').Trim();
        var matches = sections.Where(section => Matches(section, wanted, heading)).ToArray();

        if (matches.Length is 0)
        {
            return Result.Fail<DocumentSection>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"no section titled '{heading}'"),
                Nearest(sections)));
        }

        if (occurrence > 0)
        {
            return occurrence <= matches.Length
                ? Result.Ok(matches[occurrence - 1])
                : Result.Fail<DocumentSection>(Errors.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"occurrence={occurrence} does not exist: '{heading}' names {matches.Length} sections"),
                    string.Create(CultureInfo.InvariantCulture, $"pass an occurrence between 1 and {matches.Length}")));
        }

        return matches is [var only]
            ? Result.Ok(only)
            : Result.Fail<DocumentSection>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{heading}' names {matches.Length} sections"),
                string.Create(CultureInfo.InvariantCulture, $"pass occurrence=1..{matches.Length} to pick one, or a heading that occurs once; they start at {Starts(matches)}")));
    }

    private const int MaxListedSections = 12;

    private static string Starts(DocumentSection[] matches)
    {
        var listed = string.Join(
            ", ",
            matches.Take(MaxListedSections).Select((section, index) => string.Create(CultureInfo.InvariantCulture, $"{index + 1}:line {section.StartLine}")));

        return matches.Length <= MaxListedSections
            ? listed
            : listed + string.Create(CultureInfo.InvariantCulture, $", +{matches.Length - MaxListedSections} more");
    }

    private static bool Matches(DocumentSection section, string wanted, string heading) =>
        section.Title.Equals(wanted, StringComparison.OrdinalIgnoreCase)
        && (heading.StartsWith('#') is false || section.Heading.StartsWith(heading.TrimEnd(), StringComparison.Ordinal));

    private static string Nearest(IReadOnlyList<DocumentSection> sections) => sections.Count is 0
        ? "the file has no markdown headings; use startLine/endLine"
        : "headings: " + string.Join(" | ", sections.Take(12).Select(section => section.Heading));

    private static void Begin(List<DocumentSection> found, List<int> open, DocumentSection opened)
    {
        while (open.Count > 0 && found[open[^1]].Level >= opened.Level)
        {
            found[open[^1]] = found[open[^1]] with { EndLine = opened.StartLine - 1 };
            open.RemoveAt(open.Count - 1);
        }

        open.Add(found.Count);
        found.Add(opened);
    }

    private static List<DocumentSection> Seal(List<DocumentSection> found, int lastLine)
    {
        for (var index = 0; index < found.Count; index++)
        {
            if (found[index].EndLine is int.MaxValue)
                found[index] = found[index] with { EndLine = lastLine };
        }

        return found;
    }

    private static bool IsFence(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static int Level(ReadOnlySpan<char> line)
    {
        var level = 0;

        while (level < line.Length && line[level] is '#')
            level++;

        return level is > 0 and <= 6 && level < line.Length && line[level] is ' ' ? level : 0;
    }
}
