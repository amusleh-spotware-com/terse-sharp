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

        Assert.Equal("2 lines", lines[0]);
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
    public async Task SearchText_TagsTheResponseHeuristicOnceInsteadOfEveryRecord()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "Order", ["glob"] = "*.cs" });

        var tagged = text.Split('\n').Count(line => line.Contains("HEURISTIC", StringComparison.Ordinal));
        var records = text.Split('\n').Count(line => line.Contains(".cs:", StringComparison.Ordinal));

        Assert.Equal(1, tagged);
        Assert.True(records > 1, text);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
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
            candidate => candidate.Contains("Fixture.Trading.csproj", StringComparison.Ordinal)
                && !candidate.StartsWith("paths=[", StringComparison.Ordinal));
        var columns = line.Split("  ", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, columns.Length);
        Assert.EndsWith("Fixture.Trading.csproj", columns[0], StringComparison.Ordinal);
        Assert.True(DateTime.TryParseExact(
            columns[1],
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out _));
        Assert.True(long.Parse(columns[2], CultureInfo.InvariantCulture) > 0);
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

    [Fact]
    public async Task FindFiles_WithTracked_ListsOnlyWhatGitTracks()
    {
        const string Probe = "terse-untracked-probe.txt";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "probe\n" });
        try
        {
            var loose = await server.CallAsync("find_files", new() { ["glob"] = "**/*.txt" });
            var trackedText = await server.CallAsync("find_files", new() { ["glob"] = "**/*.txt", ["tracked"] = true });
            var trackedCode = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs", ["tracked"] = true });

            Assert.Contains(Probe, loose, StringComparison.Ordinal);
            Assert.DoesNotContain(Probe, trackedText, StringComparison.Ordinal);
            Assert.Contains("OrderService.cs", trackedCode, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", trackedCode, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task ReadText_WhenAMarkdownReadIsClipped_NamesHeadingsAndSectionBesideTheNextLine()
    {
        const string Probe = "terse-long-probe.md";
        var content = string.Join(
            "\n",
            Enumerable.Range(0, 400).Select(index => index % 50 is 0
                ? "## Section " + index.ToString(CultureInfo.InvariantCulture)
                : "body line " + index.ToString(CultureInfo.InvariantCulture)));

        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = content });
        try
        {
            var clipped = await server.CallAsync("read_text", new() { ["path"] = Probe, ["maxLines"] = 20 });

            Assert.Contains("next: startLine=21", clipped, StringComparison.Ordinal);
            Assert.Contains("headings=true", clipped, StringComparison.Ordinal);
            Assert.Contains("section=", clipped, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task SearchRegex_WithAPatternThatCanStartOnTheBlankLineAbove_LeavesNoRecordWithoutAPayload()
    {
        var text = await server.CallAsync("search_regex", new()
        {
            ["pattern"] = @"^\s*(public|internal|private)\s+[\w<>\[\],\? ]+\s+(\w+)\s*\(",
            ["glob"] = "src/**/*.cs",
            ["maxResults"] = 200,
        });

        var records = text.Split('\n').Where(Record).ToArray();

        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.NotEqual(string.Empty, Payload(record)));
    }

    private static bool Record(string line) => line.Contains(".cs:", StringComparison.Ordinal);

    private const string RecordSeparator = "  ";

    private static string Payload(string record) =>
        record[(record.IndexOf(RecordSeparator, StringComparison.Ordinal) + RecordSeparator.Length)..].Trim();

    [Fact]
    public async Task SearchRegex_WithMatchesOnly_PrintsTheMatchedSpanInsteadOfTheWholeLine()
    {
        var lines = await server.CallAsync("search_regex", new()
        {
            ["pattern"] = @"public\s+sealed\s+record\s+\w+",
            ["glob"] = "src/**/*.cs",
        });

        var spans = await server.CallAsync("search_regex", new()
        {
            ["pattern"] = @"public\s+sealed\s+record\s+\w+",
            ["glob"] = "src/**/*.cs",
            ["matchesOnly"] = true,
        });

        var first = spans.Split('\n').First(Record);

        Assert.Contains("record Order", spans, StringComparison.Ordinal);
        Assert.DoesNotContain("(", Payload(first), StringComparison.Ordinal);
        Assert.Contains("(", lines, StringComparison.Ordinal);
        Assert.True(spans.Length < lines.Length, spans);
    }

    [Fact]
    public async Task SearchText_WithMatchesOnlyAndUnique_CollapsesTheDistinctMatchedValues()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["query"] = "public",
            ["glob"] = "src/**/*.cs",
            ["matchesOnly"] = true,
            ["unique"] = true,
        });

        var records = text.Split('\n').Where(Record).ToArray();

        Assert.Single(records);
        Assert.Equal("public", Payload(records[0]).Split("  x")[0]);
    }

    [Fact]
    public async Task SearchText_WithSeveralQueries_TagsEveryRecordWithTheQueryThatMatchedIt()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "TotalVolume", "namespace Fixture.Trading" },
            ["glob"] = "src/Fixture.Trading/OrderBook.cs",
        });

        var lines = text.Split('\n');

        Assert.Contains(lines, line => line.Contains("OrderBook.cs:33", StringComparison.Ordinal) && line.Contains("  q1  ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("OrderBook.cs:1", StringComparison.Ordinal) && line.Contains("  q2  ", StringComparison.Ordinal));
        Assert.DoesNotContain("q1=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithASingleQueriesEntry_IsByteIdenticalToTheQueryForm()
    {
        var batched = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "Order" },
            ["glob"] = "*.cs",
        });

        var single = await server.CallAsync("search_text", new()
        {
            ["query"] = "Order",
            ["glob"] = "*.cs",
        });

        Assert.Equal(single, batched);
        Assert.DoesNotContain("q1=", batched, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithMoreQueriesThanOnePassAnswers_IsRefusedNamingTheCap()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["queries"] = Enumerable.Range(0, 11).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray(),
        });

        var withQuery = await server.CallAsync("search_text", new()
        {
            ["query"] = "alpha",
            ["queries"] = Enumerable.Range(0, 10).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray(),
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("11 patterns were requested - query plus queries", text, StringComparison.Ordinal);
        Assert.Contains("11 patterns were requested - query plus queries", withQuery, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithSeveralQueries_ReportsBothPatternsInOnePass()
    {
        var text = await server.CallAsync("search_regex", new()
        {
            ["queries"] = new[] { @"public\s+sealed\s+record", @"namespace\s+Fixture" },
            ["glob"] = "src/Fixture.Trading/Order.cs",
        });

        Assert.Contains("  q1  ", text, StringComparison.Ordinal);
        Assert.Contains("  q2  ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithABlankQueriesEntry_IsRefusedRatherThanSearchingEverything()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "OrderBook", "" },
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("blank entry", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_OnALargeFileWithTwoFarApartEdits_ReportsTheChangedLinesTheDiffShows()
    {
        const string Relative = "changed-lines-probe.txt";
        var path = Path.Combine(TerseServerFixture.FixtureRoot, Relative);
        var content = string.Join('\n', Enumerable.Range(0, 3000).Select(index => "line " + index.ToString(CultureInfo.InvariantCulture)));

        try
        {
            await server.CallAsync("write_text", new() { ["path"] = Relative, ["content"] = content });

            var text = await server.CallAsync("edit_text", new()
            {
                ["path"] = Relative,
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "\nline 5\n", ["newText"] = "\nline 5 changed\n" },
                new Dictionary<string, object?> { ["oldText"] = "\nline 2500\n", ["newText"] = "\nline 2500 changed\n" },
                },
            });

            var reported = text.Split('\n').Single(line => line.Contains("changedLines=", StringComparison.Ordinal));

            Assert.Equal("2", reported.Split("changedLines=", StringSplitOptions.None)[1].Split(' ')[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SearchText_WithTwoQueriesMatchingTheSameLine_TagsThatRecordWithBothInQueryOrder()
    {
        var separate = await server.CallAsync("search_text", new()
        {
            ["query"] = "OrderService",
            ["glob"] = "src/Fixture.Trading/OrderRouter.cs",
        });

        var batched = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "private readonly", "OrderService" },
            ["glob"] = "src/Fixture.Trading/OrderRouter.cs",
        });

        var swapped = await server.CallAsync("search_text", new()
        {
            ["queries"] = new[] { "OrderService", "private readonly" },
            ["glob"] = "src/Fixture.Trading/OrderRouter.cs",
        });

        var shared = batched.Split('\n').Single(line => line.Contains("OrderRouter.cs:5", StringComparison.Ordinal));
        var sharedSwapped = swapped.Split('\n').Single(line => line.Contains("OrderRouter.cs:5", StringComparison.Ordinal));

        Assert.Contains("OrderRouter.cs:5", separate, StringComparison.Ordinal);
        Assert.Contains("  q1,q2  ", shared, StringComparison.Ordinal);
        Assert.Contains("  q1,q2  ", sharedSwapped, StringComparison.Ordinal);
        Assert.DoesNotContain("q2,q1", swapped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_WithTwoQueriesMatchingTheSameLine_TagsThatRecordWithBoth()
    {
        var text = await server.CallAsync("search_regex", new()
        {
            ["queries"] = new[] { @"public\s+sealed", @"class\s+OrderService" },
            ["glob"] = "src/Fixture.Trading/OrderService.cs",
        });

        Assert.Contains("  q1,q2  ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithBytes_ReportsTheSameByteLengthThatFindFilesStampsDoes()
    {
        const string ProjectPath = "src/Fixture.Trading/Fixture.Trading.csproj";

        var stamped = await server.CallAsync("find_files", new() { ["glob"] = ProjectPath, ["stamps"] = true });
        var read = await server.CallAsync("read_text", new() { ["path"] = ProjectPath, ["bytes"] = true });
        var length = stamped.Split("  ", StringSplitOptions.RemoveEmptyEntries)[^1].Trim();

        Assert.StartsWith("1 files", stamped, StringComparison.Ordinal);
        Assert.True(int.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes));
        Assert.True(bytes > 0);
        Assert.Contains("\nbytes=" + length, read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithBytesOnAWholeCSharpRead_ReportsThemBesideTheOutlineSteer()
    {
        const string SourcePath = "src/Fixture.Trading/OrderService.cs";

        var stamped = await server.CallAsync("find_files", new() { ["glob"] = SourcePath, ["stamps"] = true });
        var text = await server.CallAsync("read_text", new() { ["path"] = SourcePath, ["bytes"] = true });
        var length = stamped.Split("  ", StringSplitOptions.RemoveEmptyEntries)[^1].Trim();

        Assert.Contains("this is the outline", text, StringComparison.Ordinal);
        Assert.Contains("\nbytes=" + length, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithoutBytes_ReportsNoByteLength()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/Fixture.Trading.csproj",
        });

        Assert.DoesNotContain("bytes=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithBytesOverSeveralPaths_ReportsTheLengthOfEachEntry()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["paths"] = new[] { "src/Fixture.Trading/Fixture.Trading.csproj", "tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj" },
            ["bytes"] = true,
        });

        Assert.Equal(2, text.Split('\n').Count(line => line.StartsWith("bytes=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ReadText_WithBytesOnAHeadingMapAndASection_ReportsThemOnBothShapes()
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, "CHANGELOG.md");
        var headings = await server.CallAsync("read_text", new() { ["path"] = path, ["headings"] = true, ["bytes"] = true });
        var section = await server.CallAsync("read_text", new() { ["path"] = path, ["section"] = "## [Unreleased]", ["bytes"] = true });

        Assert.Contains("sections", headings, StringComparison.Ordinal);
        Assert.Matches("\nbytes=[1-9][0-9]*", headings);
        Assert.Matches("\nbytes=[1-9][0-9]*", section);
    }

    [Fact]
    public async Task ReadText_WithBytesOnAnEmptyCSharpDocument_StillReportsBytesZero()
    {
        const string Probe = "src/Fixture.Trading/EmptyProbe.cs";

        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = string.Empty,
            ["allowEmpty"] = true,
            ["force"] = true,
        });

        try
        {
            var text = await server.CallAsync("read_text", new() { ["path"] = Probe, ["bytes"] = true });

            Assert.Contains("this is the outline", text, StringComparison.Ordinal);
            Assert.Contains("\nbytes=0", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task FindFiles_WithNameAlone_MatchesAFileNameSubstringWithNoGlobToGetRight()
    {
        var text = await server.CallAsync("find_files", new() { ["name"] = "orderrouter" });

        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithNameBesideAGlob_FiltersWhatTheGlobSelected()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/*.csproj", ["name"] = "Tests" });

        Assert.Contains("Fixture.Trading.Tests.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fixture.Trading.csproj\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithAGlobThatMatchedNothing_NamesTheNameParameterInstead()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/*.nosuchextension" });

        Assert.StartsWith("0 files", text, StringComparison.Ordinal);
        Assert.Contains("pass name=<text> to match a file name substring instead of a glob", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithNeitherGlobNorName_IsRefusedNamingBoth()
    {
        var text = await server.CallAsync("find_files", []);

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("neither 'glob' nor 'name' was supplied", text, StringComparison.Ordinal);
        Assert.Contains("'name' to match a file name substring", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumns_ProjectsTheMarkdownTableDownToThem()
    {
        var whole = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["verbose"] = true });
        var projected = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Finding" });

        Assert.Contains("rows", projected.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("first row", projected, StringComparison.Ordinal);
        Assert.DoesNotContain("read_text", projected, StringComparison.Ordinal);
        Assert.True(projected.Length * 2 < whole.Length, "columns= did not cost less than the whole file: " + projected);
    }

    [Fact]
    public async Task ReadText_WithAColumnTheTableDoesNotDeclare_NamesTheColumnsItHas()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Severity" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("Finding", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithPathInsteadOfGlob_AnswersTheSameRecords()
    {
        var byGlob = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "**/*.cs" });
        var byPath = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["path"] = "**/*.cs" });

        Assert.DoesNotContain("ERROR", byPath, StringComparison.Ordinal);
        Assert.Equal(byGlob, byPath);
    }

    [Fact]
    public async Task WriteText_WithRef_RestoresTheFileFromThatRevision()
    {
        var original = await server.CallAsync("read_text", new() { ["path"] = "appsettings.json", ["verbose"] = true });

        try
        {
            var damaged = await server.CallAsync("write_text", new()
            {
                ["path"] = "appsettings.json",
                ["content"] = "{ \"damaged\": true }\n",
            });

            Assert.DoesNotContain("ERROR", damaged, StringComparison.Ordinal);

            var restored = await server.CallAsync("write_text", new() { ["path"] = "appsettings.json", ["ref"] = "HEAD" });
            var text = await server.CallAsync("read_text", new() { ["path"] = "appsettings.json", ["verbose"] = true });

            Assert.DoesNotContain("ERROR", restored, StringComparison.Ordinal);
            Assert.DoesNotContain("damaged", text, StringComparison.Ordinal);
            Assert.Equal(original, text);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = "appsettings.json", ["ref"] = "HEAD" });
        }
    }

    [Fact]
    public async Task WriteText_WithRefAndFiles_RefusesInsteadOfIgnoringTheRef()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["ref"] = "HEAD",
            ["files"] = new[] { new Dictionary<string, object?> { ["path"] = "appsettings.json", ["content"] = "{}" } },
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_WithRefAndContent_RefusesInsteadOfPickingOne()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "appsettings.json",
            ["ref"] = "HEAD",
            ["content"] = "{}",
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_OfHtmlEscapedMarkup_WarnsThatItIsNotMarkup()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/Fixture.Trading.csproj",
            ["content"] = "&lt;Project Sdk=&quot;Microsoft.NET.Sdk&quot;&gt;&lt;/Project&gt;\n",
            ["dryRun"] = true,
        });

        Assert.Contains("WARNING", text, StringComparison.Ordinal);
        Assert.Contains("&lt;", text, StringComparison.Ordinal);
        Assert.Contains("write_text ref=HEAD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumnsAndASection_ProjectsOnlyThatSectionsTable()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["section"] = "## Open", ["columns"] = "Finding" });

        Assert.StartsWith("3 rows", text, StringComparison.Ordinal);
        Assert.Contains("**F1** first row", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**F0** older row", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumnsAndMaxLines_ReportsWhatItTruncated()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Finding", ["maxLines"] = 2 });

        Assert.StartsWith("2/4 rows truncated", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**F0** older row", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithOneKnownAndOneUnknownColumn_RefusesNamingOnlyTheUnknownOne()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Finding,Severity" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("columns=Severity names no column", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumnsAndHeadings_RefusesRatherThanIgnoringOne()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Finding", ["headings"] = true });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("headings=true and columns=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumnsAndASectionThatLacksTheColumn_NamesTheSectionItScannedAndTheWayOut()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["section"] = "## Open", ["columns"] = "Outcome" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("## Open", text, StringComparison.Ordinal);
        Assert.Contains("drop section=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithColumnsAndALineRange_RefusesRatherThanIgnoringTheRange()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "notes.md", ["columns"] = "Finding", ["startLine"] = 15, ["endLine"] = 19 });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("startLine=", text, StringComparison.Ordinal);
        Assert.Contains("section=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryGlobTakingTool_RefusesABraceExpansionInsteadOfAnsweringZero()
    {
        var files = await server.CallAsync("find_files", new() { ["glob"] = "**/*.{cs,md}" });
        var searched = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "**/*.{cs,md}" });
        var excluded = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["exclude"] = "**/{bin,obj}/**" });
        var changed = await server.CallAsync("changed_files", new() { ["exclude"] = "**/*.{md,yml}" });

        foreach (var text in new[] { files, searched, excluded, changed })
        {
            Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
            Assert.Contains("brace", text, StringComparison.Ordinal);
            Assert.Contains("remedy:", text, StringComparison.Ordinal);
        }

        Assert.Contains("'exclude'", excluded, StringComparison.Ordinal);
        Assert.Contains("'glob'", files, StringComparison.Ordinal);
    }
}
