using System.Diagnostics;

namespace TerseSharp.E2ETests;

public sealed class ProjectFileIntegrityE2ETests : IAsyncLifetime
{
    private TerseTempSolution solution = null!;

    public async ValueTask InitializeAsync() =>
        solution = await TerseTempSolution.StartAsync(watch: true, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await solution.DisposeAsync();

    [Fact]
    public async Task ExtractInterface_Applied_LeavesTheProjectFileByteIdenticalAndTheProjectBuildable()
    {
        var project = solution.ProjectPath;
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        var applied = await solution.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["interfaceName"] = "IOrderServiceExtracted",
        });

        Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);

        var after = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        Assert.Equal(before, after);

        var (exitCode, output) = await BuildAsync(project);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("NETSDK1022", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractInterface_TwiceOverTheSameProject_NeverAccumulatesDuplicateCompileItems()
    {
        var project = solution.ProjectPath;

        await solution.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["interfaceName"] = "IFirstExtracted",
        });
        await solution.CallAsync("extract_interface", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderBook",
            ["interfaceName"] = "ISecondExtracted",
        });

        var text = await File.ReadAllTextAsync(project, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("<Compile", text, StringComparison.Ordinal);

        var (exitCode, output) = await BuildAsync(project);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("NETSDK1022", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> BuildAsync(string project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(project)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("build");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nodeReuse:false");
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");

        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await output + await error);
    }
}
