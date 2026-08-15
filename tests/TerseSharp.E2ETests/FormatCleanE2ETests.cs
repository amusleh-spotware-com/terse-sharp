namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class FormatCleanE2ETests(TerseServerFixture server)
{
    private const string StyleSample = "src/Fixture.Trading/StyleSample.cs";

    [Fact]
    public async Task Format_WithVerify_ReportsAVerdictInsteadOfADiff()
    {
        var text = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["verify"] = true,
        });

        Assert.Equal("clean", text);
        Assert.DoesNotContain("VERIFY_FAILED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("changedLines=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@@", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithAGlobThatMatchesNothing_ReportsDocumentNotFound()
    {
        var text = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/*.nothing",
            ["verify"] = true,
        });

        Assert.StartsWith("ERROR DocumentNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAGlob_ScopesToTheMatchingFilesOnly()
    {
        var scoped = await server.CallAsync("cleanup", new()
        {
            ["path"] = "src/Fixture.Trading/Style*.cs",
            ["fix"] = "style",
            ["verify"] = true,
        });

        Assert.Contains("VERIFY_FAILED 1 file(s) would change", scoped, StringComparison.Ordinal);
        Assert.Contains("StyleSample.cs", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithVerify_NamesTheFileThatWouldChange()
    {
        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = StyleSample,
            ["fix"] = "style",
            ["verify"] = true,
        });

        Assert.Contains("VERIFY_FAILED 1 file(s) would change", text, StringComparison.Ordinal);
        Assert.Contains("StyleSample.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithFixStyle_AppliesTheIdeCodeFix()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "StyleSample.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = StyleSample,
            ["fix"] = "style",
            ["ids"] = "IDE0028",
            ["dryRun"] = true,
        });

        Assert.Contains("-    public List<int> Quantities { get; } = new List<int>();", text, StringComparison.Ordinal);
        Assert.Contains("+    public List<int> Quantities { get; } = [];", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_WithFixAnalyzers_AppliesTheAnalyzerCodeFix()
    {
        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = StyleSample,
            ["fix"] = "analyzers",
            ["ids"] = "CA1822",
            ["dryRun"] = true,
        });

        Assert.Contains("+    public static int Doubled(int quantity) => quantity * 2;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithFixStyle_LeavesTheAnalyzerDiagnosticAlone()
    {
        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = StyleSample,
            ["fix"] = "style",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("public static int Doubled", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_WithAnUnknownFixMode_RefusesAndNamesTheModes()
    {
        var text = await server.CallAsync("cleanup", new() { ["fix"] = "everything" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("fix=usings, style, analyzers or all", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_WithDryRun_CountsTheOutputWithoutDeletingIt()
    {
        var obj = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "obj");

        var text = await server.CallAsync("clean", new() { ["dryRun"] = true });

        Assert.Contains("locked=0 state=dryRun", text, StringComparison.Ordinal);
        Assert.Contains("projects=", text, StringComparison.Ordinal);
        Assert.Contains("REMOVED", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(obj), obj + " was deleted by a dryRun");
    }

    [Fact]
    public async Task Clean_ForAProjectOutsideTheWorkspace_Refuses()
    {
        var text = await server.CallAsync("clean", new()
        {
            ["project"] = "../../outside-the-workspace",
            ["dryRun"] = true,
        });

        Assert.StartsWith("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_ForAProjectThatDoesNotExist_RefusesInsteadOfCleaningTheWorkspace()
    {
        var text = await server.CallAsync("clean", new()
        {
            ["project"] = "src/Fixture.Trading/Nope.csproj",
            ["dryRun"] = true,
        });

        Assert.StartsWith("ERROR ProjectNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Format_WhenApplied_ReportsOneLinePerFileAndKeepsTheDiffBehindVerbose()
    {
        var quiet = await server.CallAsync("format", new() { ["path"] = "src/**/*.cs" });

        Assert.Contains("files changed", quiet, StringComparison.Ordinal);
        Assert.DoesNotContain("@@", quiet, StringComparison.Ordinal);
    }
}
