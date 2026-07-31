using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Core;

public static partial class TestResultParser
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TestRunReport Parse(IEnumerable<string> reportFiles, string workspaceRoot)
    {
        var report = TestRunReport.Empty;

        foreach (var file in reportFiles)
            report = report.Merge(ParseFile(file, workspaceRoot));

        return report.Sorted();
    }

    private static TestRunReport ParseFile(string path, string workspaceRoot)
    {
        if (Load(path)?.Root is not { } run)
            return TestRunReport.Empty;

        var results = run.Descendants(Trx + "Results").Elements(Trx + "UnitTestResult").ToArray();

        return new TestRunReport(
            results.Count(result => Outcome(result) is "Passed"),
            results.Count(result => Outcome(result) is "Failed"),
            results.Count(result => Outcome(result) is "NotExecuted"),
            results.Length,
            results.Sum(Duration),
            Failures(results, workspaceRoot),
            Passed(results));
    }

    private static XDocument? Load(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static ImmutableArray<TestFailure> Failures(XElement[] results, string workspaceRoot) =>
    [
        .. results
            .Where(result => Outcome(result) is "Failed")
            .Select(result => new TestFailure(Value(result, "testName"), Message(result), Frame(result, workspaceRoot), Duration(result)))
            .OrderBy(failure => failure.Name, StringComparer.Ordinal),
    ];

    private static ImmutableArray<TestTiming> Passed(XElement[] results) =>
    [
        .. results
            .Where(result => Outcome(result) is "Passed")
            .Select(result => new TestTiming(Value(result, "testName"), Duration(result)))
            .OrderBy(test => test.Name, StringComparer.Ordinal),
    ];

    private static string Message(XElement result) =>
        result.Descendants(Trx + "Message").FirstOrDefault()?.Value.Trim() ?? string.Empty;

    private static string? Frame(XElement result, string workspaceRoot)
    {
        if (result.Descendants(Trx + "StackTrace").FirstOrDefault()?.Value is not { } stack)
            return null;

        foreach (Match match in FrameLine().Matches(stack))
        {
            if (Relative(match.Groups["file"].Value, workspaceRoot) is { } file)
                return string.Create(CultureInfo.InvariantCulture, $"{file}:{match.Groups["line"].Value}");
        }

        return null;
    }

    private static string? Relative(string file, string workspaceRoot)
    {
        var root = Normalized(workspaceRoot);
        var candidate = Normalized(file);

        return candidate.StartsWith(root + '/', PathBoundary.Comparison) ? candidate[(root.Length + 1)..] : null;
    }

    private static string Normalized(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static string Outcome(XElement result) => Value(result, "outcome");

    private static string Value(XElement element, string name) => element.Attribute(name)?.Value ?? string.Empty;

    private static long Duration(XElement result) =>
        TimeSpan.TryParse(Value(result, "duration"), CultureInfo.InvariantCulture, out var duration)
            ? (long)duration.TotalMilliseconds
            : 0;

    [GeneratedRegex(@"\sin\s(?<file>[^\r\n]+):line\s(?<line>\d+)")]
    private static partial Regex FrameLine();
}
