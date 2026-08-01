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
        var fenced = false;
        var number = 0;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            number++;

            if (IsFence(line))
                fenced = !fenced;
            else if (!fenced && Level(line) is var level and > 0)
                found.Add(Close(found, new DocumentSection(new string(line.TrimEnd()), level, number, number)));
        }

        return Seal(found, number);
    }

    public static Result<DocumentSection> Locate(IReadOnlyList<DocumentSection> sections, string heading)
    {
        var wanted = heading.TrimStart('#').Trim();
        var matches = sections.Where(section => Matches(section, wanted, heading)).ToArray();

        return matches switch
        {
            [var only] => Result.Ok(only),
            [] => Result.Fail<DocumentSection>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"no section titled '{heading}'"),
                Nearest(sections))),
            _ => Result.Fail<DocumentSection>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{heading}' names {matches.Length} sections"),
                "pass the heading with its level, e.g. '## Commands', or use startLine/endLine")),
        };
    }

    private static bool Matches(DocumentSection section, string wanted, string heading) =>
        section.Title.Equals(wanted, StringComparison.OrdinalIgnoreCase)
        && (heading.StartsWith('#') is false || section.Heading.StartsWith(heading.TrimEnd(), StringComparison.Ordinal));

    private static string Nearest(IReadOnlyList<DocumentSection> sections) => sections.Count is 0
        ? "the file has no markdown headings; use startLine/endLine"
        : "headings: " + string.Join(" | ", sections.Take(12).Select(section => section.Heading));

    private static DocumentSection Close(List<DocumentSection> found, DocumentSection opened)
    {
        for (var index = found.Count - 1; index >= 0; index--)
        {
            if (found[index].EndLine is not int.MaxValue || found[index].Level < opened.Level)
                break;

            found[index] = found[index] with { EndLine = opened.StartLine - 1 };
        }

        return opened with { EndLine = int.MaxValue };
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
