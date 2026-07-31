namespace TerseSharp.Core;

public static class TestNameList
{
    public static string[] Parse(string output, string? contains) =>
    [
        .. Listed(output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim())
            .Where(name => Matches(name, contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<string> Listed(string[] lines)
    {
        var header = Array.FindIndex(lines, line => line.Contains("Tests are available", StringComparison.Ordinal));

        return header < 0 ? lines.Where(IsTestName) : lines.Skip(header).Where(IsTestName);
    }

    private static bool IsTestName(string line) =>
        line.StartsWith("    ", StringComparison.Ordinal) && !line.Contains("->", StringComparison.Ordinal);

    private static bool Matches(string name, string? contains) =>
        contains is not { Length: > 0 } || name.Contains(contains, StringComparison.OrdinalIgnoreCase);
}
