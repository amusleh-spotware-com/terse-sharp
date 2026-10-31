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
    public async Task GetTypeOutline_OfATypeWithAMemberUnderAnInactiveIf_NamesItAndTheFrameworkItAnswered()
    {
        var path = Path.Combine(Root, "src", "Multi.Core", "Gated.cs");
        const string Gated = "namespace Multi.Core;\n\npublic static class Gated\n{\n    public static int Always() => 1;\n#if NET\n    public static int OnlyModern() => 2;\n#else\n    public static int OnlyLegacy() => 3;\n#endif\n}\n";

        await server.CallAsync("write_text", new() { ["path"] = path, ["content"] = Gated, ["force"] = true }, TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "Multi.Core.Gated", ["contains"] = "Only" }, TestContext.Current.CancellationToken);

            Assert.Contains("1 member(s) under an inactive #if not shown (this outline answers for NET", text, StringComparison.Ordinal);
            Assert.Contains("OnlyModern", text, StringComparison.Ordinal);
            Assert.Contains("OnlyLegacy", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = path, ["delete"] = true, ["force"] = true }, TestContext.Current.CancellationToken);
        }
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
    public async Task DeleteSymbol_OfTwoMembersOneDeclaredForOneTargetFrameworkOnly_RemovesBothAndListsTheFileOnce()
    {
        var path = Path.Combine(Root, "src", "Multi.Core", "Numbers.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        try
        {
            await File.WriteAllTextAsync(path, GuardedNumbers, TestContext.Current.CancellationToken);
            await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true }, TestContext.Current.CancellationToken);

            var text = await server.CallAsync("delete_symbol", new() { ["symbolIds"] = new[] { "Numbers.Unused", "Numbers.Modern" } }, TestContext.Current.CancellationToken);
            var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

            Assert.Single(text.Split('\n'), line => line.Contains("Numbers.cs  changedLines=", StringComparison.Ordinal));
            Assert.DoesNotContain("<<<<<<<", after, StringComparison.Ordinal);
            Assert.DoesNotContain("Unused", after, StringComparison.Ordinal);
            Assert.DoesNotContain("Modern", after, StringComparison.Ordinal);
            Assert.Contains("#if NET10_0_OR_GREATER", after, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
            await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true }, TestContext.Current.CancellationToken);
        }
    }

    private const string GuardedNumbers = """
    namespace Multi.Core;

    public static class Numbers
    {
        public static int Doubled(int value) => value * 2;

        public static int Unused() => 1;

    #if NET10_0_OR_GREATER
        public static int Modern() => 2;
    #endif
    }

    """;

    [Fact]
    public async Task Cleanup_WhereNoEditorConfigGovernsTheFile_SaysSoInsteadOfRewritingSilently()
    {
        var text = await Cleaned("src/Multi.Core/Squashed.cs", "all");

        Assert.Contains("1 files changed", text, StringComparison.Ordinal);
        Assert.Contains(Ungoverned, text, StringComparison.Ordinal);
        Assert.Contains("no ReSharper .sln.DotSettings beside them sets indentation or cast spacing", text, StringComparison.Ordinal);
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

    [Fact]
    public async Task WriteText_AddingCodeForEachTargetFrameworkApart_IsVisibleToEveryCompilationWithoutAReload()
    {
        const string SplitNumbers = "namespace Multi.Core;\n\npublic static class Numbers\n{\n    public static int Doubled(int value) => value * 2;\n\n#if NET10_0_OR_GREATER\n    public static int Modern() => 2;\n#else\n    public static int Legacy() => 3;\n#endif\n}\n";
        var path = Path.Combine(Root, "src", "Multi.Core", "Numbers.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(path);

        try
        {
            var written = await server.CallAsync("write_text", new() { ["path"] = path, ["content"] = SplitNumbers, ["overwrite"] = true }, TestContext.Current.CancellationToken);
            var sources = await server.CallAsync("get_symbol_source", new() { ["symbolIds"] = new[] { "Numbers.Modern", "Numbers.Legacy" } }, TestContext.Current.CancellationToken);
            var deleted = await server.CallAsync("delete_symbol", new() { ["symbolId"] = "Numbers.Modern" }, TestContext.Current.CancellationToken);
            var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

            Assert.DoesNotContain("ERROR", written, StringComparison.Ordinal);
            Assert.DoesNotContain("NOT_RESOLVED", sources, StringComparison.Ordinal);
            Assert.DoesNotContain("SymbolNotFound", deleted, StringComparison.Ordinal);
            Assert.DoesNotContain("Modern", after, StringComparison.Ordinal);
            Assert.Contains("Legacy", after, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, stamp);
            await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true }, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task EditText_AddingCodeForEachTargetFrameworkApart_IsVisibleToEveryCompilationWithoutAReload()
    {
        const string Baseline = "namespace Multi.Core;\n\npublic static class Numbers\n{\n    public static int Doubled(int value) => value * 2;\n}\n";
        var path = Path.Combine(Root, "src", "Multi.Core", "Numbers.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(path);

        try
        {
            await File.WriteAllTextAsync(path, Baseline, TestContext.Current.CancellationToken);
            await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true }, TestContext.Current.CancellationToken);

            var edited = await server.CallAsync("edit_text", new()
            {
                ["path"] = path,
                ["oldText"] = "    public static int Doubled(int value) => value * 2;\n",
                ["newText"] = "    public static int Doubled(int value) => value * 2;\n\n#if NET10_0_OR_GREATER\n    public static int Modern() => 2;\n#else\n    public static int Legacy() => 3;\n#endif\n",
            }, TestContext.Current.CancellationToken);
            var sources = await server.CallAsync("get_symbol_source", new() { ["symbolIds"] = new[] { "Numbers.Modern", "Numbers.Legacy" } }, TestContext.Current.CancellationToken);

            Assert.DoesNotContain("ERROR", edited, StringComparison.Ordinal);
            Assert.DoesNotContain("NOT_RESOLVED", sources, StringComparison.Ordinal);
            Assert.Contains("=> 2", sources, StringComparison.Ordinal);
            Assert.Contains("=> 3", sources, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, stamp);
            await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Root, "MultiTargetSolution.slnx"), ["reload"] = true }, TestContext.Current.CancellationToken);
        }
    }
}
