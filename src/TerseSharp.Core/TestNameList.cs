namespace TerseSharp.Core;

public static class TestNameList
{
    private const string DiscoverySummary = "Test discovery summary";

    private const string FoundMarker = "found ";

    public static string[] Parse(string output, string? contains) => Parse([output], contains);

    public static string[] Parse(IEnumerable<string> outputs, string? contains) =>
    [
        .. outputs
            .SelectMany(output => Listed(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)))
            .Select(line => line.Trim())
            .Where(name => Matches(name, contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<string> Listed(string[] lines)
    {
        var summary = Array.FindIndex(lines, line => line.Contains(DiscoverySummary, StringComparison.Ordinal));

        return summary >= 0 ? Discovered(lines, summary) : Available(lines);
    }

    private static IEnumerable<string> Available(string[] lines)
    {
        var header = Array.FindIndex(lines, line => line.Contains("Tests are available", StringComparison.Ordinal));

        return header < 0 ? lines.Where(IsTestName) : lines.Skip(header).Where(IsTestName);
    }

    private static string[] Discovered(string[] lines, int summary)
    {
        var indented = lines.Take(summary).Where(IsIndented).ToArray();
        var found = Found(lines[summary]);

        return found > 0 && found < indented.Length ? indented[^found..] : indented;
    }

    private static int Found(string summary)
    {
        var marker = summary.IndexOf(FoundMarker, StringComparison.Ordinal);

        if (marker < 0)
            return 0;

        var digits = summary.AsSpan(marker + FoundMarker.Length);
        var end = 0;

        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
            end++;

        return int.TryParse(digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
    }

    private static bool IsIndented(string line) =>
        line.StartsWith(' ') && line.AsSpan().Trim().Length > 0;

    private static bool IsTestName(string line) =>
        line.StartsWith("    ", StringComparison.Ordinal) && !line.Contains("->", StringComparison.Ordinal);

    private static bool Matches(string name, string? contains) =>
        contains is not { Length: > 0 } || name.Contains(contains, StringComparison.OrdinalIgnoreCase);
}
