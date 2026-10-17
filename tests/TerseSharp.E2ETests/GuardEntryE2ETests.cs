using System.Diagnostics;

namespace TerseSharp.E2ETests;

public sealed class GuardEntryE2ETests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "terse-guard-entry-" + Guid.NewGuid().ToString("N"));

    public GuardEntryE2ETests() => Directory.CreateDirectory(Path.Combine(sandbox, ".git"));

    [Fact]
    public async Task Guard_OnTheFastPath_DeniesAReadOfACSharpFileWithExitCodeZero()
    {
        var run = await GuardAsync("""{"tool_name":"Read","tool_input":{"file_path":"A.cs"}}""", "guard");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("\"permissionDecision\":\"deny\"", run.Output, StringComparison.Ordinal);
        Assert.Contains("get_file_outline", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guard_WithHelp_StillAnswersThroughTheCommandLine()
    {
        var run = await GuardAsync(string.Empty, "guard", "--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("--folder-rules", run.Output, StringComparison.Ordinal);
        Assert.Contains("--post-batch", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guard_WithAnUnknownFlag_IsStillRejectedByTheCommandLine()
    {
        var run = await GuardAsync(string.Empty, "guard", "--no-such-flag");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--no-such-flag", run.Output + run.Error, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(sandbox, recursive: true);

    private async Task<GuardRun> GuardAsync(string payload, params string[] arguments)
    {
        using var process = Process.Start(StartInfo(arguments)) ?? throw new InvalidOperationException("dotnet did not start");

        await process.StandardInput.WriteAsync(payload);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new GuardRun(process.ExitCode, await output, await error);
    }

    private sealed record GuardRun(int ExitCode, string Output, string Error);

    private ProcessStartInfo StartInfo(string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = sandbox,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(TerseServerFixture.ServerAssemblyPath());

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["TERSE_UPDATE"] = "0";
        start.Environment["TERSE_GUARD_LOG"] = string.Empty;

        return start;
    }
}
