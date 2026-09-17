namespace TerseSharp.E2ETests;

[Collection(nameof(MultiTargetSolutionCollection))]
public sealed class MultiTargetE2ETests : IAsyncLifetime
{
    private const string Ungoverned = "no .editorconfig at or above these files sets indent_style";

    private static readonly string Root =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "MultiTargetSolution");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        server = await TerseServerProcess.StartAsync(
            Root,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--tools",
                "all",
                "--workspace",
                Path.Combine(Root, "MultiTargetSolution.slnx"),
            ],
            TestContext.Current.CancellationToken);

        await server.CallAsync("build", [], TestContext.Current.CancellationToken);

        await server.CallAsync(
            "load_workspace",
            new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await server.StopAsync();

    [Fact]
    public async Task FindUsages_OverAMultiTargetedProject_ReportsOneRecordPerSourcePosition()
    {
        var text = await server.CallAsync(
            "find_usages",
            new() { ["symbolId"] = "Numbers.Doubled" },
            TestContext.Current.CancellationToken);

        var record = Assert.Single(Records(text));

        Assert.StartsWith("1 usages in 1 files", text, StringComparison.Ordinal);
        Assert.DoesNotContain(", ", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxUsages_OverAMultiTargetedProject_ReportsOneRecordPerSourcePosition()
    {
        var text = await server.CallAsync(
            "resx_usages",
            new() { ["key"] = "Probe_Caption" },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("1 usages", text, StringComparison.Ordinal);
        Assert.Single(Records(text));
    }

    [Fact]
    public async Task Cleanup_WhereNoEditorConfigGovernsTheFile_SaysSoInsteadOfRewritingSilently()
    {
        var text = await Cleaned("src/Multi.Core/Squashed.cs", "all");

        Assert.Contains("1 files changed", text, StringComparison.Ordinal);
        Assert.Contains(Ungoverned, text, StringComparison.Ordinal);
        Assert.Contains(".sln.DotSettings is not read", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_InAModeThatNeverReformats_SaysNothingAboutWhitespaceEvenWhenItChangesTheFile()
    {
        var text = await Cleaned("src/Multi.Core/Sealable.cs", "analyzers");

        Assert.Contains("1 files changed", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class Sealable", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Ungoverned, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_OnAFileWhoseUsingsAreNotSystemFirst_LeavesTheirOrderAlone()
    {
        var text = await Cleaned("src/Multi.Core/UsingOrder.cs", "all");

        Assert.Contains("0 files changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Globalization", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_ThatActuallyWrites_CarriesTheSameUngovernedNoteAsItsPreview()
    {
        const string Squashed = "src/Multi.Core/Squashed.cs";
        const string Content = "namespace Multi.Core;\n\npublic static class Squashed\n{\n    public static int Value() =>   1;\n}\n";

        try
        {
            var text = await server.CallAsync(
                "cleanup",
                new() { ["path"] = Squashed, ["fix"] = "all", ["verbose"] = true },
                TestContext.Current.CancellationToken);

            Assert.Contains("1 files changed", text, StringComparison.Ordinal);
            Assert.Contains(Ungoverned, text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync(
                "write_text",
                new() { ["path"] = Squashed, ["content"] = Content, ["force"] = true },
                TestContext.Current.CancellationToken);
        }
    }

    private Task<string> Cleaned(string path, string fix) => server.CallAsync(
        "cleanup",
        new() { ["path"] = path, ["fix"] = fix, ["dryRun"] = true, ["verbose"] = true },
        TestContext.Current.CancellationToken);

    private static string[] Records(string text) =>
        [.. text.Split('\n').Where(line => line.Contains("Caller.cs", StringComparison.Ordinal))];

}
