using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TerseSharp.E2ETests;

public sealed class FolderRulesE2ETests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "terse-e2e-folder-rules", Guid.NewGuid().ToString("N"));

    public FolderRulesE2ETests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Directory.CreateDirectory(Path.Combine(Repository, "tests", "Unit"));
        Directory.CreateDirectory(Home);
        File.WriteAllText(Path.Combine(Repository, "tests", "CLAUDE.md"), "Use NUnit classic asserts only.");
        File.WriteAllText(Path.Combine(Repository, "tests", "Unit", "OrderTests.cs"), "class OrderTests;");
    }

    private string Repository => Path.Combine(sandbox, "repo");

    private string Home => Path.Combine(sandbox, "home");

    [Fact]
    public async Task GuardFolderRules_InjectsANestedClaudeMdAfterATerseRead_AndDoesNotStopAWriteBelowIt()
    {
        var injected = await GuardAsync(Payload("PostToolUse", "mcp__terse-sharp__get_file_outline", "tests/Unit/OrderTests.cs"));
        var write = await GuardAsync(Payload("PreToolUse", "mcp__terse-sharp__write_text", "tests/Unit/NewTests.cs"));

        var output = JsonNode.Parse(injected)!["hookSpecificOutput"]!;
        Assert.Equal("PostToolUse", output["hookEventName"]!.GetValue<string>());
        Assert.Contains("Use NUnit classic asserts only.", output["additionalContext"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(write.Trim());
    }

    [Fact]
    public async Task GuardFolderRules_DeniesTheFirstTerseWriteBelowRulesTheSessionHasNotSeen()
    {
        var denied = await GuardAsync(Payload("PreToolUse", "mcp__terse-sharp__write_text", "tests/Unit/NewTests.cs"));

        var output = JsonNode.Parse(denied)!["hookSpecificOutput"]!;
        Assert.Equal("deny", output["permissionDecision"]!.GetValue<string>());
        Assert.Contains("Use NUnit classic asserts only.", output["permissionDecisionReason"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(sandbox, recursive: true);

    private JsonObject Payload(string hookEvent, string tool, string path) => new()
    {
        ["hook_event_name"] = hookEvent,
        ["session_id"] = "e2e-session",
        ["cwd"] = Repository,
        ["tool_name"] = tool,
        ["tool_input"] = new JsonObject { ["path"] = path },
    };

    private async Task<string> GuardAsync(JsonObject payload)
    {
        using var process = Process.Start(Start()) ?? throw new InvalidOperationException("dotnet did not start");

        await process.StandardInput.WriteAsync(payload.ToJsonString());
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode is 0, await error);

        return await output;
    }

    private ProcessStartInfo Start()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        start.ArgumentList.Add(TerseServerFixture.ServerAssemblyPath());
        start.ArgumentList.Add("guard");
        start.ArgumentList.Add("--folder-rules");
        start.Environment["TERSE_HOME"] = Home;
        start.Environment["TERSE_UPDATE"] = "0";

        return start;
    }
}
