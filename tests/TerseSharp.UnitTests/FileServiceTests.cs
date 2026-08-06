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
            Assert.Contains("3/5000 lines truncated", result.Value!, StringComparison.Ordinal);
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
            Assert.Equal(FileService.MaxResponseCharacters, new FileService.LineRange(0, 0, 100, requested).Budget);


    [Fact]
    public void LineRange_WithADefaultInstance_StillCarriesTheFullBudget() =>
        Assert.Equal(FileService.MaxResponseCharacters, default(FileService.LineRange).Budget);


    [Fact]
    public void LineRange_WithACallerBudget_KeepsIt() =>
        Assert.Equal(4096, new FileService.LineRange(0, 0, 100, 4096).Budget);
}
