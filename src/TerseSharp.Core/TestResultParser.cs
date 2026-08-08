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
        var passed = results.Count(result => Outcome(result) is "Passed");
        var failed = results.Count(result => Outcome(result) is "Failed");
        var skipped = results.Count(result => Outcome(result) is "NotExecuted");
        var duration = results.Sum(Duration);
        return new TestRunReport(passed, failed, skipped, results.Length, duration, Failures(results, workspaceRoot), Passed(results))
        {
            Projects = results.Length is 0
                ? []
                : [new TestProjectSummary(ProjectName(run, results, path), passed, failed, skipped, results.Length, duration)],
        };
    }


    private static bool Named(string value) => value.Length > 0;

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

    private static int CommonLength(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < limit && left[index] == right[index])
            index++;
        return index;
    }

    private static string? Namespaced(XElement[] results)
    {
        if (results.Length is 0)
            return null;
        var shared = Value(results[0], "testName").AsSpan();
        foreach (var result in results)
            shared = shared[..CommonLength(shared, Value(result, "testName"))];
        var cut = shared.LastIndexOf('.');
        return cut > 0 ? shared[..cut].ToString() : null;
    }

    private static string ProjectName(XElement run, XElement[] results, string path) => run.Descendants(Trx + "TestMethod").Attributes("codeBase").Select(attribute => attribute.Value).FirstOrDefault(Named) is { } assembly
    ? AssemblyName(assembly)
    : Namespaced(results) ?? AssemblyName(path);

    private static string AssemblyName(ReadOnlySpan<char> path)
    {
        var name = path[(path.LastIndexOfAny('/', '\\') + 1)..];
        var dot = name.LastIndexOf('.');

        return dot > 0 ? name[..dot].ToString() : name.ToString();
    }
}
