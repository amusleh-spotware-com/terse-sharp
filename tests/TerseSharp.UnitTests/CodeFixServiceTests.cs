using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
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
    public async Task Cleanup_WithAllFixes_WithholdsTheStaticFlipOnAnExternallyVisibleMember()
    {
        var text = await CleanupAsync(FixMode.All, ["CA1822"]);

        Assert.DoesNotContain("public static int Doubled", text, StringComparison.Ordinal);
        Assert.Contains("UNFIXED CA1822 x1", text, StringComparison.Ordinal);
        Assert.Contains("externally visible member", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAllFixes_StillFlipsAMemberNothingOutsideTheAssemblyCanReach()
    {
        var text = await CleanupAsync(FixMode.All, ["CA1822"]);

        Assert.Contains("+    internal static int Tripled(int quantity) => quantity * 3;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAnalyzerFixes_MirrorsCiAndStillFlipsTheExternallyVisibleMember()
    {
        var text = await CleanupAsync(FixMode.Analyzers, ["CA1822"]);

        Assert.Contains("+    public static int Doubled(int quantity) => quantity * 2;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UNFIXED CA1822", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAllFixesUnderVerify_WithholdsNothingSoItCannotHideARedCiLeg()
    {
        var text = await CleanupAsync(FixMode.All, ["CA1822"], verify: true);

        Assert.DoesNotContain("UNFIXED CA1822", text, StringComparison.Ordinal);
        Assert.Contains("StyleSample.cs", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FixMode.All, false, true)]
    [InlineData(FixMode.All, true, false)]
    [InlineData(FixMode.Analyzers, false, false)]
    [InlineData(FixMode.Style, false, false)]
    [InlineData(FixMode.Ci, false, false)]
    [InlineData(FixMode.Usings, false, false)]
    [InlineData(FixMode.None, false, false)]
    public void WithholdsVisibleShapeFixes_IsOnOnlyWhereTheModeWritesAndDoesNotMirrorCi(FixMode mode, bool mirrorsCi, bool withholds) =>
            Assert.Equal(withholds, new FixRequest(mode, [], DiagnosticSeverity.Info, mirrorsCi) { MirrorsCi = mirrorsCi }.WithholdsVisibleShapeFixes);

    [Fact]
    public void WithholdsVisibleShapeFixes_ForTheRequestGateBuildsUnderDryRun_StaysOnSoThePreviewMatchesTheWrite() =>
            Assert.True(new FixRequest(FixMode.All, [], DiagnosticSeverity.Info, Verify: true).WithholdsVisibleShapeFixes);

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

    private static async Task<string> CleanupAsync(FixMode mode, string[] ids, bool verify = false)
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            new FixScope(StyleSample, ChangedOnly: false),
            new FixRequest(mode, ids, DiagnosticSeverity.Info, verify) { MirrorsCi = verify },
            new EditOptions("cleanup", DryRun: true, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsOk, result.Error?.Message);

        return result.Value!;
    }

    [Theory]
    [InlineData("CA1822")]
    [InlineData("ca1822")]
    [InlineData("Ca1822")]
    public async Task Producing_MatchesTheRequestedIdWhateverItsCasing(string requested)
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var project = lease.Workspace.Solution.Projects.First(candidate => ProjectDiagnostics.Analyzers(candidate).Length > 0);

        Assert.False(ProjectDiagnostics.Producing(project, [requested]).IsEmpty);
    }

    [Theory]
    [InlineData(FixMode.Style)]
    [InlineData(FixMode.All)]
    public void StyleUnavailable_WhenNoIdeFixerIsRegistered_SaysNothingWasChecked(FixMode mode)
    {
        var note = CodeFixService.StyleUnavailable(mode, hasStyleFixers: false, "Fixture.Trading");

        Assert.NotNull(note);
        Assert.StartsWith("UNAVAILABLE Fixture.Trading registers no IDE code fixer", note, StringComparison.Ordinal);
        Assert.Contains("fix=style checked nothing", note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FixMode.Style, true)]
    [InlineData(FixMode.All, true)]
    [InlineData(FixMode.Usings, false)]
    [InlineData(FixMode.Analyzers, false)]
    [InlineData(FixMode.None, false)]
    public void StyleUnavailable_WhenTheFixersAreThereOrTheModeDoesNotUseThem_SaysNothing(FixMode mode, bool hasStyleFixers) => Assert.Null(CodeFixService.StyleUnavailable(mode, hasStyleFixers, "Fixture.Trading"));
}
