using System.Buffers;
using System.Text;

namespace TerseSharp.Server;

internal static class TestPlatformArguments
{
    internal const string TrxName = "results.trx";

    private const string Qualified = "FullyQualifiedName";

    private const int ZeroTestsExitCode = 8;

    public static string[] Of(TestRunRequest request, string resultsDirectory)
    {
        var arguments = new List<string>(12)
        {
            "test", Selector(request.Target), request.Target, "--results-directory", resultsDirectory,
        };

        if (request.NoBuild)
            arguments.Add("--no-build");

        return [.. request.Scope.Applied(arguments), "--", .. Forwarded(request)];
    }

    internal static string Selector(string target) =>
        target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
        || target.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase)
            ? "--solution"
            : "--project";

    private static List<string> Forwarded(TestRunRequest request)
    {
        var forwarded = new List<string>(12);

        forwarded.AddRange(Report(request.Reporter));

        if (DotnetRunner.HangWindow(request.Timeout) is { TotalSeconds: >= 1 } window)
            forwarded.AddRange(["--timeout", string.Create(CultureInfo.InvariantCulture, $"{(long)window.TotalSeconds}s")]);

        forwarded.AddRange(["--ignore-exit-code", ZeroTestsExitCode.ToString(CultureInfo.InvariantCulture)]);

        if (request.Filter is { Length: > 0 } filter)
            forwarded.AddRange(Filtered(request.Reporter, filter));

        return forwarded;
    }

    private static string[] Report(TestReporter reporter) => reporter is TestReporter.XunitTrx
        ? ["--report-xunit-trx", "--report-xunit-trx-filename", TrxName]
        : ["--report-trx", "--report-trx-filename", TrxName];

    internal static string[] Filtered(TestReporter reporter, string filter) =>
        reporter is TestReporter.XunitTrx
            ? ["--filter-method", .. filter.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Named)]
            : ["--filter", filter];

    private static bool IsQualified(string part) =>
        (part.StartsWith(Qualified + "=", StringComparison.Ordinal)
            || part.StartsWith(Qualified + "~", StringComparison.Ordinal))
        && part.AsSpan(Qualified.Length + 1).IndexOfAny(Compound) < 0;

    internal static bool Untranslatable(TestReporter reporter, string? filter) =>
        reporter is TestReporter.XunitTrx
        && filter is { Length: > 0 } expression
        && !Array.TrueForAll(expression.Split('|', StringSplitOptions.RemoveEmptyEntries), IsQualified);

    private static string Named(string part)
    {
        var value = Unescaped(part[(Qualified.Length + 1)..]);

        return part[Qualified.Length] is '~' ? "*" + value + "*" : value;
    }

    private static string Unescaped(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var text = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '\\' && index + 1 < value.Length)
                index++;

            text.Append(value[index]);
        }

        return text.ToString();
    }

    private static readonly SearchValues<char> Compound = SearchValues.Create("&!|=~");
}
