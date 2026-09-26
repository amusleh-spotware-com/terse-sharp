namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class AnalysisToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task Analyze_NamesTheEnginesItRan()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "warning" });

        Assert.Contains("engines=compiler", text, StringComparison.Ordinal);
        Assert.Contains("diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ReachesBelowWarning_WhereGetDiagnosticsStops()
    {
        var info = await server.CallAsync("analyze", new() { ["minSeverity"] = "hidden" });
        var errors = await server.CallAsync("analyze", new() { ["minSeverity"] = "error" });

        Assert.True(Total(info) >= Total(errors), $"hidden={Total(info)} error={Total(errors)}");
    }

    [Fact]
    public async Task Analyze_WithAnIdFilter_ReturnsOnlyThatId()
    {
        var text = await server.CallAsync("analyze", new() { ["ids"] = "CS9999", ["minSeverity"] = "hidden" });

        Assert.Contains("0 diagnostics", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithDryRun_LeavesTheFileUntouched()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Order.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("format", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_WithDryRun_ReportsWhatItWouldChange()
    {
        var text = await server.CallAsync("cleanup", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ReportsDeadCodeAsPartOfItsResult()
    {
        var text = await server.CallAsync("analyze", new() { ["minSeverity"] = "info" });

        Assert.Contains("engines=compiler", text, StringComparison.Ordinal);
        Assert.Contains("dead-code", text, StringComparison.Ordinal);
        Assert.Contains("TERSE001", text, StringComparison.Ordinal);
        Assert.Contains("is never referenced", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithDeadCodeDisabled_OmitsTheScanAndTheFindings()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["minSeverity"] = "info",
            ["includeDeadCode"] = false,
        });

        Assert.DoesNotContain("dead-code", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TERSE001", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_FilteredToDeadCodeOnly_ReturnsOnlyThose()
    {
        var text = await server.CallAsync("analyze", new() { ["ids"] = "TERSE001", ["minSeverity"] = "info" });

        var records = text.Split('\n').Where(line => line.Contains(": ", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(records);
        Assert.All(records, line => Assert.StartsWith("TERSE001", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyze_ScopedToAFile_ExcludesDeadCodeFoundInOtherFiles()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["minSeverity"] = "info",
        });

        Assert.DoesNotContain("NeverCalled", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ScopedToTheFileHoldingIt_StillReportsThatDeadCode()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["minSeverity"] = "info",
        });

        Assert.Contains("NeverCalled", text, StringComparison.Ordinal);
        Assert.Contains("TERSE001", text, StringComparison.Ordinal);
    }

    private static int Total(string response)
    {
        var newline = response.IndexOf('\n');
        var summary = response.AsSpan(0, newline < 0 ? response.Length : newline);
        var slash = summary.IndexOf('/');
        var counted = slash < 0 ? summary : summary[(slash + 1)..];

        return int.Parse(counted[..counted.IndexOf(' ')], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Gate_OverOneFile_AnswersOneVerdictLineCarryingTheAnalyzeCounts()
    {
        var text = await server.CallAsync("gate", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["dryRun"] = true,
        });

        var first = text.Split('\n')[0];

        Assert.Matches("^(clean|FAILED)  analyzed=", first);
        Assert.EndsWith("  dryRun", first, StringComparison.Ordinal);
        Assert.Contains("remaining=", first, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_ReportsTheSameDiagnosticsAnalyzeDoesForTheSameScope()
    {
        const string Path = "src/Fixture.Trading/OrderService.cs";

        var analyzed = await server.CallAsync("analyze", new() { ["path"] = Path, ["minSeverity"] = "info" });
        var gated = await server.CallAsync("gate", new() { ["path"] = Path, ["dryRun"] = true, ["verbose"] = true });

        Assert.Contains("format:", gated, StringComparison.Ordinal);
        Assert.Contains("cleanup:", gated, StringComparison.Ordinal);
        Assert.Equal(Total(analyzed), Remaining(gated));
    }

    [Fact]
    public async Task Gate_WhenAWriteStepWouldChangeAFile_NeverCondensesThatAway()
    {
        const string Target = "src/Fixture.Trading/Order.cs";
        var full = System.IO.Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Order.cs");
        var original = await File.ReadAllTextAsync(full, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(full);

        try
        {
            await server.CallAsync("write_text", new()
            {
                ["path"] = Target,
                ["content"] = "using System.Text;\n" + original,
                ["force"] = true,
            });

            var previewed = await server.CallAsync("gate", new() { ["path"] = Target, ["dryRun"] = true });
            var applied = await server.CallAsync("gate", new() { ["path"] = Target });

            Assert.StartsWith("FAILED  analyzed=", previewed, StringComparison.Ordinal);
            Assert.Contains("VERIFY_FAILED", previewed, StringComparison.Ordinal);
            Assert.StartsWith("clean  analyzed=", applied, StringComparison.Ordinal);
            Assert.Contains("remaining=0", applied, StringComparison.Ordinal);
            Assert.Contains("cleanup: ", applied, StringComparison.Ordinal);
            Assert.Contains("Order.cs", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Text;", await File.ReadAllTextAsync(full, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Target, ["content"] = original, ["force"] = true });
            File.SetLastWriteTimeUtc(full, stamp);
        }
    }

    private static int Remaining(string text)
    {
        var marker = text.IndexOf("remaining=", StringComparison.Ordinal) + "remaining=".Length;
        var tail = text.AsSpan(marker);
        var end = tail.IndexOfAny(" \r\n");

        return int.Parse(end < 0 ? tail : tail[..end], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Analyze_WithChanged_NamesGateAsTheOneCallForm()
    {
        const string probe = "src/Fixture.Trading/TerseProbe.cs";

        await server.CallAsync("write_text", new()
        {
            ["path"] = probe,
            ["content"] = "namespace Fixture.Trading;\n\npublic sealed record GateSteerProbe(int Value);\n",
            ["force"] = true,
        });

        try
        {
            var scoped = await server.CallAsync("analyze", new() { ["changed"] = true, ["minSeverity"] = "info" });
            var unscoped = await server.CallAsync("analyze", new() { ["path"] = probe, ["minSeverity"] = "info" });

            Assert.Contains("gate runs this, format and cleanup fix=all as one call", scoped, StringComparison.Ordinal);
            Assert.DoesNotContain("gate runs this", unscoped, StringComparison.Ordinal);
        }
        finally
        {
            var removed = await server.CallAsync("write_text", new() { ["path"] = probe, ["delete"] = true, ["force"] = true });

            Assert.DoesNotContain("ERROR", removed, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Gate_AnalyzedCountsTheDocumentsInScope_NotTheDiagnosticsItFound()
    {
        var clean = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/Order.cs", ["dryRun"] = true });
        var findings = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["dryRun"] = true });
        var glob = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/*.cs", ["dryRun"] = true });

        Assert.Equal(1, Analyzed(clean));
        Assert.Equal(1, Analyzed(findings));
        Assert.True(Remaining(findings) > 0, findings);
        Assert.True(Analyzed(glob) > 1, glob);
    }

    [Fact]
    public async Task Gate_OverAScopeMatchingNoDocument_AnswersAnErrorWithARemedy()
    {
        var text = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/NoSuchDocument*.cs" });

        Assert.StartsWith("ERROR ", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("analyzed=", text, StringComparison.Ordinal);
    }

    private static int Analyzed(string text)
    {
        var marker = text.IndexOf("analyzed=", StringComparison.Ordinal) + "analyzed=".Length;
        var tail = text.AsSpan(marker);
        var end = tail.IndexOfAny(" \r\n");

        return int.Parse(end < 0 ? tail : tail[..end], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Analyze_WithAnIdNoReferencedAnalyzerDeclares_SaysNotEnabledInsteadOfASilentZero()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ids"] = "CA9999",
        });

        Assert.Contains("NOT_ENABLED CA9999", text, StringComparison.Ordinal);
        Assert.Contains("could not have found it", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithACompilerId_DoesNotClaimItIsNotEnabled()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ids"] = "CS0219",
        });

        Assert.DoesNotContain("NOT_ENABLED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithSeverityInsteadOfMinSeverity_AnswersTheSame()
    {
        var named = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["minSeverity"] = "warning",
        });

        var aliased = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["severity"] = "warning",
        });

        Assert.DoesNotContain("ERROR", aliased, StringComparison.Ordinal);
        Assert.Equal(WithoutOneOffNotes(named), WithoutOneOffNotes(aliased));
    }

    private static string WithoutOneOffNotes(string response) => string.Join(
        "\n",
        response.Split('\n').Where(line => !line.StartsWith("compilations=", StringComparison.Ordinal)));

    [Fact]
    public async Task Analyze_NamesTheDeclarationEachFindingSitsIn()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["minSeverity"] = "info",
            ["includeDeadCode"] = false,
        });

        Assert.Contains("OrderService.cs:15:16 OrderService.Unused", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs:17:17 OrderService.NeverCalled", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WhenThePathsBatchSaturatesItsCap_NamesTheOneCallThatAnswersTheWholeSweep()
    {
        string[] batch =
        [
            "src/Fixture.Trading/Awkward.cs",
            "src/Fixture.Trading/Compactness.cs",
            "src/Fixture.Trading/Composition.cs",
            "src/Fixture.Trading/InMemoryOrderRepository.cs",
            "src/Fixture.Trading/IOrderRepository.cs",
            "src/Fixture.Trading/Localization.cs",
            "src/Fixture.Trading/NullOrderRepository.cs",
            "src/Fixture.Trading/Order.cs",
            "src/Fixture.Trading/OrderBook.cs",
            "src/Fixture.Trading/OrderRouter.cs",
        ];

        var saturated = await server.CallAsync("analyze", new() { ["paths"] = batch, ["minSeverity"] = "warning" });
        var narrow = await server.CallAsync("analyze", new() { ["paths"] = batch[..3], ["minSeverity"] = "warning" });

        Assert.Contains("next: analyze changed=true", saturated, StringComparison.Ordinal);
        Assert.DoesNotContain("next: analyze changed=true", narrow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ForAPathsBatchOfRepeatedEntries_DoesNotClaimTheCapWasReached()
    {
        var repeated = new string[10];
        Array.Fill(repeated, "src/Fixture.Trading/Order.cs");

        var text = await server.CallAsync("analyze", new() { ["paths"] = repeated, ["minSeverity"] = "warning" });

        Assert.DoesNotContain("next: analyze changed=true", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_WithChangedTrue_AnswersTheSameVerdictBecauseItIsAlreadyScopedThatWay()
    {
        var plain = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/Order.cs", ["dryRun"] = true });
        var documented = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading/Order.cs", ["dryRun"] = true, ["changed"] = true });

        Assert.DoesNotContain("unrecognized", documented, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", plain, StringComparison.Ordinal);
        Assert.Contains("analyzed=", plain, StringComparison.Ordinal);
        Assert.Equal(WithoutTheReplayNote(WithoutTheOncePerLoadNote(plain)), WithoutTheReplayNote(WithoutTheOncePerLoadNote(documented)));
    }

    private static string[] WithoutTheReplayNote(string[] lines) =>
        [.. lines.Where(line => !line.Contains("UNCHANGED - nothing was written", StringComparison.Ordinal))];

    [Fact]
    public async Task Gate_WithChangedFalse_IsRefusedNamingTheWholeDocumentModeInsteadOfListingParameters()
    {
        var text = await server.CallAsync("gate", new() { ["changed"] = false });

        Assert.Contains("always scoped to the files modified since the workspace loaded", text, StringComparison.Ordinal);
        Assert.Contains("solution=true", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithPaths_ScopesOnePassToEveryEntry()
    {
        var text = await server.CallAsync("format", new()
        {
            ["paths"] = new[] { "src/Fixture.Trading/Order.cs", "src/Fixture.Trading/OrderSide.cs" },
            ["verify"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.True(
            text.Contains("clean", StringComparison.Ordinal) || text.Contains("VERIFY_FAILED", StringComparison.Ordinal),
            text);
    }

    [Fact]
    public async Task Format_WithMorePathsThanItAnalyzesInOneCall_IsRefusedByName()
    {
        var text = await server.CallAsync("format", new()
        {
            ["paths"] = Enumerable.Repeat("src/Fixture.Trading/Order.cs", 11).ToArray(),
            ["verify"] = true,
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("paths", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithNoCheckedInPolicy_StaysSilentAboutTheAmbientCommentRule()
    {
        const string probe = "src/Fixture.Trading/CommentProbe.cs";

        await server.CallAsync("write_text", new()
        {
            ["path"] = probe,
            ["force"] = true,
            ["content"] = "namespace Fixture.Trading;\n\npublic static class CommentProbe\n{\n    // explain the obvious\n    public static int Value => 1;\n}\n",
        });

        try
        {
            var text = await server.CallAsync("analyze", new() { ["path"] = probe, ["minSeverity"] = "info" });

            Assert.DoesNotContain("TERSE112", text, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = probe, ["delete"] = true, ["force"] = true });
        }
    }

    private static string[] WithoutTheOncePerLoadNote(string response) =>
        [.. response.Split('\n').Where(line => !line.StartsWith("compilations=realized", StringComparison.Ordinal))];

    private static int Records(string response) =>
        int.Parse(response.AsSpan(0, response.IndexOf(' ', StringComparison.Ordinal)), CultureInfo.InvariantCulture);

    [Fact]
    public async Task Analyze_WithBaseRef_KeepsWhatTheWorkingTreeChanged_AndCountsThePreExistingRest()
    {
        const string Probe = "src/Fixture.Trading/BaseRefProbe.cs";
        const string Content = "namespace Fixture.Trading;\n\npublic sealed class BaseRefProbe\n{\n    private int Hidden() => 1;\n}\n";

        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = Content, ["force"] = true, ["allowErrors"] = true });
        try
        {
            var probed = await server.CallAsync("analyze", new() { ["path"] = Probe, ["minSeverity"] = "info", ["baseRef"] = "HEAD" });
            var plain = await server.CallAsync("analyze", new() { ["minSeverity"] = "info" });
            var narrowed = await server.CallAsync("analyze", new() { ["minSeverity"] = "info", ["baseRef"] = "HEAD" });

            Assert.Contains("BaseRefProbe", probed, StringComparison.Ordinal);
            Assert.Contains("pre-existing finding(s)", narrowed, StringComparison.Ordinal);
            Assert.Contains("against HEAD", narrowed, StringComparison.Ordinal);
            Assert.DoesNotContain("pre-existing finding(s)", plain, StringComparison.Ordinal);
            Assert.True(
                Records(narrowed) < Records(plain),
                string.Create(CultureInfo.InvariantCulture, $"narrowed reported {Records(narrowed)} records against {Records(plain)} for the whole repository"));
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task Analyze_RepeatedWithNothingWrittenInBetween_ReplaysThePreviousAnswerInsteadOfRunningAgain()
    {
        const string Probe = "src/Fixture.Trading/ReplayProbe.cs";

        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class ReplayProbe\n{\n    private int Hidden() => 1;\n}\n", ["force"] = true, ["allowErrors"] = true });
        try
        {
            var first = await server.CallAsync("analyze", new() { ["path"] = Probe, ["minSeverity"] = "info" });
            var second = await server.CallAsync("analyze", new() { ["path"] = Probe, ["minSeverity"] = "info" });

            Assert.DoesNotContain("UNCHANGED", first, StringComparison.Ordinal);
            Assert.Contains("analyze UNCHANGED", second, StringComparison.Ordinal);
            Assert.StartsWith(first, second, StringComparison.Ordinal);
            Assert.Contains("force=true re-runs it", second, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task Analyze_WithIdsWrittenAsAJsonArray_ReadsTheIdsInsteadOfDeclaringThemNotEnabled()
    {
        const string Target = "src/Fixture.Trading/OrderService.cs";

        var bare = await server.CallAsync("analyze", new() { ["path"] = Target, ["ids"] = "CS0219,TERSE001", ["minSeverity"] = "info" });
        var bracketed = await server.CallAsync("analyze", new() { ["path"] = Target, ["ids"] = "[\"CS0219\", \"TERSE001\"]", ["minSeverity"] = "info" });

        Assert.True(Records(bare) > 0, bare);
        Assert.DoesNotContain("NOT_ENABLED", bracketed, StringComparison.Ordinal);
        Assert.Equal(Records(bare), Records(bracketed));
    }

    [Fact]
    public async Task Gate_WithBaseRefOverAGlob_WritesOnlyTheFilesTheWorkingTreeChanged()
    {
        const string Probe = "src/Fixture.Trading/GateScopeProbe.cs";
        const string Glob = "src/Fixture.Trading/*.cs";
        var untouched = System.IO.Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "StyleSample.cs");
        var original = await File.ReadAllTextAsync(untouched, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(untouched);

        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "using System.Text;\n\nnamespace Fixture.Trading;\n\npublic sealed class GateScopeProbe\n{\n    public int Value => 1;\n}\n",
            ["force"] = true,
        });

        try
        {
            var previewed = await server.CallAsync("gate", new() { ["path"] = Glob, ["baseRef"] = "HEAD", ["dryRun"] = true, ["verbose"] = true });
            var applied = await server.CallAsync("gate", new() { ["path"] = Glob, ["baseRef"] = "HEAD", ["verbose"] = true });

            Assert.Contains("GateScopeProbe.cs", previewed, StringComparison.Ordinal);
            Assert.DoesNotContain("StyleSample.cs", previewed, StringComparison.Ordinal);
            Assert.DoesNotContain("StyleSample.cs", applied, StringComparison.Ordinal);
            Assert.Equal(original, await File.ReadAllTextAsync(untouched, TestContext.Current.CancellationToken));
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
            await server.CallAsync("write_text", new() { ["path"] = "src/Fixture.Trading/StyleSample.cs", ["content"] = original, ["force"] = true });
            File.SetLastWriteTimeUtc(untouched, stamp);
        }
    }

    [Fact]
    public async Task Gate_OverTheChangedFilesWithNoBaseRef_FoldsWhatTheWorkingTreeDidNotChange_AndBaseRefEmptyOptsOut()
    {
        var full = System.IO.Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderService.cs");
        var stamp = File.GetLastWriteTimeUtc(full);

        File.SetLastWriteTimeUtc(full, DateTime.UtcNow);
        try
        {
            var defaulted = await server.CallAsync("gate", new() { ["dryRun"] = true });
            var every = await server.CallAsync("gate", new() { ["dryRun"] = true, ["baseRef"] = "" });

            Assert.StartsWith("clean  analyzed=", defaulted, StringComparison.Ordinal);
            Assert.Contains("remaining=0  preExisting=", defaulted, StringComparison.Ordinal);
            Assert.True(Remaining(every) > 0, every);
            Assert.DoesNotContain("preExisting=", every, StringComparison.Ordinal);
            Assert.True(
                defaulted.Length < every.Length,
                string.Create(CultureInfo.InvariantCulture, $"defaulted={defaulted.Length} every={every.Length} chars"));
        }
        finally
        {
            File.SetLastWriteTimeUtc(full, stamp);
        }
    }

    [Fact]
    public async Task Analyze_WithChangedAndNoBaseRef_FoldsWhatTheWorkingTreeDidNotChange_AndBaseRefEmptyOptsOut()
    {
        var full = System.IO.Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderService.cs");
        var stamp = File.GetLastWriteTimeUtc(full);

        File.SetLastWriteTimeUtc(full, DateTime.UtcNow);
        try
        {
            var defaulted = await server.CallAsync("analyze", new() { ["changed"] = true, ["minSeverity"] = "info" });
            var every = await server.CallAsync("analyze", new() { ["changed"] = true, ["minSeverity"] = "info", ["baseRef"] = "" });

            Assert.Contains("pre-existing finding(s)", defaulted, StringComparison.Ordinal);
            Assert.Contains("against HEAD", defaulted, StringComparison.Ordinal);
            Assert.DoesNotContain("pre-existing finding(s)", every, StringComparison.Ordinal);
            Assert.True(
                Records(defaulted) < Records(every),
                string.Create(CultureInfo.InvariantCulture, $"defaulted reported {Records(defaulted)} records against {Records(every)}"));
        }
        finally
        {
            File.SetLastWriteTimeUtc(full, stamp);
        }
    }

    [Fact]
    public async Task Gate_OverSeveralPaths_AnswersOneVerdictOverTheirUnion()
    {
        var pair = await server.CallAsync("gate", new() { ["paths"] = new[] { "src/Fixture.Trading/Order.cs", "src/Fixture.Trading/OrderService.cs" }, ["dryRun"] = true });
        var directory = await server.CallAsync("gate", new() { ["path"] = "src/Fixture.Trading", ["dryRun"] = true });
        var overlapping = await server.CallAsync("gate", new() { ["paths"] = new[] { "src/Fixture.Trading/Order.cs", "src/Fixture.Trading" }, ["dryRun"] = true });

        Assert.DoesNotContain("InvalidArgument", pair, StringComparison.Ordinal);
        Assert.Equal(2, Analyzed(pair));
        Assert.True(Remaining(pair) > 0, pair);
        Assert.Equal(Analyzed(directory), Analyzed(overlapping));
    }

    [Fact]
    public async Task Gate_WithAPathsEntryMatchingNoDocument_IsRefusedNamingThatEntry()
    {
        var text = await server.CallAsync("gate", new() { ["paths"] = new[] { "src/Fixture.Trading/Order.cs", "src/Fixture.Trading/NoSuchDocument.cs" }, ["dryRun"] = true });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("NoSuchDocument.cs' matches no document", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObservedSiblingSpellings_BindToTheirCanonicalParameter_InsteadOfAnsweringInvalidArgument()
    {
        var outline = await server.CallAsync("get_type_outline", new() { ["typeName"] = "OrderService" });
        var counted = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs", ["output_mode"] = "count" });
        var canonicalOutline = await server.CallAsync("get_type_outline", new() { ["symbol"] = "OrderService" });
        var countOnly = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs", ["countOnly"] = true });
        var projects = await server.CallAsync("list_projects", new() { ["contains"] = "Trading" });
        var grepContext = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs", ["-C"] = 1 });
        var added = await server.CallAsync("add_member", new() { ["typeSymbolId"] = "OrderService", ["code"] = "public int AliasProbe() => 1;", ["dryRun"] = true });
        var context = await server.CallAsync("search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs", ["context"] = 1 });

        Assert.Equal(canonicalOutline, outline);
        Assert.Contains("Fixture.Trading", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidArgument", projects, StringComparison.Ordinal);
        Assert.Equal(countOnly, counted);
        Assert.Equal(context, grepContext);
        Assert.Contains("AliasProbe", added, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidArgument", added, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAliasBesideItsCanonicalParameter_IsStillRefusedByName()
    {
        var text = await server.CallAsync("get_type_outline", new() { ["symbol"] = "OrderService", ["typeName"] = "Order" });

        Assert.Contains("unrecognized typeName", text, StringComparison.Ordinal);
    }
}
