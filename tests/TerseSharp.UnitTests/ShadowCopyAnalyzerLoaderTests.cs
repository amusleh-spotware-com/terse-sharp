using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ShadowCopyAnalyzerLoaderTests
{
    private static string GeneratorSolutionPath { get; } = Path.Combine(
        Fixtures.RepositoryRoot, "fixtures", "GeneratorSolution", "GeneratorSolution.slnx");

    private static string AnalyzerAssembly { get; } = Path.Combine(
        Fixtures.RepositoryRoot,
        "fixtures",
        "GeneratorSolution",
        "src",
        "Fixture.Generator",
        "bin",
        "Debug",
        "netstandard2.0",
        "Fixture.Generator.dll");

    [Fact]
    public async Task ReboundSolution_AfterRunningTheAnalyzer_LeavesTheOriginalAssemblyWritable()
    {
        await EnsureBuiltAsync();

        Assert.True(Writable(), "the analyzer assembly was already locked before the test started");

        using var registry = new WorkspaceRegistry(watch: false);

        await registry.LoadAsync(GeneratorSolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var forked = AnalyzerRebind.Rebound(lease.Workspace.Solution, ShadowCopyAnalyzerLoader.Shared);
        var diagnostics = await AnalyzerDiagnosticsAsync(Consumer(forked));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id is "FIX001");
        Assert.True(Writable(), "the rebound solution still mapped the analyzer assembly in place");
    }

    private static async Task EnsureBuiltAsync()
    {
        if (File.Exists(AnalyzerAssembly))
            return;

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add("build");
        start.ArgumentList.Add(GeneratorSolutionPath);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(File.Exists(AnalyzerAssembly), await output + await error);
    }

    private static Project Consumer(Solution solution) =>
        solution.Projects.Single(project => project.Name is "Fixture.Generated");

    private static async Task<ImmutableArray<Diagnostic>> AnalyzerDiagnosticsAsync(Project project)
    {
        var compilation = await project.GetCompilationAsync(TestContext.Current.CancellationToken);
        var analyzers = project.AnalyzerReferences
            .SelectMany(reference => reference.GetAnalyzers(LanguageNames.CSharp))
            .ToImmutableArray();

        Assert.False(analyzers.IsEmpty, "the fixture exposed no analyzer, so the assertion cannot fail");

        return await compilation!
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    private static bool Writable()
    {
        try
        {
            using var stream = File.Open(AnalyzerAssembly, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
