using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
public sealed class FileServiceTests
{
    private const int LineCount = 5000;

    [Fact]
    public async Task ReadText_OnAFileLargerThanTheOldCap_StillServesARange()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = WriteLargeFile(lease.Workspace.Root, out var path);

        try
        {
            var result = await Read(lease.Workspace, name, 4000, 4002, 2000);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("4000: line 4000 ", result.Value!, StringComparison.Ordinal);
            Assert.Contains("3 lines", result.Value!, StringComparison.Ordinal);
            Assert.DoesNotContain("truncated", result.Value!, StringComparison.Ordinal);
            Assert.DoesNotContain("3999: ", result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadText_WithoutARange_CapsTheLinesItReturns()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = WriteLargeFile(lease.Workspace.Root, out var path);

        try
        {
            var result = await Read(lease.Workspace, name, 0, 0, 10);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("10/5000 lines truncated", result.Value!, StringComparison.Ordinal);
            Assert.DoesNotContain("11: ", result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadText_WithASingleLineOverTheResponseBudget_TruncatesItAndSaysByHowMuch()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-wide-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(lease.Workspace.Root, name);

        await File.WriteAllTextAsync(path, new string('x', 200_000), TestContext.Current.CancellationToken);

        try
        {
            var result = await Read(lease.Workspace, name, 0, 0, 2000);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("... (+", result.Value!, StringComparison.Ordinal);
            Assert.True(result.Value!.Length < 200_000, "the response was not truncated");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditText_WithUnixLineEndingsAgainstAWindowsFile_StillMatches()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-crlf-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(lease.Workspace.Root, name);

        await File.WriteAllTextAsync(path, "alpha\r\nbeta\r\ngamma\r\n", TestContext.Current.CancellationToken);

        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("alpha\nbeta", "one\ntwo", null, false, false, false),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal("one\r\ntwo\r\ngamma\r\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditText_WhenNothingMatches_NamesTheClosestLinesInsteadOfAskingForMoreText()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-miss-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(lease.Workspace.Root, name);

        await File.WriteAllTextAsync(path, "alpha beta\r\ngamma\r\n", TestContext.Current.CancellationToken);

        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("alpha beta delta", "x", null, false, false, false),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsOk);
            Assert.Contains("L1: alpha beta", result.Error!.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadText_WithHeadings_ReturnsTheMarkdownMapAndNotTheBody()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-doc-" + Guid.NewGuid().ToString("N") + ".md";
        var path = Path.Combine(lease.Workspace.Root, name);

        await File.WriteAllTextAsync(path, "# Title\r\nbody\r\n## Commands\r\nrun it\r\n", TestContext.Current.CancellationToken);

        try
        {
            var result = await FileService.ReadTextAsync(
                lease.Workspace,
                name,
                new FileService.ReadRequest(new FileService.LineRange(0, 0, 2000), true, null),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("## Commands", result.Value!, StringComparison.Ordinal);
            Assert.DoesNotContain("run it", result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditText_WithASection_ReplacesTheWholeSectionWithoutAnOldText()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-sec-" + Guid.NewGuid().ToString("N") + ".md";
        var path = Path.Combine(lease.Workspace.Root, name);

        await File.WriteAllTextAsync(path, "# Title\r\n\r\n## Commands\r\nold\r\n\r\n## After\r\nkeep\r\n", TestContext.Current.CancellationToken);

        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest(string.Empty, "## Commands\nnew", "## Commands", false, false, false),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);

            var after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

            Assert.Contains("new", after, StringComparison.Ordinal);
            Assert.DoesNotContain("old", after, StringComparison.Ordinal);
            Assert.Contains("## After", after, StringComparison.Ordinal);
            Assert.Contains("keep", after, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task<Result<string>> Read(LoadedWorkspace workspace, string name, int start, int end, int maxLines) =>
        FileService.ReadTextAsync(
            workspace,
            name,
            new FileService.ReadRequest(new FileService.LineRange(start, end, maxLines), false, null),
            TestContext.Current.CancellationToken);

    private static string WriteLargeFile(string root, out string path)
    {
        var name = "terse-large-" + Guid.NewGuid().ToString("N") + ".txt";
        var padding = new string('x', 40);

        path = Path.Combine(root, name);

        File.WriteAllLines(
            path,
            Enumerable.Range(1, LineCount).Select(number =>
                string.Create(CultureInfo.InvariantCulture, $"line {number} {padding}")));

        return name;
    }
    [Fact]
    public async Task WriteText_OnACSharpDocumentThatWouldNotCompile_IsRolledBackLikeASymbolEdit()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var document = lease.Workspace.Solution.Projects
            .SelectMany(project => project.Documents)
            .First(candidate => candidate.FilePath is { Length: > 0 });
        var before = await File.ReadAllTextAsync(document.FilePath!, TestContext.Current.CancellationToken);

        try
        {
            var result = await FileService.WriteTextAsync(
                lease.Workspace,
                document.FilePath!,
                before + "\nthis is not C#\n",
                dryRun: false,
                force: true,
                allowErrors: false,
                verbose: false,
                allowPolicy: false,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsOk);
            Assert.Equal(TerseErrorCode.CompileRegression, result.Error!.Code);
            Assert.Equal(before, await File.ReadAllTextAsync(document.FilePath!, TestContext.Current.CancellationToken));
        }
        finally
        {
            await File.WriteAllTextAsync(document.FilePath!, before, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void LineRange_WithANonPositiveCharacterBudget_FallsBackToTheDefault(int requested) =>
        Assert.Equal(FileService.DefaultResponseCharacters, new FileService.LineRange(0, 0, 100, requested).Budget);

    [Fact]
    public void LineRange_WithADefaultInstance_CarriesTheInlineableDefaultBudget()
    {
        Assert.Equal(FileService.DefaultResponseCharacters, default(FileService.LineRange).Budget);
        Assert.True(FileService.DefaultResponseCharacters < FileService.MaxResponseCharacters);
    }

    [Fact]
    public void LineRange_WithACallerBudget_KeepsIt() =>
        Assert.Equal(4096, new FileService.LineRange(0, 0, 100, 4096).Budget);

    [Fact]
    public async Task WriteText_OnACSharpDocumentWithoutAByteOrderMark_LeavesItWithoutOne()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var document = lease.Workspace.Solution.Projects
            .SelectMany(project => project.Documents)
            .First(candidate => candidate.FilePath is { Length: > 0 });
        var before = await File.ReadAllBytesAsync(document.FilePath!, TestContext.Current.CancellationToken);

        Assert.False(HasByteOrderMark(before));

        var text = await File.ReadAllTextAsync(document.FilePath!, TestContext.Current.CancellationToken);
        try
        {
            var result = await FileService.WriteTextAsync(
                lease.Workspace,
                document.FilePath!,
                text + "\n",
                dryRun: false,
                force: true,
                allowErrors: false,
                verbose: false,
                allowPolicy: false,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.False(HasByteOrderMark(
                await File.ReadAllBytesAsync(document.FilePath!, TestContext.Current.CancellationToken)));
        }
        finally
        {
            await File.WriteAllBytesAsync(document.FilePath!, before, TestContext.Current.CancellationToken);
        }
    }

    private static bool HasByteOrderMark(byte[] content) =>
        content is [0xEF, 0xBB, 0xBF, ..];

    [Fact]
    public async Task EditText_WithAnOldTextThatOnlyMatchesDedented_SaysSoAndSteersToTheSymbolTools()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-dedent-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(lease.Workspace.Root, name);
        await File.WriteAllTextAsync(
            path,
            "class Order\n{\n    public int Total()\n    {\n\n        return 1;\n    }\n}\n",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("public int Total()\n{\nreturn 1;\n}", "x", null, false, false, false),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsOk);
            Assert.Contains("indentation and blank lines", result.Error!.Remedy, StringComparison.Ordinal);
            Assert.Contains("replace_symbol_body", result.Error!.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1, "| PICKED | a |\r\n| row | b |\r\n| row | c |\r\n")]
    [InlineData(2, "| row | a |\r\n| PICKED | b |\r\n| row | c |\r\n")]
    [InlineData(3, "| row | a |\r\n| row | b |\r\n| PICKED | c |\r\n")]
    public async Task EditText_WithAnOccurrence_ReplacesThatMatchAndLeavesTheOthers(int occurrence, string expected)
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-rows-" + Guid.NewGuid().ToString("N") + ".md";
        var path = Path.Combine(lease.Workspace.Root, name);
        await File.WriteAllTextAsync(
            path,
            "| row | a |\r\n| row | b |\r\n| row | c |\r\n",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("| row |", "| PICKED |", null, false, false, false, occurrence),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditText_WithAnOccurrenceBeyondTheMatches_NamesTheRangeItCouldHavePicked()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-rows-" + Guid.NewGuid().ToString("N") + ".md";
        var path = Path.Combine(lease.Workspace.Root, name);
        await File.WriteAllTextAsync(path, "| row | a |\r\n| row | b |\r\n", TestContext.Current.CancellationToken);
        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("| row |", "x", null, false, false, false, 7),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsOk);
            Assert.Contains("occurrence=7 does not exist", result.Error!.Message, StringComparison.Ordinal);
            Assert.Contains("between 1 and 2", result.Error!.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadText_OnAFileOverTheDefaultCharacterBudget_ClipsItAndNamesTheContinuationLine()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var name = WriteLargeFile(lease.Workspace.Root, out var path);
        try
        {
            var result = await Read(lease.Workspace, name, 0, 0, 5000);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.True(
                result.Value!.Length <= FileService.DefaultResponseCharacters + 4096,
                string.Create(CultureInfo.InvariantCulture, $"the response was {result.Value!.Length} characters"));
            Assert.Contains("next: startLine=", result.Value!, StringComparison.Ordinal);
            Assert.Contains("total=5000", result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditText_WithAnOccurrenceAndAMultilineAnchorAgainstAWindowsFile_StillPicksTheNthMatch()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-crlf-" + Guid.NewGuid().ToString("N") + ".md";
        var path = Path.Combine(lease.Workspace.Root, name);
        await File.WriteAllTextAsync(
            path,
            "start\r\n| row |\r\n| a |\r\nstart\r\n| row |\r\n| b |\r\n",
            TestContext.Current.CancellationToken);
        try
        {
            var result = await FileService.EditTextAsync(
                lease.Workspace,
                name,
                new FileService.EditRequest("start\n| row |", "PICKED", null, false, false, false, 2),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal(
                "start\r\n| row |\r\n| a |\r\nPICKED\r\n| b |\r\n",
                await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
