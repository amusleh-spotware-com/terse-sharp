
namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class BacklogClosureE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ReadText_WithTail_ReturnsTheLastLinesOnly()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["tail"] = 2,
        });

        Assert.StartsWith("2 lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
        Assert.Contains("OrderSubmitted", text, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Fixture.Trading", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public enum OrderSide", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WhenTheToolClipsTheRead_NamesTheLineToContinueFrom()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["maxLines"] = 3,
        });

        Assert.Contains("next: startLine=5 (total=", text, StringComparison.Ordinal);
        Assert.Contains("outline: get_file_outline path=src/Fixture.Trading/OrderBook.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithACallerChosenRange_AddsNoContinuationSteer()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 2,
            ["endLine"] = 3,
        });

        Assert.DoesNotContain("next: startLine=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithContext_ReturnsTheSurroundingLinesIndented()
    {
        var bare = await server.CallAsync("search_text", new() { ["query"] = "class OrderBook", ["glob"] = "**/*.cs" });
        var withContext = await server.CallAsync("search_text", new()
        {
            ["query"] = "class OrderBook",
            ["glob"] = "**/*.cs",
            ["context"] = 2,
        });

        Assert.True(withContext.Split('\n').Length > bare.Split('\n').Length, withContext);
        Assert.Contains("\n    ", withContext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithoutContext_IsByteIdenticalToTheOldAnswer()
    {
        var bare = await server.CallAsync("search_text", new() { ["query"] = "OrderBook", ["glob"] = "**/*.cs" });
        var explicitZero = await server.CallAsync("search_text", new()
        {
            ["query"] = "OrderBook",
            ["glob"] = "**/*.cs",
            ["context"] = 0,
        });

        Assert.Equal(bare, explicitZero);
    }

    [Fact]
    public async Task SearchText_WithUnique_CollapsesRepeatedLinesIntoOneRecord()
    {
        var every = await server.CallAsync("search_text", new() { ["query"] = "namespace Fixture", ["glob"] = "**/*.cs" });
        var collapsed = await server.CallAsync("search_text", new()
        {
            ["query"] = "namespace Fixture",
            ["glob"] = "**/*.cs",
            ["unique"] = true,
        });

        var everyRecords = every.Split('\n').Count(line => line.Contains(".cs:", StringComparison.Ordinal));
        var collapsedRecords = collapsed.Split('\n').Count(line => line.Contains(".cs:", StringComparison.Ordinal));

        Assert.True(everyRecords > collapsedRecords, $"{everyRecords} -> {collapsedRecords}\n{collapsed}");
        Assert.Contains("  x", collapsed, StringComparison.Ordinal);
        Assert.Contains("unique:", collapsed, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", collapsed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_WithSeveralIds_AnswersOnceAndNamesTheIdItCouldNotResolve()
    {
        var text = await server.CallAsync("get_symbol_source", new()
        {
            ["symbolIds"] = new[] { "OrderBook.Add", "Fixture.Trading.NoSuchMember" },
        });

        Assert.Contains("2 symbols", text, StringComparison.Ordinal);
        Assert.Contains("NOT_RESOLVED Fixture.Trading.NoSuchMember", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EverySymbolTool_AcceptsTheSymbolAliasInsteadOfSymbolId()
    {
        var source = await server.CallAsync("get_symbol_source", new() { ["symbol"] = "OrderBook.Add" });
        var usages = await server.CallAsync("find_usages", new() { ["symbol"] = "T:Fixture.Trading.OrderBook" });

        Assert.DoesNotContain("ERROR", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", usages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_WithNeitherIdNorAlias_NamesTheMissingArgument()
    {
        var text = await server.CallAsync("get_symbol_source", []);

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("symbolId", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_OnAnEnum_AddsTheEnumMember()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderSide",
            ["declaration"] = "Hold",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("Hold", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not a type declaration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_OnAnEnumMember_RewritesThatMember()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "F:Fixture.Trading.OrderSide.Sell",
            ["declaration"] = "Sell = 7",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("Sell = 7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAFilePath_AppendsANamespaceLevelType()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["declaration"] = "public sealed record OrderTag(string Value);",
            ["dryRun"] = true,
            ["verbose"] = true,
        });

        Assert.Contains("OrderTag", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithDelete_RemovesAFileAndIsRefusedOnCSharpWithoutForce()
    {
        var refused = await server.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderSide.cs",
            ["delete"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", refused, StringComparison.Ordinal);

        var missing = await server.CallAsync("write_text", new()
        {
            ["path"] = "terse-no-such-file.txt",
            ["delete"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR DocumentNotFound", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListProjects_PrintsThePathBesideEveryProject()
    {
        var text = await server.CallAsync("list_projects", []);

        Assert.Contains("Fixture.Trading", text, StringComparison.Ordinal);
        Assert.Contains(".csproj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_ReportsMappedAnalyzersWithoutUnloading()
    {
        var text = await server.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.Contains("mapped=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAndRunTests_AdvertiseConfigurationAndTargetFramework()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var name in new[] { "build", "run_tests", "rerun_failed", "list_tests" })
        {
            var schema = tools.Single(tool => tool.Name == name).JsonSchema.GetProperty("properties");

            Assert.True(schema.TryGetProperty("configuration", out _), name + " has no configuration");
            Assert.True(schema.TryGetProperty("targetFramework", out _), name + " has no targetFramework");
        }
    }

    [Fact]
    public async Task SearchText_WithAnAbsoluteRoot_SearchesOutsideTheWorkspaceAndSaysSo()
    {
        var outside = Path.GetDirectoryName(TerseServerFixture.FixtureRoot)!;
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "FixtureSolution",
            ["root"] = outside,
            ["glob"] = "**/*.slnx",
        });

        Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithARelativeRoot_IsRefusedWithARemedy()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "anything",
            ["root"] = "../not-absolute",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithARootThatDoesNotExist_SaysSoInsteadOfAnsweringZero()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "anything",
            ["root"] = Path.Combine(Path.GetTempPath(), "terse-no-such-directory-9d2f"),
        });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithMaxChars_BoundsAFileWhoseLinesAreTooLongForMaxLines()
    {
        var whole = await server.CallAsync("read_text", new() { ["path"] = "wide-lines.json" });
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-lines.json",
            ["maxChars"] = 200,
        });

        Assert.DoesNotContain("next: startLine=", whole, StringComparison.Ordinal);
        Assert.True(bounded.Length < whole.Length / 2, bounded);
        Assert.Contains("chars)", bounded, StringComparison.Ordinal);
        Assert.Contains("next: startLine=", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WhenTheClipLandsMidLine_SaysSoAndNeverSteersBackToThatLine()
    {
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-lines.json",
            ["startLine"] = 3,
            ["maxChars"] = 200,
        });

        Assert.Contains("line 3 was cut mid-way", bounded, StringComparison.Ordinal);
        Assert.DoesNotContain("next: startLine=3 ", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WhenTheCutLandsOnTheLastLine_StillSaysTheLineWasCut()
    {
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-lines.json",
            ["startLine"] = 3,
            ["endLine"] = 3,
            ["maxChars"] = 200,
        });

        Assert.StartsWith("1 lines", bounded, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", bounded, StringComparison.Ordinal);
        Assert.Contains("line 3 was cut mid-way", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithASectionAndMaxChars_HonoursTheCharacterBudget()
    {
        var whole = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["section"] = "## Wide",
        });
        var bounded = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["section"] = "## Wide",
            ["maxChars"] = 200,
        });

        Assert.DoesNotContain("ERROR", whole, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", bounded, StringComparison.Ordinal);
        Assert.True(whole.Length > 3000, whole.Length.ToString(CultureInfo.InvariantCulture));
        Assert.True(bounded.Length < 600, bounded);
        Assert.Contains("was cut mid-way", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithACallerChosenRange_CountsWhatArrivedInsteadOfClaimingTruncation()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 2,
            ["endLine"] = 3,
        });

        Assert.StartsWith("2 lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithACompleteSection_NeverCallsItTruncated()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["section"] = "## Wide",
        });

        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithSeveralPaths_AnswersThemAllInOneResponseUnderTheirOwnPathLines()
    {
        string[] paths = ["wide-lines.json", "src/Fixture.Trading/Views/OrderView.xaml", "src/Fixture.Trading/Pages/Index.cshtml"];
        var batched = await server.CallAsync("read_text", new() { ["paths"] = paths, ["maxLines"] = 5 });
        var separate = 0;

        foreach (var path in paths)
            separate += ToolCensus.Tokens(await server.CallAsync("read_text", new() { ["path"] = path, ["maxLines"] = 5 }));

        Assert.StartsWith("3 files", batched, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_FOUND", batched, StringComparison.Ordinal);
        Assert.All(paths, path => Assert.Contains(path, batched, StringComparison.Ordinal));
        Assert.True(
            ToolCensus.Tokens(batched) <= separate + 40,
            string.Create(CultureInfo.InvariantCulture, $"batched={ToolCensus.Tokens(batched)} separate={separate}\n{batched}"));
    }

    [Fact]
    public async Task ReadText_WithAPathThatDoesNotResolve_ReportsItInlineInsteadOfFailingTheCall()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["paths"] = new[] { "wide-lines.json", "no/such/file.json" },
            ["maxLines"] = 3,
        });

        Assert.StartsWith("2 files", text, StringComparison.Ordinal);
        Assert.Contains("NOT_FOUND no/such/file.json", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.Contains("\"first\": \"short\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithABlankPathEntryOrTooMany_IsRefusedByNameRatherThanTruncated()
    {
        var blank = await server.CallAsync("read_text", new() { ["paths"] = new[] { "package.json", "" } });
        var many = await server.CallAsync("read_text", new()
        {
            ["path"] = "package.json",
            ["paths"] = Enumerable.Repeat("package.json", 10).ToArray(),
        });

        Assert.Contains("'paths' carries a blank entry", blank, StringComparison.Ordinal);
        Assert.Contains("11 paths were requested", many, StringComparison.Ordinal);
        Assert.Contains("remedy:", many, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithSeveralPaths_SharesOneMaxCharsBudgetAndNamesTheEntryItClipped()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["paths"] = new[] { "wide-lines.json", "package.json" },
            ["maxChars"] = 200,
        });

        Assert.Contains("the shared maxChars budget ran out at", text, StringComparison.Ordinal);
        Assert.Contains("wide-lines.json", text, StringComparison.Ordinal);
        Assert.True(text.Length < 2000, string.Create(CultureInfo.InvariantCulture, $"{text.Length} characters for a 200-character budget"));
    }

    [Fact]
    public async Task ReadText_WithNeitherPathNorPaths_NamesBothInsteadOfReadingNothing()
    {
        var text = await server.CallAsync("read_text", []);

        Assert.Contains("path", text, StringComparison.Ordinal);
        Assert.Contains("paths", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithAPathPerEntry_EditsSeveralFilesInOneCallAndAnswersOneLinePerFile()
    {
        var first = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "multi-a.json");
        var second = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "multi-b.json");

        await File.WriteAllTextAsync(first, "{ \"probe\": 1 }", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "{ \"probe\": 1 }", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("edit_text", new()
            {
                ["path"] = "src/Fixture.Trading/multi-a.json",
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "1", ["newText"] = "2" },
                new Dictionary<string, object?> { ["oldText"] = "1", ["newText"] = "3", ["path"] = "src/Fixture.Trading/multi-b.json" },
                },
            });

            Assert.Equal(
                "multi-a.json  changedLines=1  edits=1/1\nmulti-b.json  changedLines=1  edits=1/1",
                text);

            Assert.Equal("{ \"probe\": 2 }", await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken));
            Assert.Equal("{ \"probe\": 3 }", await File.ReadAllTextAsync(second, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task EditText_WithMoreEntriesThanOneCallTakes_IsRefusedByName()
    {
        var many = Enumerable
            .Range(0, 26)
            .Select(index => new Dictionary<string, object?> { ["oldText"] = "1", ["newText"] = index.ToString(CultureInfo.InvariantCulture) })
            .ToArray();

        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/multi-a.json",
            ["edits"] = many,
        });

        Assert.Contains("26 entries, at most 25 are applied in one call", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithMoreEntriesForOneFileThanOneWriteTakes_NamesTheFile()
    {
        var many = Enumerable
            .Range(0, 11)
            .Select(index => new Dictionary<string, object?>
            {
                ["oldText"] = "1",
                ["newText"] = index.ToString(CultureInfo.InvariantCulture),
                ["path"] = "src/Fixture.Trading/multi-b.json",
            })
            .ToArray();

        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/multi-a.json",
            ["edits"] = many,
        });

        Assert.Contains("11 entries for src/Fixture.Trading/multi-b.json", text, StringComparison.Ordinal);
        Assert.Contains("at most 10 per file", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithSeveralFiles_WritesThemAllAndAnswersOneLinePerFile()
    {
        var first = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "batch-a.json");
        var second = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "batch-b.json");

        try
        {
            var text = await server.CallAsync("write_text", new()
            {
                ["files"] = new object[]
                {
                new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/batch-a.json", ["content"] = "{ \"a\": 1 }" },
                new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/batch-b.json", ["content"] = "{ \"b\": 2 }" },
                },
            });

            Assert.Equal("batch-a.json  changedLines=1\nbatch-b.json  changedLines=1", text);
            Assert.Equal("{ \"a\": 1 }", await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken));
            Assert.Equal("{ \"b\": 2 }", await File.ReadAllTextAsync(second, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public async Task WriteText_WithTwoInterdependentCSharpFiles_LandsThemUnderOneCompileGate()
    {
        var callee = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "GateCallee.cs");
        var caller = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "GateCaller.cs");

        try
        {
            var text = await server.CallAsync("write_text", new()
            {
                ["force"] = true,
                ["files"] = new object[]
                {
                new Dictionary<string, object?>
                {
                    ["path"] = "src/Fixture.Trading/GateCaller.cs",
                    ["content"] = "namespace Fixture.Trading;\n\npublic static class GateCaller\n{\n    public static int Call() => GateCallee.Answer();\n}\n",
                },
                new Dictionary<string, object?>
                {
                    ["path"] = "src/Fixture.Trading/GateCallee.cs",
                    ["content"] = "namespace Fixture.Trading;\n\npublic static class GateCallee\n{\n    public static int Answer() => 42;\n}\n",
                },
                },
            });

            Assert.False(text.Contains("ERROR", StringComparison.Ordinal), "write_text answered an error, in full: " + text);
            Assert.Contains("GateCaller.cs", text, StringComparison.Ordinal);
            Assert.Contains("GateCallee.cs", text, StringComparison.Ordinal);
            Assert.True(File.Exists(callee));
            Assert.True(File.Exists(caller));
        }
        finally
        {
            File.Delete(callee);
            File.Delete(caller);
        }
    }

    [Fact]
    public async Task WriteText_WithFilesAndATopLevelPath_IsRefusedRatherThanDroppingOne()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/batch-a.json",
            ["files"] = new object[] { new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/batch-b.json", ["content"] = "{}" } },
        });

        Assert.Contains("would have been silently dropped", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithNeitherPathNorFiles_NamesBoth()
    {
        var text = await server.CallAsync("write_text", []);

        Assert.Contains("path", text, StringComparison.Ordinal);
        Assert.Contains("files", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithSeveralProjects_RunsThemAllUnderOneVerdictLine()
    {
        var batched = await StartedAsync("SelectionSolution");

        try
        {
            var once = await batched.CallAsync(
                "run_tests",
                new() { ["project"] = "Selection.Core.Tests", ["timeoutSeconds"] = 300 },
                TestContext.Current.CancellationToken);

            var several = await batched.CallAsync(
                "run_tests",
                new() { ["projects"] = new[] { "Selection.Core.Tests", "Selection.Other.Tests" }, ["timeoutSeconds"] = 300 },
                TestContext.Current.CancellationToken);

            Assert.StartsWith("run_tests PASSED", once, StringComparison.Ordinal);
            Assert.StartsWith("run_tests PASSED", several, StringComparison.Ordinal);
            Assert.Contains("total=1", once, StringComparison.Ordinal);
            Assert.Contains("total=2", several, StringComparison.Ordinal);
            Assert.Contains("Selection.Core.Tests:1", several, StringComparison.Ordinal);
            Assert.Contains("Selection.Other.Tests:1", several, StringComparison.Ordinal);
            Assert.DoesNotContain("timed out", several, StringComparison.Ordinal);
        }
        finally
        {
            await batched.StopAsync();
        }
    }

    [Fact]
    public async Task RunTests_WithProjectsAndProject_IsRefusedRatherThanDroppingOne()
    {
        var text = await server.CallAsync("run_tests", new()
        {
            ["project"] = "Fixture.Trading.Tests",
            ["projects"] = new[] { "Fixture.Trading.Tests" },
        });

        Assert.Contains("would have been silently dropped", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAProjectNameThatMatchesNothing_NamesTheClosestInsteadOfRunningNothing()
    {
        var text = await server.CallAsync("run_tests", new() { ["projects"] = new[] { "Fixture.Trading.Tests", "Nope.Tests" } });

        Assert.Contains("ProjectNotFound", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithABlankProjectEntry_IsRefusedByName()
    {
        var text = await server.CallAsync("run_tests", new() { ["projects"] = new[] { "Fixture.Trading.Tests", "" } });

        Assert.Contains("'projects' carries a blank entry", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithSeveralPaths_OutlinesThemAllInOneResponse()
    {
        string[] paths = ["src/Fixture.Trading/OrderService.cs", "src/Fixture.Trading/OrderBook.cs", "src/Fixture.Trading/OrderSide.cs"];
        var batched = await server.CallAsync("get_file_outline", new() { ["paths"] = paths });
        var separate = 0;

        foreach (var path in paths)
            separate += ToolCensus.Tokens(await server.CallAsync("get_file_outline", new() { ["path"] = path }));

        Assert.StartsWith("3 files", batched, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_FOUND", batched, StringComparison.Ordinal);
        Assert.All(paths, path => Assert.Contains(path, batched, StringComparison.Ordinal));
        Assert.True(
            ToolCensus.Tokens(batched) <= separate + (20 * paths.Length),
            string.Create(CultureInfo.InvariantCulture, $"batched={ToolCensus.Tokens(batched)} separate={separate} over {paths.Length} files"));
    }

    [Fact]
    public async Task GetFileOutline_WithAPathThatDoesNotResolve_ReportsItInlineInsteadOfFailingTheCall()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["paths"] = new[] { "src/Fixture.Trading/OrderSide.cs", "src/Fixture.Trading/NoSuchType.cs" },
        });

        Assert.StartsWith("2 files", text, StringComparison.Ordinal);
        Assert.Contains("NOT_FOUND src/Fixture.Trading/NoSuchType.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderSide", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithContains_KeepsTheMatchingMembersAndSaysHowManyItDropped()
    {
        var whole = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });
        var filtered = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["contains"] = "Total",
        });

        Assert.Contains("TotalVolume", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBook.Add", filtered, StringComparison.Ordinal);
        Assert.Contains(" of ", filtered, StringComparison.Ordinal);
        Assert.Contains(" members", filtered, StringComparison.Ordinal);
        Assert.True(
            ToolCensus.Tokens(filtered) * 2 < ToolCensus.Tokens(whole),
            string.Create(CultureInfo.InvariantCulture, $"filtered={ToolCensus.Tokens(filtered)} whole={ToolCensus.Tokens(whole)}\n{filtered}"));
    }

    [Fact]
    public async Task GetTypeOutline_WithContains_KeepsTheMatchingMembersOnly()
    {
        var text = await server.CallAsync("get_type_outline", new()
        {
            ["symbolId"] = "T:Fixture.Trading.OrderBook",
            ["contains"] = "Total",
        });

        Assert.Contains("TotalVolume", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBook.Add", text, StringComparison.Ordinal);
        Assert.Contains(" members", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffText_WithSeveralPaths_ScopesOneDiffToThemAll()
    {
        var scoped = await server.CallAsync("diff_text", new()
        {
            ["paths"] = new[] { "src", "tests" },
            ["baseRef"] = "HEAD",
            ["maxLines"] = 20,
        });

        var refused = await server.CallAsync("diff_text", new() { ["paths"] = new[] { "src", "" } });

        Assert.DoesNotContain("ERROR", scoped, StringComparison.Ordinal);
        Assert.Contains("lines", scoped, StringComparison.Ordinal);
        Assert.Contains("'paths' carries a blank entry", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithANameThatIsBothATypeAndAMember_ResolvesTheTypeInsteadOfRefusing()
    {
        var added = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderSide",
            ["declaration"] = "Hold",
            ["dryRun"] = true,
        });

        var read = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderSide" });

        Assert.DoesNotContain("AmbiguousSymbol", added, StringComparison.Ordinal);
        Assert.Contains("Hold", added, StringComparison.Ordinal);
        Assert.Contains("AmbiguousSymbol", read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithANameNoTypeCarries_SaysSoAndCountsTheNonTypeMatches()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "Flip",
            ["declaration"] = "public int Extra { get; }",
            ["dryRun"] = true,
        });

        Assert.Contains("names no type", text, StringComparison.Ordinal);
        Assert.Contains("non-type symbol(s) also match this name", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondConsecutiveCallOfTheSameTool_CarriesTheImperativeSteerThatNamesItsPluralParameter()
    {
        await server.CallAsync("workspace_status", []);

        var first = await server.CallRawAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs" });
        var second = await server.CallRawAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs" });
        var batched = await server.CallRawAsync("get_file_outline", new() { ["paths"] = new[] { "src/Fixture.Trading/OrderSide.cs" } });

        Assert.DoesNotContain("calls in a row", first, StringComparison.Ordinal);
        Assert.Contains("2 get_file_outline calls in a row - pass paths=[...] with the next 2+ in ONE call", second, StringComparison.Ordinal);
        Assert.DoesNotContain("calls in a row", batched, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSuiteAttributesItsOwnWallClockToServerStartsAndToolCalls()
    {
        await server.CallAsync("workspace_status", []);

        var report = E2ETelemetry.Report();

        TestContext.Current.TestOutputHelper?.WriteLine(report);

        Assert.Contains("starts=", report, StringComparison.Ordinal);
        Assert.Contains("startMs=", report, StringComparison.Ordinal);
        Assert.Contains("callMs=", report, StringComparison.Ordinal);
        Assert.True(E2ETelemetry.Starts > 0, report);
        Assert.True(E2ETelemetry.Calls > 0, report);
    }

    [Fact]
    public async Task ReplaceSymbol_WithADeclarationReadBackFromGetSymbolSource_ChangesNothing()
    {
        var source = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "OrderBook.For" });
        var body = source
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("public", StringComparison.Ordinal))
            .TakeWhile(line => !line.StartsWith("compilations=", StringComparison.Ordinal))
            .ToArray();

        var declaration = string.Join('\n', body).TrimEnd('\n');

        var diff = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "OrderBook.For",
            ["declaration"] = declaration,
            ["dryRun"] = true,
        });

        Assert.StartsWith("public IReadOnlyList<Order> For", declaration, StringComparison.Ordinal);
        Assert.Contains("\n    bySymbol.TryGetValue", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("@@", diff, StringComparison.Ordinal);
        Assert.Contains("0 files changed", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithTwoExistingDocuments_RunsOneCompileGateOverBothInsteadOfOnePerFile()
    {
        var caller = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs", ["verbose"] = true });

        Assert.DoesNotContain("ERROR", caller, StringComparison.Ordinal);

        var text = await server.CallAsync("write_text", new()
        {
            ["force"] = true,
            ["dryRun"] = true,
            ["files"] = new object[]
            {
            new Dictionary<string, object?>
            {
                ["path"] = "src/Fixture.Trading/SideHolder.cs",
                ["content"] = "namespace Fixture.Trading;\n\npublic sealed class SideHolder\n{\n    public OrderSide OrderSide { get; init; }\n\n    public OrderSide Flip() => OrderSide is OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;\n\n    public string Describe() => SideNames.Of(OrderSide);\n}\n",
            },
            new Dictionary<string, object?>
            {
                ["path"] = "src/Fixture.Trading/OrderSide.cs",
                ["content"] = "namespace Fixture.Trading;\n\npublic enum OrderSide\n{\n    Buy,\n    Sell,\n}\n\npublic static class SideNames\n{\n    public static string Of(OrderSide side) => side.ToString();\n}\n",
            },
            },
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("SideHolder.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderSide.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WhenOneFileOfTheBatchBreaksTheBuild_RollsBackTheWholeBatch()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["force"] = true,
            ["dryRun"] = true,
            ["files"] = new object[]
            {
            new Dictionary<string, object?>
            {
                ["path"] = "src/Fixture.Trading/SideHolder.cs",
                ["content"] = "namespace Fixture.Trading;\n\npublic sealed class SideHolder\n{\n    public OrderSide OrderSide { get; init; }\n\n    public string Describe() => NothingDeclaresThis.Of(OrderSide);\n}\n",
            },
            },
        });

        Assert.Contains("would be rolled back", text, StringComparison.Ordinal);
        Assert.Contains("SideHolder.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithMoreProjectsThanOneCallTakes_IsRefusedByName()
    {
        var text = await server.CallAsync("run_tests", new()
        {
            ["projects"] = Enumerable.Repeat("Fixture.Trading.Tests", 11).ToArray(),
        });

        Assert.Contains("11 entries, at most 10 run in one call", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithAnEmptyContentEntry_IsRefusedExactlyLikeTheSingleFileWrite()
    {
        var batch = await server.CallAsync("write_text", new()
        {
            ["files"] = new object[]
            {
            new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/batch-a.json", ["content"] = "{}" },
            new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/Fixture.Trading.csproj", ["content"] = "" },
            },
        });

        var allowed = await server.CallAsync("write_text", new()
        {
            ["allowEmpty"] = true,
            ["dryRun"] = true,
            ["files"] = new object[]
            {
            new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/Fixture.Trading.csproj", ["content"] = "" },
            },
        });

        Assert.Contains("entry 2", batch, StringComparison.Ordinal);
        Assert.Contains("is empty, which would truncate the file", batch, StringComparison.Ordinal);
        Assert.Contains("allowEmpty=true", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", allowed, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "batch-a.json")));
    }

    [Fact]
    public async Task ReadText_WhenEveryPathFitsTheBudget_DoesNotClaimItRanOut()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["paths"] = new[] { "wide-lines.json", "src/Fixture.Trading/Views/OrderView.xaml" },
            ["maxLines"] = 2,
        });

        Assert.StartsWith("2 files", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT_FOUND", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the shared maxChars budget ran out", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASteeredResponse_StaysWithinItsBudgetWithTheSteerCounted()
    {
        await server.CallAsync("workspace_status", []);

        var steered = string.Empty;

        for (var call = 0; call < 3; call++)
            steered = await server.CallRawAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderSide.cs" });

        var bare = ToolCensus.WithoutSteer(steered);

        Assert.Contains("calls in a row", steered, StringComparison.Ordinal);
        Assert.True(
            ToolCensus.Tokens(steered) - ToolCensus.Tokens(bare) <= 20,
            string.Create(CultureInfo.InvariantCulture, $"the steer cost {ToolCensus.Tokens(steered) - ToolCensus.Tokens(bare)} tokens\n{steered}"));
        Assert.True(ToolCensus.Tokens(steered) <= ToolCensus.GlobalTokenCap, steered);
    }

    [Fact]
    public async Task RunTests_WithParallelOne_StopsTheBatchAtTheFirstTimeoutAndNamesWhatProducedNoResults()
    {
        var hanging = await StartedAsync("HangSolution");

        try
        {
            var built = await hanging.CallAsync("build", [], TestContext.Current.CancellationToken);

            var batch = await hanging.CallAsync(
                "run_tests",
                new() { ["projects"] = new[] { "Hang.Tests", "Hang.Second.Tests" }, ["parallel"] = 1, ["noBuild"] = true, ["timeoutSeconds"] = 40 },
                TestContext.Current.CancellationToken);

            var alone = await hanging.CallAsync(
                "run_tests",
                new() { ["project"] = "Hang.Tests", ["noBuild"] = true, ["timeoutSeconds"] = 40 },
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain("ERROR", built, StringComparison.Ordinal);
            Assert.DoesNotContain("run_tests PASSED", batch, StringComparison.Ordinal);
            Assert.Contains("the batch stopped at the first project that timed out; 2 of 2 project(s) produced no results", batch, StringComparison.Ordinal);
            Assert.Contains("Hang.Tests", batch, StringComparison.Ordinal);
            Assert.DoesNotContain("Hang.Second.Tests.SecondHangingTests.AlsoNeverFinishes", batch, StringComparison.Ordinal);
            Assert.DoesNotContain("batch", alone, StringComparison.Ordinal);
            Assert.Contains("this run timed out and produced no results", alone, StringComparison.Ordinal);
        }
        finally
        {
            await hanging.StopAsync();
        }
    }

    [Fact]
    public async Task SearchText_OverSeveralFiles_EndsWithAPasteReadyPathsArgument()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "Submit",
            ["glob"] = "**/*.cs",
        });

        var batch = text.Split('\n').Single(line => line.StartsWith("paths=[", StringComparison.Ordinal));

        Assert.Contains("\"src", batch, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\"", batch.Replace("\\\\", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.EndsWith("]", batch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_AcrossFiles_EndsWithAPasteReadyPathsArgument()
    {
        var text = await server.CallAsync("find_usages", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)" });

        Assert.Contains("paths=[\"src", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedFiles_AndFindFiles_EndWithAPasteReadyPathsArgument()
    {
        var files = await server.CallAsync("find_files", new() { ["glob"] = "src/Fixture.Trading/*.cs" });

        Assert.Contains("paths=[\"src", files, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_OfANarrowFile_EndsWithAPasteReadySymbolIdsArgument()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        var batch = text.Split('\n').Single(line => line.StartsWith("symbolIds=[", StringComparison.Ordinal));
        var entries = batch["symbolIds=[".Length..^1].Split(',').Select(entry => entry.Trim('"')).ToArray();

        Assert.Contains("OrderService.Submit", entries);
        Assert.Contains("OrderService.SubmitTwice", entries);
        Assert.Contains("M:Fixture.Trading.OrderService.#ctor(Fixture.Trading.IOrderRepository)", entries);

        var resolved = await server.CallAsync("get_symbol_source", new() { ["symbolIds"] = entries });

        Assert.DoesNotContain("NOT_RESOLVED", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_OfAWideFile_OffersContainsRatherThanAnArbitraryTenOfItsMembers()
    {
        var text = await server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/ProbeSaturation.cs" });

        Assert.Contains(" members - narrow with contains=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("symbolIds=[", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithVerbose_CarriesTheDoctorSelfChecksSoDiagnosingTheServerNeedsNoShellOut()
    {
        var quiet = await server.CallAsync("workspace_status", []);
        var loud = await server.CallAsync("workspace_status", new() { ["verbose"] = true });
        var lines = loud.TrimEnd().Split('\n');
        var version = Array.FindIndex(lines, line => line.StartsWith("terse=", StringComparison.Ordinal));

        foreach (var check in new[] { "roslyn:", "assets:", "guard coverage:", "phases:" })
        {
            Assert.Contains(check, loud, StringComparison.Ordinal);
            Assert.DoesNotContain(check, quiet, StringComparison.Ordinal);
            Assert.InRange(Array.FindIndex(lines, line => line.Contains(check, StringComparison.Ordinal)), 0, version - 1);
        }

        Assert.Contains("widest=", loud, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithSectionAndAPlace_WritesInsideTheSectionInsteadOfReplacingIt()
    {
        var full = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "section-probe.md");

        await File.WriteAllTextAsync(full, "# Title\n\n## Open\n\n- first\n\n## Closed\n\n- done\n", TestContext.Current.CancellationToken);

        try
        {
            await server.CallAsync("edit_text", new()
            {
                ["path"] = "src/Fixture.Trading/section-probe.md",
                ["section"] = "## Open",
                ["place"] = "append",
                ["newText"] = "- last",
            });

            await server.CallAsync("edit_text", new()
            {
                ["path"] = "src/Fixture.Trading/section-probe.md",
                ["section"] = "## Open",
                ["place"] = "prepend",
                ["newText"] = "- zero",
            });

            var after = await File.ReadAllTextAsync(full, TestContext.Current.CancellationToken);

            Assert.Equal(
                "# Title\n\n## Open\n- zero\n\n- first\n- last\n\n## Closed\n\n- done\n",
                after.ReplaceLineEndings("\n"));
        }
        finally
        {
            File.Delete(full);
        }
    }

    [Fact]
    public async Task EditText_WithAPlaceThatIsNotAPlacementOrWithoutASection_IsRefusedRatherThanSilentlyReplacing()
    {
        var unknown = await server.CallAsync("edit_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["section"] = "## Open",
            ["place"] = "after",
            ["newText"] = "x",
            ["dryRun"] = true,
        });

        var loose = await server.CallAsync("edit_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["place"] = "append",
            ["newText"] = "x",
            ["dryRun"] = true,
        });

        Assert.Contains("place=after is not a placement", unknown, StringComparison.Ordinal);
        Assert.Contains("place=append or place=prepend", unknown, StringComparison.Ordinal);
        Assert.Contains("place was passed without a section", loose, StringComparison.Ordinal);
        Assert.Contains("remedy:", loose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_OnACSharpFileThatIsNotADocument_ParsesItFromTextInsteadOfRefusing()
    {
        var full = Path.Combine(TerseServerFixture.FixtureRoot, "outline-probe.cs");

        await File.WriteAllTextAsync(
            full,
            "namespace Probe;\n\npublic sealed class Detached\n{\n    public int Answer() => 42;\n}\n",
            TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("get_file_outline", new() { ["path"] = "outline-probe.cs" });

            Assert.Contains("Detached.Answer", text, StringComparison.Ordinal);
            Assert.Contains("HEURISTIC parsed from the file's own text", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(full);
        }
    }

    [Fact]
    public async Task TheFirstCompileGatedEdit_NamesWhatTheGateDidNotCheckAndNeverRepeatsIt()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: true, TestContext.Current.CancellationToken);

        var first = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "OrderService.Submit",
            ["body"] = "return order.Volume > 0 && repository.Submit(order);",
        });

        var second = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "OrderService.Submit",
            ["body"] = "return order.Volume >= 1 && repository.Submit(order);",
        });

        Assert.Contains("gate=semantic", first, StringComparison.Ordinal);
        Assert.Contains("run build once before you push, not after every edit", first, StringComparison.Ordinal);
        Assert.DoesNotContain("gate=semantic", second, StringComparison.Ordinal);
        Assert.Contains("changedLines=", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithATopLevelPlaceAndEdits_IsRefusedRatherThanDroppingThePlacement()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "wide-sections.txt",
            ["place"] = "append",
            ["edits"] = new object[]
            {
            new Dictionary<string, object?> { ["oldText"] = "a", ["newText"] = "b" },
            },
            ["dryRun"] = true,
        });

        Assert.Contains("top-level oldText, newText, section or place", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVerboseWorkspaceStatus_DoesNotConsumeTheGateNoticeTheNextEditOwes()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: true, TestContext.Current.CancellationToken);

        var status = await solution.CallAsync("workspace_status", new() { ["verbose"] = true });

        var edited = await solution.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "OrderService.Submit",
            ["body"] = "return order.Volume > 0 && repository.Submit(order);",
        });

        Assert.Contains("phases:", status, StringComparison.Ordinal);
        Assert.DoesNotContain("gate=semantic", status, StringComparison.Ordinal);
        Assert.Contains("gate=semantic", edited, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithDepth_FoldsEverythingBelowTheNthSegmentIntoOneRow()
    {
        var flat = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs" });
        var rolled = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs", ["depth"] = 2 });
        var top = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs", ["depth"] = 1 });
        var counted = flat[..flat.IndexOf('\n', StringComparison.Ordinal)];

        Assert.StartsWith(counted, rolled, StringComparison.Ordinal);
        Assert.StartsWith(counted, top, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/**  x25 files", rolled, StringComparison.Ordinal);
        Assert.Contains("src/**  x25 files", top, StringComparison.Ordinal);
        Assert.Contains("DeliberateOutcomesTests.cs", rolled, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", rolled, StringComparison.Ordinal);
        Assert.True(rolled.Length * 4 < flat.Length, rolled);
    }

    [Fact]
    public async Task FindFiles_WithANegativeDepth_IsRefusedNamingTheRange()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs", ["depth"] = -1 });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("depth", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_WhenNoSourceDeclarationMatches_AnswersFromTheReferencedAssemblies()
    {
        var text = await server.CallAsync("search_symbols", new() { ["query"] = "StringBuilder" });

        Assert.Contains("T:System.Text.StringBuilder", text, StringComparison.Ordinal);
        Assert.Contains("from referenced assemblies", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 symbols", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTypeOutline_OnATypeFromAReferencedAssembly_ListsItsMembersInsteadOfFailing()
    {
        var text = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "T:System.Text.StringBuilder" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Append", text, StringComparison.Ordinal);
        Assert.Contains("metadata - no source", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTypeOutline_ByBareName_ResolvesAgainstTheReferencedAssemblies()
    {
        var text = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "StringBuilder" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Append", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbolSource_OnAMemberFromAReferencedAssembly_NamesTheAssemblyInsteadOfClaimingItIsMissing()
    {
        var text = await server.CallAsync("get_symbol_source", new() { ["symbolId"] = "M:System.Text.StringBuilder.AppendLine" });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("metadata - no source", text, StringComparison.Ordinal);
        Assert.Contains("get_type_outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSymbol_OnAMetadataSymbol_NamesTheAssemblyAndVersion()
    {
        var text = await server.CallAsync("get_symbol", new() { ["symbolId"] = "T:System.Text.StringBuilder" });

        Assert.Contains("class public StringBuilder", text, StringComparison.Ordinal);
        Assert.DoesNotContain("at - in", text, StringComparison.Ordinal);
        Assert.Contains("in System.Text", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAdvertisedSchemas_CarryNoDefaultKey()
    {
        var surface = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var carrying = surface
            .Where(tool => tool.JsonSchema.GetRawText().Contains("\"default\"", StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.NotEmpty(surface);
        Assert.Empty(carrying);
        Assert.Contains(surface, tool => tool.JsonSchema.GetRawText().Contains("\"verbose\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetryWith_HoldsTheUsingsAndTheAddedHelpersOfTheRejectedEdit()
    {
        var rejected = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "OrderService.Unused",
            ["declaration"] = "public int Unused() => Helper() + ImmutableArray<int>.Empty.Length + Absent();",
            ["add"] = new[] { "private static int Helper() => 7;" },
            ["usings"] = new[] { "System.Collections.Immutable" },
        });

        Assert.StartsWith("ERROR CompileRegression", rejected, StringComparison.Ordinal);

        var token = Token(rejected);
        var replayed = await server.CallAsync("replace_symbol", new()
        {
            ["retryWith"] = token,
            ["allowErrors"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", replayed, StringComparison.Ordinal);
        Assert.Contains("using System.Collections.Immutable;", replayed, StringComparison.Ordinal);
        Assert.Contains("private static int Helper() => 7;", replayed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryWith_AgainstTheWrongEditToolNamesTheOneThatHoldsIt()
    {
        var rejected = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "OrderService.Unused",
            ["declaration"] = "public int Unused() => Absent();",
        });

        Assert.StartsWith("ERROR CompileRegression", rejected, StringComparison.Ordinal);

        var wrong = await server.CallAsync("replace_symbol_body", new() { ["retryWith"] = Token(rejected) });

        Assert.StartsWith("ERROR InvalidArgument", wrong, StringComparison.Ordinal);
        Assert.Contains("was issued by replace_symbol", wrong, StringComparison.Ordinal);
    }

    private static string Token(string rejection)
    {
        var marker = rejection.IndexOf("retryWith=", StringComparison.Ordinal);

        Assert.True(marker >= 0, rejection);

        var tail = rejection.AsSpan(marker + "retryWith=".Length);
        var end = tail.IndexOfAny(" \r\n");

        return new string(end < 0 ? tail : tail[..end]);
    }

    [Fact]
    public async Task AMalformedDeclaration_QuotesTheTextAroundTheFirstParseError()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "OrderService.Unused",
            ["declaration"] = "public int Unused() => 7 + ;",
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("did not parse", text, StringComparison.Ordinal);
        Assert.Contains("at offset ", text, StringComparison.Ordinal);
        Assert.Contains("public int Unused() => 7 + ;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedEntryOfDeclarations_NamesWhichEntryItCameFrom()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "OrderService.Unused", "OrderService.NeverCalled" },
            ["declarations"] = new[] { "public int Unused() => 7;", "private int NeverCalled( => 42;" },
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("declarations[1]", text, StringComparison.Ordinal);
        Assert.Contains("at offset ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTypeOutline_ByAQualifiedNameNamingTheWrongNamespace_DoesNotAnswerAnotherType()
    {
        var wrong = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "System.Collections.StringBuilder" });
        var right = await server.CallAsync("get_type_outline", new() { ["symbolId"] = "System.Text.StringBuilder" });

        Assert.StartsWith("ERROR", wrong, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", right, StringComparison.Ordinal);
        Assert.Contains("T:System.Text.StringBuilder", right, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_FromMetadata_ReportsWhatItTruncated()
    {
        var all = await server.CallAsync("search_symbols", new() { ["query"] = "Timer" });
        var one = await server.CallAsync("search_symbols", new() { ["query"] = "Timer", ["maxResults"] = 1 });

        Assert.Contains("from referenced assemblies", all, StringComparison.Ordinal);
        Assert.Contains("truncated", one, StringComparison.Ordinal);
        Assert.StartsWith("1/", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMutatingTool_GivenANameOnlyMetadataMatches_AnswersOnTheNameItWasGiven()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "StringBuilder",
            ["body"] = "=> 1;",
            ["dryRun"] = true,
        });

        Assert.StartsWith("ERROR SymbolNotFound", text, StringComparison.Ordinal);
        Assert.Contains("'StringBuilder' did not resolve", text, StringComparison.Ordinal);
        Assert.DoesNotContain("T:System.Text.StringBuilder", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTests_WithAParallelOutsideTheAcceptedRange_IsRejectedWithARemedy()
    {
        var text = await server.CallAsync("run_tests", new() { ["parallel"] = 99 });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("outside the accepted range 0-10", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    private static Task<TerseServerProcess> StartedAsync(string fixture)
    {
        var root = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", fixture);

        return TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, fixture + ".slnx")],
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FindFiles_WithAnAbsoluteRoot_ListsThatDirectoryAndSaysItIsOutsideTheWorkspace()
    {
        var outside = Path.GetDirectoryName(TerseServerFixture.FixtureRoot)!;
        var text = await server.CallAsync("find_files", new()
        {
            ["glob"] = "**/*.slnx",
            ["root"] = outside,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
        Assert.Contains("FixtureSolution.slnx", text, StringComparison.Ordinal);
        Assert.Contains(outside.Replace('\\', '/'), text.Replace("\\\\", "/", StringComparison.Ordinal).Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithARootThatDoesNotExist_SaysSoInsteadOfAnsweringZero()
    {
        var text = await server.CallAsync("find_files", new()
        {
            ["glob"] = "*",
            ["root"] = Path.Combine(Path.GetTempPath(), "terse-no-such-directory-4b7c"),
        });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithRootAndTracked_IsRefusedRatherThanListingWithoutTheFilter()
    {
        var text = await server.CallAsync("find_files", new()
        {
            ["glob"] = "*",
            ["root"] = Path.GetTempPath(),
            ["tracked"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithABatchMixingACSharpFileAndAMarkdownFile_IsAcceptedUnderTheOneForce()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["force"] = true,
            ["dryRun"] = true,
            ["files"] = new object[]
            {
                new Dictionary<string, object?> { ["path"] = "probe-mixed.md", ["content"] = "probe\n" },
                new Dictionary<string, object?>
                {
                    ["path"] = "src/Fixture.Trading/ProbeMixed.cs",
                    ["content"] = "namespace Fixture.Trading;\n\npublic static class ProbeMixed\n{\n    public static int Answer() => 7;\n}\n",
                },
            },
        });

        Assert.False(text.Contains("ERROR", StringComparison.Ordinal), "write_text answered an error, in full: " + text);
        Assert.Contains("probe-mixed.md", text, StringComparison.Ordinal);
        Assert.Contains("ProbeMixed.cs", text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(TerseServerFixture.FixtureRoot, "probe-mixed.md")));
    }

    [Fact]
    public async Task WriteText_WithForceAndAnAbsolutePathOutsideEveryRoot_WritesItAndSaysSo()
    {
        var probe = Path.Combine(Path.GetTempPath(), "terse-outside-probe-i310.cs");

        try
        {
            var refused = await server.CallAsync("write_text", new()
            {
                ["path"] = probe,
                ["content"] = "public static class OutsideProbe;\n",
            });

            Assert.Contains("ERROR OutOfWorkspace", refused, StringComparison.Ordinal);
            Assert.Contains("force=true", refused, StringComparison.Ordinal);

            var written = await server.CallAsync("write_text", new()
            {
                ["path"] = probe,
                ["content"] = "public static class OutsideProbe;\n",
                ["force"] = true,
            });

            Assert.DoesNotContain("ERROR", written, StringComparison.Ordinal);
            Assert.Contains("outside-workspace", written, StringComparison.Ordinal);
            Assert.True(File.Exists(probe));
            Assert.Equal("public static class OutsideProbe;\n", (await File.ReadAllTextAsync(probe, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(probe);
        }
    }

    [Fact]
    public async Task Analyze_WithPaths_CoversEveryNamedFileInOnePass()
    {
        const string First = "src/Fixture.Trading/OrderService.cs";
        const string Second = "src/Fixture.Trading/Awkward.cs";

        var one = await server.CallAsync("analyze", new() { ["path"] = First });
        var two = await server.CallAsync("analyze", new() { ["path"] = Second });
        var both = await server.CallAsync("analyze", new() { ["paths"] = new[] { First, Second } });

        Assert.Contains("OrderService.cs", one, StringComparison.Ordinal);
        Assert.Contains("Awkward.cs", two, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", both, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", both, StringComparison.Ordinal);
        Assert.Contains("Awkward.cs", both, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithAPathsEntryCarryingAComma_IsRefusedRatherThanMisScoped()
    {
        var text = await server.CallAsync("analyze", new() { ["paths"] = new[] { "src/Fixture.Trading/OrderService.cs,src/Fixture.Trading/Awkward.cs" } });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedFiles_WithATrackedModification_EndsWithTheDiffSymbolsCallForIt()
    {
        try
        {
            await server.CallAsync("edit_text", new()
            {
                ["path"] = "notes.md",
                ["oldText"] = "# Fixture notes",
                ["newText"] = "# Fixture notes, momentarily changed",
            });

            var text = await server.CallAsync("changed_files", new() { ["path"] = "notes.md" });

            Assert.Contains("notes.md", text, StringComparison.Ordinal);
            Assert.Contains("next: diff_symbols path=\"notes.md\"", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = "notes.md", ["ref"] = "HEAD" });
        }
    }

    [Fact]
    public async Task ChangedFiles_WithStaged_AnswersTheIndexRatherThanTheWorkingTree()
    {
        try
        {
            await server.CallAsync("write_text", new() { ["path"] = "terse-staged-probe.txt", ["content"] = "probe\n" });
            await server.CallAsync("edit_text", new()
            {
                ["path"] = "notes.md",
                ["oldText"] = "# Fixture notes",
                ["newText"] = "# Fixture notes, momentarily changed",
            });

            var working = await server.CallAsync("changed_files", []);
            var staged = await server.CallAsync("changed_files", new() { ["staged"] = true });

            Assert.Contains("notes.md", working, StringComparison.Ordinal);
            Assert.Contains("terse-staged-probe.txt", working, StringComparison.Ordinal);

            Assert.DoesNotContain("ERROR", staged, StringComparison.Ordinal);
            Assert.DoesNotContain("notes.md", staged, StringComparison.Ordinal);
            Assert.DoesNotContain("terse-staged-probe.txt", staged, StringComparison.Ordinal);
            Assert.DoesNotContain("next: diff_symbols", staged, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = "terse-staged-probe.txt", ["delete"] = true });
            await server.CallAsync("write_text", new() { ["path"] = "notes.md", ["ref"] = "HEAD" });
        }
    }

    [Fact]
    public async Task ChangedFiles_WithUntrackedFalse_DropsTheFilesGitDoesNotTrack()
    {
        try
        {
            await server.CallAsync("write_text", new() { ["path"] = "terse-untracked-probe.txt", ["content"] = "probe\n" });

            var all = await server.CallAsync("changed_files", []);
            var tracked = await server.CallAsync("changed_files", new() { ["untracked"] = false });

            Assert.Contains("terse-untracked-probe.txt", all, StringComparison.Ordinal);
            Assert.DoesNotContain("terse-untracked-probe.txt", tracked, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = "terse-untracked-probe.txt", ["delete"] = true });
        }
    }

    [Fact]
    public async Task ReadText_OnAWholeMarkdownFile_NamesTheSectionsThatAddressItInsteadOfAnAnchor()
    {
        var whole = await server.CallAsync("read_text", new() { ["path"] = "notes.md" });
        var ranged = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["startLine"] = 1, ["endLine"] = 5 });

        Assert.Contains("sections=3", whole, StringComparison.Ordinal);
        Assert.Contains("## Open", whole, StringComparison.Ordinal);
        Assert.DoesNotContain("sections=", ranged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_Verbose_ReportsHowMuchMemoryTheLiveServersHold()
    {
        var verbose = await server.CallAsync("workspace_status", new() { ["verbose"] = true });
        var quiet = await server.CallAsync("workspace_status", []);

        Assert.Contains("live terse server(s) holding", verbose, StringComparison.Ordinal);
        Assert.DoesNotContain("this server 0MB", verbose, StringComparison.Ordinal);
        Assert.DoesNotContain("holding 0MB", verbose, StringComparison.Ordinal);
        Assert.DoesNotContain("live terse server(s) holding", quiet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WhenTheTypeAlreadyDeclaresThatSignature_IsRefusedBeforeAnythingIsCompiled()
    {
        var taken = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderService",
            ["declaration"] = "public int Unused() => 7;",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR NameTaken", taken, StringComparison.Ordinal);
        Assert.Contains("Unused()", taken, StringComparison.Ordinal);
        Assert.Contains("remedy:", taken, StringComparison.Ordinal);
        Assert.DoesNotContain("CompileRegression", taken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAnOverloadNoExistingMemberDeclares_StillLands()
    {
        var overloaded = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderService",
            ["declaration"] = "public bool Submit(Order order, decimal limit) => Submit(order);",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", overloaded, StringComparison.Ordinal);
        Assert.Contains("dryRun", overloaded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithPathsNamingADirectory_CoversEveryFileUnderIt()
    {
        const string Directory = "src/Fixture.Trading";
        const string Elsewhere = "src/Fixture.Trading/Views/OrderViewModel.cs";

        var elsewhereOnly = await server.CallAsync("analyze", new() { ["path"] = Elsewhere });
        var both = await server.CallAsync("analyze", new() { ["paths"] = new[] { Elsewhere, Directory } });

        Assert.DoesNotContain("ERROR", elsewhereOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", elsewhereOnly, StringComparison.Ordinal);

        Assert.DoesNotContain("ERROR", both, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", both, StringComparison.Ordinal);
    }
}
