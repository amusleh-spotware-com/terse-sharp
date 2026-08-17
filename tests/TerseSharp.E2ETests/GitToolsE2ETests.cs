using TerseSharp.Core;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class GitToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ChangedFiles_OnAnUnmodifiedFixture_AnswersZeroWithoutADiff()
    {
        var text = await server.CallAsync("changed_files", []);

        Assert.DoesNotContain("@@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public async Task ChangedFiles_WithAnUnknownBaseRef_NamesGitsExitCodeAndARemedy()
    {
        var text = await server.CallAsync("changed_files", new() { ["baseRef"] = "terse-no-such-ref" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiffSymbols_AgainstAnEarlierCommit_NamesChangedMembersAsResolvableSymbolIds()
    {
        var text = await server.CallAsync("diff_symbols", new()
        {
            ["baseRef"] = "HEAD~1",
            ["path"] = ".",
            ["maxResults"] = 50,
        });

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return;

        foreach (var record in text.Split('\n').Where(line => line.Contains("  EXACT  ", StringComparison.Ordinal)))
        {
            var symbolId = record[(record.IndexOf("  EXACT  ", StringComparison.Ordinal) + 9)..].Trim();
            var resolved = await server.CallAsync("get_symbol", new() { ["symbolId"] = symbolId });

            Assert.DoesNotContain("ERROR", resolved, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DiffText_NeverReturnsMoreLinesThanMaxLines()
    {
        var text = await server.CallAsync("diff_text", new() { ["baseRef"] = "HEAD~1", ["maxLines"] = 5 });

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            return;

        var lines = text.Split('\n');

        Assert.Contains("lines", lines[0], StringComparison.Ordinal);
        Assert.True(lines.Length - 1 <= 5, text);
    }

    [Theory]
    [InlineData("@@ -12,0 +12,2 @@", 12, 13)]
    [InlineData("@@ -1 +1 @@", 1, 1)]
    [InlineData("@@ -10,3 +9,0 @@", 9, 9)]
    [InlineData("@@ -4,6 +4,7 @@ public sealed class OrderBook", 4, 10)]
    public void DiffParser_ReadsTheAddedSideOfEveryHunkHeaderShape(string header, int start, int end)
    {
        var mapped = DiffParser.Hunks(
            "diff --git a/src/Fixture.Trading/OrderBook.cs b/src/Fixture.Trading/OrderBook.cs\n"
            + "--- a/src/Fixture.Trading/OrderBook.cs\n"
            + "+++ b/src/Fixture.Trading/OrderBook.cs\n"
            + header);

        var hunk = Assert.Single(mapped);

        Assert.Equal("src/Fixture.Trading/OrderBook.cs", hunk.Path);
        Assert.Equal(start, hunk.Start);
        Assert.Equal(end, hunk.End);
    }

    [Fact]
    public void DiffParser_SkipsHunksOfADeletedFile()
    {
        var mapped = DiffParser.Hunks(
            "diff --git a/Gone.cs b/Gone.cs\n--- a/Gone.cs\n+++ /dev/null\n@@ -1,5 +0,0 @@");

        Assert.Empty(mapped);
    }

    [Fact]
    public void NumStat_ReportsABinaryFileAsUnknownRatherThanZero()
    {
        var files = DiffParser.NumStat("-\t-\tassets/logo.png\n3\t1\tsrc/Order.cs");

        Assert.Equal(2, files.Count);
        Assert.Equal(-1, files[0].Added);
        Assert.Equal(-1, files[0].Deleted);
        Assert.Equal(3, files[1].Added);
    }

    [Fact]
    public async Task ChangedFiles_WithAPath_ListsOnlyWhatThatPathspecCovers()
    {
        const string Probe = "terse-changed-files-probe.txt";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "probe\n" });
        try
        {
            var everything = await server.CallAsync("changed_files", []);
            var scoped = await server.CallAsync("changed_files", new() { ["path"] = "src" });
            var named = await server.CallAsync("changed_files", new() { ["path"] = Probe });

            Assert.Contains(Probe, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(Probe, scoped, StringComparison.Ordinal);
            Assert.All(Records(scoped), record => Assert.StartsWith("src/", record, StringComparison.Ordinal));
            Assert.Contains(Probe, named, StringComparison.Ordinal);
            Assert.Contains("1 files", named, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }
    [Fact]
    public async Task DiffSymbols_WithAHunkNoDeclarationContains_NamesTheExactDiffTextCallForThatPath()
    {
        const string Probe = "appsettings.json";
        var path = Path.Combine(TerseServerFixture.FixtureRoot, Probe);
        var original = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var written = File.GetLastWriteTimeUtc(path);
        try
        {
            await File.WriteAllTextAsync(path, original.Replace("100", "250", StringComparison.Ordinal), TestContext.Current.CancellationToken);

            var text = await server.CallAsync("diff_symbols", new() { ["path"] = Probe });

            Assert.Contains("HEURISTIC", text, StringComparison.Ordinal);
            Assert.Contains("diff_text path=" + Probe, text, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(path, original, CancellationToken.None);
            File.SetLastWriteTimeUtc(path, written);
        }
    }
    private static string[] Records(string listing) =>
        [.. listing.Split('\n').Skip(1).Where(line => line.Length > 0)];

    [Fact]
    public async Task ChangedFiles_WithExclude_DropsThePathsAPathspecCannotLeaveOut()
    {
        const string Probe = "terse-changed-files-exclude-probe.txt";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "probe\n" });
        try
        {
            var everything = await server.CallAsync("changed_files", []);
            var filtered = await server.CallAsync("changed_files", new() { ["exclude"] = "**/*.txt" });

            Assert.Contains(Probe, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(Probe, filtered, StringComparison.Ordinal);
            Assert.All(Records(filtered), record => Assert.DoesNotContain(".txt", record, StringComparison.Ordinal));
            Assert.True(Records(filtered).Length < Records(everything).Length, filtered);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task ChangedFiles_WithRoot_AnswersAboutThatDirectoryAndTagsItOutsideTheWorkspace()
    {
        var text = await server.CallAsync("changed_files", new() { ["root"] = TerseServerFixture.RepositoryRoot });

        Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffText_WithRoot_TagsTheAnswerOutsideTheWorkspace()
    {
        var text = await server.CallAsync("diff_text", new()
        {
            ["root"] = TerseServerFixture.RepositoryRoot,
            ["path"] = "src",
            ["maxLines"] = 5,
        });

        Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedFiles_WithARelativeRoot_IsRefusedWithARemedy()
    {
        var text = await server.CallAsync("changed_files", new() { ["root"] = "../somewhere" });

        Assert.Contains("is not an absolute path", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedFiles_WithARootThatDoesNotExist_IsRefusedWithARemedy()
    {
        var missing = Path.Combine(Path.GetTempPath(), "terse-no-such-directory-i167");

        var text = await server.CallAsync("changed_files", new() { ["root"] = missing });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffSymbols_WithRoot_RefusesAndNamesTheTwoToolsThatCanAnswer()
    {
        var text = await server.CallAsync("diff_symbols", new() { ["root"] = TerseServerFixture.RepositoryRoot });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("changed_files root=", text, StringComparison.Ordinal);
        Assert.Contains("diff_text root=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffText_WhenItTruncates_NamesTheExactMaxLinesThatReturnsTheRest()
    {
        var text = await server.CallAsync("diff_text", new()
        {
            ["root"] = TerseServerFixture.RepositoryRoot,
            ["baseRef"] = "HEAD~1",
            ["maxLines"] = 1,
        });

        var summary = text.Split('\n')[0];

        Assert.DoesNotContain("ERROR", summary, StringComparison.Ordinal);
        Assert.Contains("truncated", summary, StringComparison.Ordinal);

        var total = summary.Split('/')[1].Split(' ')[0];

        Assert.True(int.Parse(total, CultureInfo.InvariantCulture) > 1, summary);
        Assert.EndsWith("narrow with path=, paths= or maxLines=" + total, summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_AtARef_AnswersTheOutlineOfThatRevisionAndNotItsWholeText()
    {
        var outline = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ref"] = "HEAD",
        });

        Assert.Contains("OrderService  class public", outline, StringComparison.Ordinal);
        Assert.Contains("OrderService.SubmitTwice", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Fixture.Trading;", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("symbolIds=[", outline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_AtARef_TakesTheLineRangeAndTailTheWorkingTreeReadTakes()
    {
        var ranged = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ref"] = "HEAD",
            ["startLine"] = 11,
            ["endLine"] = 11,
        });

        Assert.StartsWith("1 lines", ranged, StringComparison.Ordinal);
        Assert.Contains("public bool Submit(Order order)", ranged, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitTwice", ranged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_AtARef_OutlinesThatRevision()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ref"] = "HEAD",
            ["contains"] = "Submit",
        });

        Assert.Contains("OrderService.Submit", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.SubmitTwice", text, StringComparison.Ordinal);
        Assert.Contains(" members", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PendingCount", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_AtARef_IsRefusedWithPathsRatherThanReadingOnlyTheFirst()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["paths"] = new[] { "src/Fixture.Trading/OrderService.cs", "src/Fixture.Trading/OrderRouter.cs" },
            ["ref"] = "HEAD",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("cannot be combined with paths=", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_AtARefThatDoesNotExist_SaysSoInsteadOfAnsweringTheWorkingTree()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["ref"] = "no-such-ref-anywhere",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool Submit", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_WithCommitBesideAFilter_IsRefusedRatherThanIgnoringTheFilter()
    {
        var text = await server.CallAsync("history", new() { ["commit"] = "HEAD", ["contains"] = "Submit" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("cannot be combined with contains=", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_WithAPickaxe_ListsOnlyTheCommitsThatTouchedThatLiteral()
    {
        var all = await server.CallAsync("history", new() { ["maxResults"] = 20 });
        var picked = await server.CallAsync("history", new() { ["contains"] = "SubmitTwice", ["maxResults"] = 20 });

        Assert.StartsWith("20 commits", all, StringComparison.Ordinal);
        Assert.Contains("more commits match than were listed", all, StringComparison.Ordinal);
        Assert.DoesNotContain("/21", all, StringComparison.Ordinal);
        Assert.True(picked.Split('\n').Length < all.Split('\n').Length, picked);
    }

    [Fact]
    public async Task ChangedFiles_FoldsADirectoryContributingManyUntrackedFilesIntoOneRow()
    {
        var scratch = Path.Combine(TerseServerFixture.FixtureRoot, "scratch-i237");

        Directory.CreateDirectory(scratch);

        try
        {
            for (var index = 0; index < 8; index++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(scratch, string.Create(CultureInfo.InvariantCulture, $"note{index}.txt")),
                    "scratch",
                    TestContext.Current.CancellationToken);
            }

            var text = await server.CallAsync("changed_files", new() { ["path"] = "scratch-i237" });
            var rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.Contains("8 files", text, StringComparison.Ordinal);
            Assert.Contains("scratch-i237/**", text, StringComparison.Ordinal);
            Assert.Contains("x8 untracked", text, StringComparison.Ordinal);
            Assert.DoesNotContain("note3.txt", text, StringComparison.Ordinal);
            Assert.Equal(2, rows.Length);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedFiles_ForFewUntrackedFiles_StillListsThemOneByOne()
    {
        var scratch = Path.Combine(TerseServerFixture.FixtureRoot, "scratch-i237-small");

        Directory.CreateDirectory(scratch);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(scratch, "only.txt"), "scratch", TestContext.Current.CancellationToken);

            var text = await server.CallAsync("changed_files", new() { ["path"] = "scratch-i237-small" });

            Assert.Contains("only.txt", text, StringComparison.Ordinal);
            Assert.DoesNotContain("/**", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedFiles_WhenTheListIsCapped_CountsFilesOnBothHalvesOfTheSummary()
    {
        var first = Path.Combine(TerseServerFixture.FixtureRoot, "scratch-i278a");
        var second = Path.Combine(TerseServerFixture.FixtureRoot, "scratch-i278b");

        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        try
        {
            await FillAsync(first, 8);
            await FillAsync(second, 7);

            var whole = await server.CallAsync("changed_files", new() { ["path"] = "scratch-i278*" });
            var capped = await server.CallAsync("changed_files", new() { ["path"] = "scratch-i278*", ["maxResults"] = 1 });

            Assert.StartsWith("15 files", whole, StringComparison.Ordinal);
            Assert.StartsWith("8/15 files truncated", capped, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    private static async Task FillAsync(string directory, int files)
    {
        for (var index = 0; index < files; index++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"note{index}.txt")),
                "scratch",
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ReadText_AtARef_DecodesGitsOutputAsUtf8()
    {
        await using var solution = await TerseTempSolution.StartAsync(
            watch: false,
            TestContext.Current.CancellationToken,
            CommittedUnicodeProbeAsync);

        var text = await solution.CallAsync("read_text", new()
        {
            ["path"] = "unicode-probe.md",
            ["ref"] = "HEAD",
            ["verbose"] = true,
        });

        Assert.Contains("an em dash — here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Γ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("�", text, StringComparison.Ordinal);
    }

    private static async Task CommittedUnicodeProbeAsync(string root)
    {
        await File.WriteAllTextAsync(
            Path.Combine(root, "unicode-probe.md"),
            "an em dash — here\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            TestContext.Current.CancellationToken);

        await RunGitAsync(root, "init");
        await RunGitAsync(root, "-c", "core.autocrlf=false", "add", "-A");
        await RunGitAsync(
            root,
            "-c",
            "user.email=terse@example.com",
            "-c",
            "user.name=terse",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "--no-verify",
            "-m",
            "probe");
    }

    private static async Task RunGitAsync(string root, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start");

        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await Task.WhenAll(output, error, process.WaitForExitAsync(TestContext.Current.CancellationToken));

        if (process.ExitCode is not 0)
            throw new InvalidOperationException("git " + arguments[^1] + " exited " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + await error);
    }

    [Fact]
    public async Task History_WithTags_ListsTheRepositoryTagsNewestFirstWithTheCommitEachNames()
    {
        var tags = await server.CallAsync("history", new() { ["tags"] = true, ["maxResults"] = 5 });
        var newest = tags.Split('\n').First(line => line.StartsWith('v')).Split(' ');
        var commits = await server.CallAsync("history", new() { ["root"] = TerseServerFixture.RepositoryRoot, ["maxResults"] = 50 });

        Assert.Contains(" tags", tags, StringComparison.Ordinal);
        Assert.Matches(@"^v\d+\.\d+\.\d+$", newest[0]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", newest[2]);
        Assert.Contains(newest[1], commits, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_WithTagsBesideACommitFilter_IsRefusedInsteadOfIgnoringTheFilter()
    {
        var text = await server.CallAsync("history", new() { ["tags"] = true, ["contains"] = "OrderService" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("contains=", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }
}

internal static class DiffSymbolProbe
{
    public static IReadOnlyList<TerseSharp.Core.DiffHunk> Map(string diff) => TerseSharp.Core.DiffParser.Hunks(diff);
}
