using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class DiagnosticFold
{
    public static Finding[] Findings(string root, IEnumerable<Diagnostic> found, Func<Diagnostic, string> head) =>
    [
        .. found.Select(diagnostic => new Finding(
            head(diagnostic),
            PositionFormat.Describe(root, diagnostic.Location),
            diagnostic.GetMessage(CultureInfo.InvariantCulture))),
    ];

    public static string[] Lines(string root, IEnumerable<Diagnostic> found, Func<Diagnostic, string> head) =>
        Lines(Findings(root, found, head));

    public static string[] Lines(IEnumerable<Finding> findings) =>
    [
        .. findings
            .GroupBy(finding => (finding.Head, finding.Message))
            .Select(group => Line(group.Key.Head, group.Key.Message, group))
            .Order(StringComparer.Ordinal),
    ];

    public static string Repeated(string text, int count) =>
        count is 1 ? text : string.Create(CultureInfo.InvariantCulture, $"{text} x{count}");

    private const int MaxPositions = 20;

    private static string Line(string head, string message, IEnumerable<Finding> findings) =>
        head + " " + Positions(findings) + ": " + message;

    private static string Positions(IEnumerable<Finding> findings)
    {
        var distinct = findings
            .GroupBy(finding => finding.Position, StringComparer.Ordinal)
            .Select(group => Repeated(group.Key, group.Count()))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return distinct.Length <= MaxPositions
            ? string.Join(", ", distinct)
            : string.Join(", ", distinct.Take(MaxPositions)) + string.Create(CultureInfo.InvariantCulture, $", +{distinct.Length - MaxPositions} more");
    }

    public readonly record struct Finding(string Head, string Position, string Message)
    {
        public string Key => Head + " " + Position + ": " + Message;
    }

    public static string[] PerOccurrence(IEnumerable<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();

        foreach (var key in keys.Order(StringComparer.Ordinal))
            kept.Add(Distinguished(key, seen));

        kept.Sort(StringComparer.Ordinal);

        return [.. kept];
    }

    private static string Distinguished(string key, HashSet<string> seen)
    {
        var candidate = key;
        var occurrence = 1;

        while (!seen.Add(candidate))
        {
            occurrence++;
            candidate = string.Create(CultureInfo.InvariantCulture, $"{key}  [{occurrence}]");
        }

        return candidate;
    }
}
