using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace TerseSharp.Core;

public enum TestReporter
{
    VsTestLogger,
    TestingPlatformTrx,
    XunitTrx,
    Unknown,
}

public static class TestReporterProbe
{
    private const string PlatformRunner = "Microsoft.Testing.Platform";

    private const string TrxExtension = "Microsoft.Testing.Extensions.TrxReport";

    private const string MsTestSdk = "MSTest.Sdk";

    private const string XunitPackage = "\"xunit.v3";

    private const int MaxProjects = 200;

    private static readonly string BinDirectory = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;

    private static readonly string ObjDirectory = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public static async Task<TestReporter> DetectAsync(string root, string target, CancellationToken cancellationToken)
    {
        if (!await UsesTestingPlatformAsync(root, cancellationToken).ConfigureAwait(false))
            return TestReporter.VsTestLogger;

        return await ReportedByAsync(root, target, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> UsesTestingPlatformAsync(string root, CancellationToken cancellationToken)
    {
        if (Nearest(root) is not { } candidate)
            return false;

        var stamp = FileStamp.Of(candidate);

        if (Runners.TryGetValue(candidate, out var cached) && cached.Stamp == stamp)
            return cached.Platform;

        var platform = await DeclaresPlatformRunnerAsync(candidate, cancellationToken).ConfigureAwait(false);

        Runners[candidate] = (stamp, platform);

        return platform;
    }

    private static async Task<bool> DeclaresPlatformRunnerAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            return Runner(document.RootElement) is { } runner
                && PlatformRunner.Equals(runner, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Runner(JsonElement root) =>
        root.ValueKind is JsonValueKind.Object
        && root.TryGetProperty("test", out var test)
        && test.ValueKind is JsonValueKind.Object
        && test.TryGetProperty("runner", out var runner)
        && runner.ValueKind is JsonValueKind.String
            ? runner.GetString()
            : null;

    private static async Task<TestReporter> ReportedByAsync(string root, string target, CancellationToken cancellationToken)
    {
        var xunit = false;

        foreach (var project in await ProjectsAsync(root, target, cancellationToken).ConfigureAwait(false))
        {
            var text = await ReadAsync(project, cancellationToken).ConfigureAwait(false);

            if (Declares(text, TrxExtension) || Declares(text, MsTestSdk))
                return TestReporter.TestingPlatformTrx;

            xunit |= text.Contains(XunitPackage, StringComparison.OrdinalIgnoreCase);
        }

        return xunit ? TestReporter.XunitTrx : TestReporter.Unknown;
    }

    private static bool Declares(string text, string package) =>
        text.Contains(package + '"', StringComparison.OrdinalIgnoreCase)
        || text.Contains(package + '\'', StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<string>> ProjectsAsync(string root, string target, CancellationToken cancellationToken)
    {
        if (target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return [target];

        if (SolutionFile.IsSolutionFile(target) && File.Exists(target))
            return Absolute(target, await SolutionFile.ProjectsAsync(target, cancellationToken).ConfigureAwait(false));

        return [.. Directory.EnumerateFiles(root, "*.csproj", Walk).Where(Compiled).Take(MaxProjects)];
    }

    private static bool Compiled(string path) =>
        !path.AsSpan().Contains(BinDirectory, PathBoundary.Comparison)
        && !path.AsSpan().Contains(ObjDirectory, PathBoundary.Comparison);

    private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static async Task<ImmutableArray<string>> TestProjectsAsync(string root, string target, CancellationToken cancellationToken)
    {
        var found = ImmutableArray.CreateBuilder<string>();

        foreach (var project in await ProjectsAsync(root, target, cancellationToken).ConfigureAwait(false))
        {
            if (DeclaresATestFramework(await ReadAsync(project, cancellationToken).ConfigureAwait(false)))
                found.Add(project);
        }

        return found.ToImmutable();
    }

    private static bool DeclaresATestFramework(string text) =>
        Declares(text, TrxExtension)
        || Declares(text, MsTestSdk)
        || text.Contains(XunitPackage, StringComparison.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, (FileStamp Stamp, bool Platform)> Runners = new(StringComparer.FromComparison(PathBoundary.Comparison));

    private static string? Nearest(string root)
    {
        for (var directory = root; directory is { Length: > 0 }; directory = Path.GetDirectoryName(directory))
        {
            var candidate = Path.Combine(directory, "global.json");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string[] Absolute(string solutionPath, IReadOnlyList<string> projects)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? string.Empty;
        var resolved = new string[projects.Count];

        for (var index = 0; index < projects.Count; index++)
            resolved[index] = Path.GetFullPath(Path.Combine(directory, projects[index]));

        return resolved;
    }
}
