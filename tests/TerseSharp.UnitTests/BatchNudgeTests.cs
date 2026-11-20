using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class BatchNudgeTests
{
    private const string NudgedPayload = """{"hook_event_name":"PostToolBatch","session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"toolu_1"}]}""";
    private const string HeldPayload = """{"hook_event_name":"PostToolBatch","session_id":"test","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"toolu_1"}]}""";

    [Theory]
    [InlineData(NudgedPayload)]
    [InlineData("""{"session_id":"abc","tools":[{"name":"mcp__terse__search_text"}]}""")]
    public void Decide_ForOneTerseReadCallInANudgeArmSession_InjectsTheBatchingNudge(string payload)
    {
        var hook = JsonNode.Parse(BatchNudge.Decide(BatchNudge.Classify(payload))!)!["hookSpecificOutput"]!;

        Assert.Equal("PostToolBatch", hook["hookEventName"]!.GetValue<string>());
        Assert.Equal(BatchNudge.Nudge, hook["additionalContext"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"},{"tool_name":"mcp__terse-sharp__read_text"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__replace_symbol"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__build"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"Read"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__other__get_file_outline"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[]}""")]
    [InlineData("""{"session_id":"abc","tool_name":"mcp__terse-sharp__get_file_outline"}""")]
    [InlineData("""{"session_id":"abc","tool_calls":"mcp__terse-sharp__get_file_outline"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Classify_ForAnythingButOneTerseReadCall_IsInNeitherArmAndInjectsNothing(string payload)
    {
        var verdict = BatchNudge.Classify(payload);

        Assert.Null(verdict);
        Assert.Null(BatchNudge.Decide(verdict));
    }

    [Theory]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"toolu_1"}]}""")]
    [InlineData("""{"session_id":"","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    [InlineData("""{"session_id":"a b","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    [InlineData("""{"session_id":7,"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    public void Classify_ForABatchWithNoUsableSessionId_IsInNeitherArm(string payload) =>
        Assert.Null(BatchNudge.Classify(payload));

    [Theory]
    [InlineData("test", true)]
    [InlineData("abc", false)]
    public void IsHeldOut_IsTheParityOfTheFirstSha256ByteOfTheSessionId(string sessionId, bool held) =>
        Assert.Equal(held, NudgeHoldOut.IsHeldOut(sessionId));

    [Fact]
    public void IsHeldOut_SplitsSessionIdsIntoTwoHalves()
    {
        var held = Enumerable.Range(0, 400)
            .Count(index => NudgeHoldOut.IsHeldOut(new Guid(index, 0, 0, new byte[8]).ToString()));

        Assert.InRange(held, 150, 250);
    }

    [Fact]
    public void Classify_ForEveryBatchOfOneSession_KeepsThatSessionsArm()
    {
        NudgeArm[] expected = [NudgeArm.Held];

        var arms = Enumerable.Range(0, 50)
            .Select(index => BatchNudge.Classify(HeldPayload.Replace("toolu_1", new Guid(index, 0, 0, new byte[8]).ToString("N"), StringComparison.Ordinal))!.Value.Arm)
            .Distinct();

        Assert.Equal(expected, arms);
    }

    [Fact]
    public void Classify_ForAHeldOutSession_DecidesHeldAndInjectsNothing()
    {
        var verdict = BatchNudge.Classify(HeldPayload);

        Assert.Equal(new BatchVerdict("test", "toolu_1", NudgeArm.Held), verdict);
        Assert.Null(BatchNudge.Decide(verdict));
        Assert.NotNull(BatchNudge.Decide(BatchNudge.Classify(NudgedPayload)));
    }

    [Theory]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"a b"}]}""")]
    [InlineData("""{"session_id":"abc","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":7}]}""")]
    public void Classify_ForABatchWithNoUsableToolUseId_KeepsTheSessionsArmAndRecordsItUnidentified(string payload) =>
        Assert.Equal(new BatchVerdict("abc", BatchNudge.Unidentified, NudgeArm.Nudged), BatchNudge.Classify(payload));

    [Theory]
    [InlineData(HeldPayload, " test toolu_1 held", false)]
    [InlineData(NudgedPayload, " abc toolu_1 nudged", true)]
    public async Task RunAsync_ForAnEligibleBatch_LogsItsSessionArmAndNudgesOnlyTheNudgeArm(string payload, string logTail, bool nudged)
    {
        var home = Directory.CreateTempSubdirectory("terse-nudge-");

        try
        {
            using var output = new StringWriter();

            await BatchNudge.RunAsync(new StringReader(payload), output, home.FullName, TestContext.Current.CancellationToken);

            var logged = await File.ReadAllLinesAsync(Path.Combine(home.FullName, ".terse", NudgeHoldOut.LogFileName), TestContext.Current.CancellationToken);
            Assert.EndsWith(logTail, Assert.Single(logged), StringComparison.Ordinal);
            Assert.Equal(nudged ? BatchNudge.Decide(BatchNudge.Classify(payload)) + output.NewLine : string.Empty, output.ToString());
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenTheLogCannotBeWritten_StillNudgesAndExitsZero()
    {
        var home = Path.GetTempFileName();

        try
        {
            using var output = new StringWriter();

            var exit = await BatchNudge.RunAsync(new StringReader(NudgedPayload), output, home, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(NudgedPayload)) + output.NewLine, output.ToString());
        }
        finally
        {
            File.Delete(home);
        }
    }

    [Fact]
    public async Task RunAsync_ForABatchItDoesNotRecognise_WritesNothing()
    {
        using var output = new StringWriter();

        await BatchNudge.RunAsync(new StringReader("""{"session_id":"abc","tool_calls":[]}"""), output, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task InstalledPostToolBatchCommand_RoutesToTheNudge_AndNudgesANudgeArmSession()
    {
        var command = ClientRegistrar.Guarded([], folderRules: true)["hooks"]!["PostToolBatch"]!.AsArray()
            .Select(entry => entry!["hooks"]![0]!["command"]!.GetValue<string>())
            .Single(text => text.StartsWith("terse ", StringComparison.Ordinal));
        using var output = new StringWriter();

        var hook = GuardEntry.Route([.. command.Split(' ').AsSpan(1)]);
        var exit = await GuardEntry.DispatchAsync(Assert.NotNull(hook), new StringReader(NudgedPayload), output, TestContext.Current.CancellationToken);

        Assert.Equal(GuardHook.PostBatch, hook);
        Assert.Equal(0, exit);
        Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(NudgedPayload)) + output.NewLine, output.ToString());
    }

    [Fact]
    public void EveryNudgedTool_IsADeclaredTerseTool()
    {
        var declared = typeof(ToolGuard).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.NotEmpty(BatchNudge.Reads);
        Assert.All(BatchNudge.Reads, tool => Assert.Contains(tool, declared));
    }

    [Fact]
    public void EveryNudgedTool_IsDeclaredReadOnly()
    {
        var readOnly = typeof(ToolGuard).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .OfType<McpServerToolAttribute>()
            .Where(attribute => attribute.ReadOnly)
            .Select(attribute => attribute.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("analyze", readOnly);
        Assert.NotEmpty(BatchNudge.Reads);
        Assert.All(BatchNudge.Reads, tool => Assert.Contains(tool, readOnly));
    }
}
