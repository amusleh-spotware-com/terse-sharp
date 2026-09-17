using System.Diagnostics;

namespace TerseSharp.E2ETests;

public sealed class ProjectFileIntegrityE2ETests : IAsyncLifetime
{
    private TerseTempSolution solution = null!;

    public async ValueTask InitializeAsync() =>
        solution = await TerseTempSolution.StartAsync(watch: true, TestContext.Current.CancellationToken, HandFormatAsync);

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

    [Fact]
    public async Task WriteText_CreatingANewSourceFile_LeavesAHandFormattedProjectFileByteIdentical()
    {
        var project = solution.ProjectPath;
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        var applied = await solution.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/ProbeAdded.cs",
            ["content"] = "namespace Fixture.Trading;\n\ninternal static class ProbeAdded;\n",
            ["force"] = true,
        });

        Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);

        var after = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        Assert.Equal(before, after);

        var (exitCode, output) = await BuildAsync(project);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("NETSDK1022", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_DeletingAFileItJustCreated_LeavesTheProjectFileByteIdentical()
    {
        var project = solution.ProjectPath;
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        await solution.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/ProbeRoundTrip.cs",
            ["content"] = "namespace Fixture.Trading;\n\ninternal static class ProbeRoundTrip;\n",
            ["force"] = true,
        });

        var deleted = await solution.CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Trading/ProbeRoundTrip.cs",
            ["delete"] = true,
            ["force"] = true,
        });

        Assert.DoesNotContain("ERROR", deleted, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(solution.ProjectDirectory, "ProbeRoundTrip.cs")));
        Assert.Equal(before, await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken));
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

    private static readonly string HandFormattedProject = string.Join('\n',
    [
        "<Project Sdk=\"Microsoft.NET.Sdk\">",
        "\t<ItemGroup>",
        "\t\t<EmbeddedResource Remove=\"**\\*.resx\"/>",
        "\t</ItemGroup>",
        "",
        "\t<PropertyGroup>",
        "\t\t<RootNamespace>Fixture.Trading</RootNamespace>",
        "\t</PropertyGroup>",
        "</Project>",
        "",
    ]);

    private static Task HandFormatAsync(string root) =>
        File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Fixture.Trading.csproj"), HandFormattedProject);
}
