namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class FileToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ReadText_WithALineRange_ReturnsOnlyThoseLines()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 2,
            ["endLine"] = 3,
        });

        var lines = text.Split('\n');

        Assert.Equal("2/5 lines truncated", lines[0]);
        Assert.StartsWith("2: ", lines[1], StringComparison.Ordinal);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain("3: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1: {", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnAWholeFileWithBlankLines_NeverClaimsItTruncated()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderBook.cs",
            ["startLine"] = 1,
        });

        Assert.DoesNotContain("truncated", text, StringComparison.Ordinal);
        Assert.Contains("namespace Fixture.Trading;", text, StringComparison.Ordinal);
        Assert.Contains("TotalVolume", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_MatchesTheGlobAndExcludesBuildOutput()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "*.csproj" });

        Assert.Contains("Fixture.Trading.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("obj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithADirectoryGlob_MatchesOnTheRelativePath()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/Views/*.xaml" });

        Assert.Contains("OrderView.xaml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Order.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithADirectoryGlobThatMatchesNothing_ReportsNoneRatherThanFailing()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/NoSuchFolder/*.cs" });

        Assert.Contains("0 files", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithADirectoryGlob_SearchesOnlyThatSubtree()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["pattern"] = "Button",
            ["glob"] = "**/Views/OrderView.xaml",
        });

        Assert.Contains("OrderView.xaml", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_TagsResultsHeuristic()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "MaxVolume", ["glob"] = "*.json" });

        Assert.Contains("HEURISTIC", text, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_MatchesAPattern()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = @"public\s+sealed\s+record", ["glob"] = "*.cs" });

        Assert.Contains("Order.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_LocatesABinaryFile()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "*.png" });

        Assert.Contains("logo.png", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0 files", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_SkipsBinaryFilesThatFindFilesStillLists()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "PNG" });

        Assert.DoesNotContain("logo.png", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_ForAMissingFile_ReturnsDocumentNotFound()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "nope.json" });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_AskedTwice_AnswersTheSecondCallFromThePathIndex()
    {
        await server.CallAsync("find_files", new() { ["glob"] = "*.csproj" });

        var before = PathIndexHits(await server.CallAsync("workspace_status", new() { ["verbose"] = true }));

        await server.CallAsync("find_files", new() { ["glob"] = "*.json" });

        Assert.Equal(before + 1, PathIndexHits(await server.CallAsync("workspace_status", new() { ["verbose"] = true })));
    }

    private static int PathIndexHits(string status)
    {
        const string Marker = "paths(hit=";

        Assert.Contains(Marker, status, StringComparison.Ordinal);

        var counter = status.AsSpan(status.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length);

        return int.Parse(counter[..counter.IndexOf(' ')], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task FindFiles_WithStamps_AppendsAUtcWriteTimeAndByteLengthPerFile()
    {
        var plain = await server.CallAsync("find_files", new() { ["glob"] = "*.csproj" });
        var stamped = await server.CallAsync("find_files", new() { ["glob"] = "*.csproj", ["stamps"] = true });

        Assert.DoesNotContain("Z  ", plain, StringComparison.Ordinal);
        var line = Assert.Single(
            stamped.Split('\n'),
            candidate => candidate.Contains("Fixture.Trading.csproj", StringComparison.Ordinal));
        var columns = line.Split("  ", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, columns.Length);
        Assert.EndsWith("Fixture.Trading.csproj", columns[0], StringComparison.Ordinal);
        Assert.True(DateTime.TryParseExact(
            columns[1],
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out _));
        Assert.True(int.TryParse(columns[2], NumberStyles.None, CultureInfo.InvariantCulture, out var bytes));
        Assert.True(bytes > 0);
    }

    [Fact]
    public async Task SearchText_WithExclude_DropsTheMatchesTheGlobCannotLeaveOut()
    {
        var all = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "**/*.cs" });
        var kept = await server.CallAsync("search_text", new()
        {
            ["query"] = "OrderService",
            ["glob"] = "**/*.cs",
            ["exclude"] = "**/OrderRouter.cs",
        });

        Assert.Contains("5 matches", all, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", all, StringComparison.Ordinal);
        Assert.Contains("3 matches", kept, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderRouter.cs", kept, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnAWholeCsFile_AnswersTheOutlineAndNamesTheOptInForTheText()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        Assert.Contains("OrderService.Submit", text, StringComparison.Ordinal);
        Assert.Contains("read_text verbose=true for the raw text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("repository.Submit(order)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnACsFileWithVerbose_StillReturnsTheText()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["verbose"] = true,
        });

        Assert.Contains("repository.Submit(order)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this is the outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnACsFileWithALineRange_StillReturnsThoseLines()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["startLine"] = 11,
            ["endLine"] = 11,
        });

        Assert.Contains("repository.Submit(order)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this is the outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_OnACsFileThatIsNotAWorkspaceDocument_StillReturnsItsText()
    {
        const string Loose = "terse-loose-file.cs";
        await server.CallAsync("write_text", new()
        {
            ["path"] = Loose,
            ["content"] = "// belongs to no project\nclass Loose;\n",
            ["force"] = true,
        });
        try
        {
            var text = await server.CallAsync("read_text", new() { ["path"] = Loose });

            Assert.Contains("belongs to no project", text, StringComparison.Ordinal);
            Assert.DoesNotContain("this is the outline", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Loose, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task ReadText_OnANonCsFile_IsUnaffected()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "appsettings.json" });

        Assert.Contains("MaxVolume", text, StringComparison.Ordinal);
        Assert.DoesNotContain("this is the outline", text, StringComparison.Ordinal);
    }
}
