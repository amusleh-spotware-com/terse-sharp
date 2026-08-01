using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class CodeFixServiceTests
{
    private const string StyleSample = "src/Fixture.Trading/StyleSample.cs";

    [Fact]
    public async Task Cleanup_WithAnalyzerFixes_AppliesTheAnalyzerCodeFix()
    {
        var text = await CleanupAsync(FixMode.Analyzers, ["CA1822"]);

        Assert.Contains("+    public static int Doubled(int quantity) => quantity * 2;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithStyleFixes_AppliesTheIdeCodeFix()
    {
        var text = await CleanupAsync(FixMode.Style, ["IDE0028"]);

        Assert.Contains("+    public List<int> Quantities { get; } = [];", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithStyleFixes_LeavesAnalyzerDiagnosticsAlone()
    {
        var text = await CleanupAsync(FixMode.Style, []);

        Assert.DoesNotContain("public static int Doubled", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAnIdThatNeverFires_ChangesNothing()
    {
        var text = await CleanupAsync(FixMode.All, ["CA9999"]);

        Assert.Contains("0 files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixerCatalog_ForAProjectEnforcingCodeStyle_ExposesTheIdeFixers()
    {
        var catalog = await CatalogAsync();

        Assert.True(catalog.Count > 0, "no code fix provider was discovered");
        Assert.True(catalog.HasStyleFixers, "no IDE code fix provider was discovered");
    }

    [Fact]
    public async Task FixerCatalog_ForADiagnosticNothingFixes_ReturnsNoProvider()
    {
        var catalog = await CatalogAsync();

        Assert.Null(catalog.For("CA9999"));
    }

    private static async Task<FixerCatalog> CatalogAsync()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        return FixerCatalog.For(lease.Workspace.Solution.Projects.First());
    }

    private static async Task<string> CleanupAsync(FixMode mode, string[] ids)
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            new FixScope(StyleSample, ChangedOnly: false),
            new FixRequest(mode, ids, DiagnosticSeverity.Info, Verify: false),
            new EditOptions("cleanup", DryRun: true, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsOk, result.Error?.Message);

        return result.Value!;
    }
}
