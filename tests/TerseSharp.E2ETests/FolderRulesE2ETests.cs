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
        await GuardAsync(new JsonObject { ["hook_event_name"] = "PostToolBatch", ["session_id"] = "e2e-session", ["cwd"] = Repository, ["tool_calls"] = new JsonArray() }, "--post-batch");
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

    private async Task<string> GuardAsync(JsonObject payload, string flag = "--folder-rules")
    {
        using var process = Process.Start(Start(flag)) ?? throw new InvalidOperationException("dotnet did not start");

        await process.StandardInput.WriteAsync(payload.ToJsonString());
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode is 0, await error);

        return await output;
    }

    private ProcessStartInfo Start(string flag)
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
        start.ArgumentList.Add(flag);
        start.Environment["TERSE_HOME"] = Home;
        start.Environment["TERSE_UPDATE"] = "0";

        return start;
    }

    [Fact]
    public async Task GuardFolderRules_AllowsADryRunWriteBelowUnseenRules_AndStillDeniesTheRealOne()
    {
        var dryRun = Payload("PreToolUse", "mcp__terse-sharp__write_text", "tests/Unit/NewTests.cs");
        dryRun["tool_input"]!["dryRun"] = true;

        var allowed = await GuardAsync(dryRun);
        var denied = JsonNode.Parse(await GuardAsync(Payload("PreToolUse", "mcp__terse-sharp__write_text", "tests/Unit/NewTests.cs")))!["hookSpecificOutput"]!;

        Assert.Empty(allowed.Trim());
        Assert.Equal("deny", denied["permissionDecision"]!.GetValue<string>());
    }

    [Fact]
    public async Task GuardPostBatch_EndsTheResponseSoASiblingWriteIsDeniedAndTheNextResponsesRetryIsNot()
    {
        var write = Payload("PreToolUse", "mcp__terse-sharp__write_text", "tests/Unit/NewTests.cs");
        var first = await GuardAsync(write);
        var sibling = await GuardAsync(write);
        await GuardAsync(new JsonObject { ["hook_event_name"] = "PostToolBatch", ["session_id"] = "e2e-session", ["cwd"] = Repository, ["tool_calls"] = new JsonArray() }, "--post-batch");
        var retry = await GuardAsync(write);

        Assert.Equal("deny", JsonNode.Parse(first)!["hookSpecificOutput"]!["permissionDecision"]!.GetValue<string>());
        Assert.Contains("Use NUnit classic asserts only.", JsonNode.Parse(sibling)!["hookSpecificOutput"]!["permissionDecisionReason"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(retry.Trim());
    }

    [Fact]
    public async Task GuardFolderRules_AppendsANestedClaudeMdOverTheContextCapWholeToTheToolResult()
    {
        var rules = "# Tests\n\n1. `[SetUp]` method first\n" + string.Concat(Enumerable.Repeat("Background prose that explains the layout of the suite.\n", 600));
        await File.WriteAllTextAsync(Path.Combine(Repository, "tests", "CLAUDE.md"), rules, TestContext.Current.CancellationToken);
        var payload = Payload("PostToolUse", "mcp__terse-sharp__get_file_outline", "tests/Unit/OrderTests.cs");
        payload["tool_response"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "class OrderTests" });

        var blocks = JsonNode.Parse(await GuardAsync(payload))!["hookSpecificOutput"]!["updatedToolOutput"]!.AsArray();

        Assert.Equal("class OrderTests", blocks[0]!["text"]!.GetValue<string>());
        Assert.Contains(rules.TrimEnd(), blocks[1]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
