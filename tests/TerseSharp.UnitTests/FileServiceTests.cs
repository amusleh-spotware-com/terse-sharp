using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

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
            var result = FileService.ReadText(lease.Workspace, name, 4000, 4002, 2000, TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("4000: line 4000 ", result.Value!, StringComparison.Ordinal);
            Assert.Contains("total=5000", result.Value!, StringComparison.Ordinal);
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
            var result = FileService.ReadText(lease.Workspace, name, 0, 0, 10, TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("10 lines (truncated=true, total=5000)", result.Value!, StringComparison.Ordinal);
            Assert.DoesNotContain("11: ", result.Value!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

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
    public async Task ReadText_WithASingleLineOverTheResponseBudget_TruncatesItAndSaysByHowMuch()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;
        var name = "terse-wide-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(lease.Workspace.Root, name);

        File.WriteAllText(path, new string('x', 200_000));

        try
        {
            var result = FileService.ReadText(lease.Workspace, name, 0, 0, 2000, TestContext.Current.CancellationToken);

            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Contains("... (+", result.Value!, StringComparison.Ordinal);
            Assert.True(result.Value!.Length < 200_000, "the response was not truncated");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
