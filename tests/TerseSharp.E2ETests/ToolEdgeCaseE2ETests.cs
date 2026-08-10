using System.Diagnostics;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolEdgeCaseE2ETests(TerseServerFixture server)
{
    private static readonly TimeSpan IndexBudget = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ReadText_WithStartAfterEnd_ReturnsNoLinesRatherThanFailing()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 9,
            ["endLine"] = 3,
        });

        Assert.StartsWith("0 lines", text, StringComparison.Ordinal);
        Assert.Contains("(total=5)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithARangeBeyondTheFile_ReturnsNoLines()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 9000,
            ["endLine"] = 9100,
        });

        Assert.StartsWith("0 lines", text, StringComparison.Ordinal);
        Assert.Contains("(total=5)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithNegativeLines_ClampsToTheStartOfTheFile()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = -50,
            ["endLine"] = 2,
        });

        Assert.Contains("1: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithMaxLinesOne_ReturnsOneLineAndSaysItTruncated()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "appsettings.json", ["maxLines"] = 1 });

        Assert.StartsWith("1/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2: ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnADirectory_ReturnsAnError()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "src" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OutsideTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "../../../../etc/passwd" });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithMaxResultsOne_TruncatesAndSaysSo()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "*.cs", ["maxResults"] = 1 });

        Assert.StartsWith("1/", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithADoubleStarGlob_MatchesAcrossDirectories()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs" });

        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithAnInvalidPattern_ReturnsAnErrorWithARemedy()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = "(", ["glob"] = "*.cs" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithACatastrophicPattern_StillCompletes()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = "(a+)+$", ["glob"] = "*.cs" });

        Assert.Contains("matches", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithLookaround_FallsBackAndStillAnswers()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = "public(?=\\s)", ["glob"] = "*.cs" });

        Assert.Contains("matches", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_ForSomethingAbsent_ReportsZeroRatherThanFailing()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "zzz-no-such-token-zzz" });

        Assert.Contains("0 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbol_WithAMalformedId_NamesTheErrorAndSuggestsSearch()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "notanid" });

        Assert.Contains("ERROR SymbolNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_OnANonCsharpFile_ReportsDocumentNotFound()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "appsettings.json" });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithoutSignatures_IsCheaperAndKeepsEveryId()
    {
        var full = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });
        var lean = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["signatures"] = false,
        });

        Assert.True(lean.Length < full.Length, $"lean={lean.Length} full={full.Length}");
        Assert.Contains("  OrderBook.TotalVolume  ", lean, StringComparison.Ordinal);
        Assert.DoesNotContain("decimal TotalVolume(string symbol)", lean, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_WithMaxResultsOne_TruncatesButStillCountsThemAll()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["maxResults"] = 1,
        });

        Assert.StartsWith("1/4 usages in ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_ReportsWorkspaceRelativePaths()
    {
        var text = await server.CallAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
        });

        Assert.Contains("src", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TerseServerFixture.FixtureRoot, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithAnUnknownSeverity_FallsBackInsteadOfFailing()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "nonsense" });

        Assert.Contains("diagnostics", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ReportsWorkspaceRelativePaths()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "info" });

        Assert.DoesNotContain(TerseServerFixture.FixtureRoot, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WhenTheSnippetIsAbsent_RefusesAndSaysHowManyItMatched()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "appsettings.json",
            ["oldText"] = "no-such-snippet-anywhere",
            ["newText"] = "x",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("matched 0 times", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlOutline_OnACsharpFile_ReturnsAnErrorRatherThanGarbage()
    {
        var text = await server.CallAsync("xaml_outline", new() { ["path"] = "src/Fixture.Trading/Order.cs" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_OnAMissingProject_ReturnsAnErrorWithARemedy()
    {
        var text = await server.CallAsync("package_list", new() { ["project"] = "src/Nope/Nope.csproj" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnloadWorkspace_ForAPathThatIsNotLoaded_SaysSoWithoutFailing()
    {
        var text = await server.CallAsync("unload_workspace", new() { ["path"] = "C:/nope/nope.slnx" });

        Assert.Contains("not loaded", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageAdd_WithAnEmptyPackage_IsRefusedAndWritesNothingOutsideTheWorkspace()
    {
        var central = Path.Combine(TerseServerFixture.RepositoryRoot, "Directory.Packages.props");
        var before = await File.ReadAllBytesAsync(central, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("package_add", new()
        {
            ["project"] = string.Empty,
            ["package"] = string.Empty,
            ["version"] = string.Empty,
        });

        var after = await File.ReadAllBytesAsync(central, TestContext.Current.CancellationToken);

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.True(before.AsSpan().SequenceEqual(after), "the repository Directory.Packages.props was modified");
    }

    [Fact]
    public async Task PackageAdd_ForAProjectOutsideTheWorkspace_NeverReachesItsDirectoryPackagesProps()
    {
        var text = await server.CallAsync("package_add", new()
        {
            ["project"] = "../../src/TerseSharp.Core/TerseSharp.Core.csproj",
            ["package"] = "Serilog",
            ["version"] = "1.0.0",
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionAddProject_WithABlankPath_IsRefusedAndLeavesTheSolutionAlone()
    {
        var solution = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx");
        var before = await File.ReadAllBytesAsync(solution, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("solution_add_project", new() { ["project"] = string.Empty });

        var after = await File.ReadAllBytesAsync(solution, TestContext.Current.CancellationToken);

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.True(before.AsSpan().SequenceEqual(after), "the fixture solution was modified");
    }

    [Fact]
    public async Task SolutionAddProject_WithSomethingThatIsNotAProject_IsRefused()
    {
        var text = await server.CallAsync("solution_add_project", new() { ["project"] = "appsettings.json" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageAdd_ForARealProject_NeverReachesADirectoryPackagesPropsAboveTheWorkspace()
    {
        var central = Path.Combine(TerseServerFixture.RepositoryRoot, "Directory.Packages.props");
        var before = await File.ReadAllBytesAsync(central, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("package_add", new()
        {
            ["project"] = "src/Fixture.Trading/Fixture.Trading.csproj",
            ["package"] = "Serilog",
            ["version"] = "9.9.9",
        });

        var after = await File.ReadAllBytesAsync(central, TestContext.Current.CancellationToken);

        Assert.True(before.AsSpan().SequenceEqual(after), "the repository Directory.Packages.props was modified");
        Assert.Contains("above the workspace root", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_OutsideTheWorkspace_IsRefusedRatherThanProbingTheFilesystem()
    {
        var text = await server.CallAsync("package_list", new()
        {
            ["project"] = "../../src/TerseSharp.Core/TerseSharp.Core.csproj",
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectProperties_OutsideTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("project_properties", new()
        {
            ["project"] = "../../src/TerseSharp.Core/TerseSharp.Core.csproj",
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ForAProjectOutsideTheWorkspace_IsRefusedBeforeSpawningAnything()
    {
        var text = await server.CallAsync("build", new()
        {
            ["project"] = "../../src/TerseSharp.Core/TerseSharp.Core.csproj",
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithBothATestAndAFilter_IsRefused()
    {
        var text = await server.CallAsync("run_tests", new()
        {
            ["test"] = "Some.Test",
            ["filter"] = "Category=Fast",
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("cannot be combined", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTests_ForAProjectOutsideTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("list_tests", new()
        {
            ["project"] = "../../tests/TerseSharp.UnitTests/TerseSharp.UnitTests.csproj",
            ["timeoutSeconds"] = 10,
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_SkipsAFileTooLargeToScanAndSaysHowMany()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "terse-huge.txt");
        await File.WriteAllTextAsync(path, new string('q', 17 * 1024 * 1024), TestContext.Current.CancellationToken);
        try
        {
            await IndexedAsync("terse-huge.txt");

            var text = await server.CallAsync("search_text", new() { ["pattern"] = "qqqq", ["glob"] = "*.txt" });

            Assert.Contains("skipped 1 files over 16 MB", text, StringComparison.Ordinal);
            Assert.Contains("0 matches", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task IndexedAsync(string name)
    {
        var elapsed = Stopwatch.StartNew();
        var listing = string.Empty;

        while (elapsed.Elapsed < IndexBudget)
        {
            listing = await server.CallAsync("find_files", new() { ["glob"] = "*.txt" });

            if (listing.Contains(name, StringComparison.Ordinal))
                return;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail(string.Create(
            CultureInfo.InvariantCulture,
            $"'{name}' never reached the workspace file index within {elapsed.Elapsed.TotalSeconds:F0}s of a {IndexBudget.TotalSeconds:F0}s budget; find_files last answered: {listing}"));
    }

    [Fact]
    public async Task ReplaceSymbol_WithAnArrayEntryThatCannotConvert_NamesTheArrayParameterAndQuotesTheOffendingText()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["declarations"] = new object[] { 17 },
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("declarations is an array parameter", text, StringComparison.Ordinal);
        Assert.Contains("falls near:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithTwoArrayParametersBothLongerThanTheOffset_NeverAssertsAnOffsetItCannotAttribute()
    {
        var padding = new string('x', 200);

        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "OrderBook.Add", padding },
            ["declarations"] = new object[] { padding, 17 },
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("symbolIds is an array parameter", text, StringComparison.Ordinal);
    }
}
