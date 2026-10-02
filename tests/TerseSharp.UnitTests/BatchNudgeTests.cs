using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class BatchNudgeTests
{
    [Theory]
    [InlineData("""{"hook_event_name":"PostToolBatch","tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_input":{"path":"a.cs"}}]}""")]
    [InlineData("""{"tools":[{"name":"mcp__terse__search_text"}]}""")]
    public void Decide_ForABatchOfOneTerseReadCall_InjectsTheBatchingNudge(string payload)
    {
        var hook = JsonNode.Parse(BatchNudge.Decide(BatchNudge.Classify(payload))!)!["hookSpecificOutput"]!;

        Assert.Equal("PostToolBatch", hook["hookEventName"]!.GetValue<string>());
        Assert.Equal(BatchNudge.Nudge, hook["additionalContext"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"},{"tool_name":"mcp__terse-sharp__read_text"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__replace_symbol"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__build"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"Read"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__other__get_file_outline"}]}""")]
    [InlineData("""{"tool_calls":[]}""")]
    [InlineData("""{"tool_name":"mcp__terse-sharp__get_file_outline"}""")]
    [InlineData("""{"tool_calls":"mcp__terse-sharp__get_file_outline"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Decide_ForAnythingButOneTerseReadCall_InjectsNothing(string payload) =>
        Assert.Null(BatchNudge.Decide(BatchNudge.Classify(payload)));

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
    public async Task RunAsync_ForABatchItDoesNotRecognise_WritesNothing()
    {
        using var output = new StringWriter();

        await BatchNudge.RunAsync(new StringReader("""{"tool_calls":[]}"""), output, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, output.ToString());
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

    private const string HeldPayload = """{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"test"}]}""";
    private const string NudgedPayload = """{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"abc"}]}""";

    [Theory]
    [InlineData("test", true)]
    [InlineData("abc", false)]
    public void IsHeldOut_IsTheParityOfTheFirstSha256ByteTheScanRecomputes(string toolUseId, bool held) =>
        Assert.Equal(held, NudgeHoldOut.IsHeldOut(toolUseId));

    [Fact]
    public void IsHeldOut_SplitsToolUseIdsIntoTwoHalves()
    {
        var held = Enumerable.Range(0, 200)
            .Count(index => NudgeHoldOut.IsHeldOut("toolu_" + index.ToString("D3", CultureInfo.InvariantCulture)));

        Assert.InRange(held, 60, 140);
    }

    [Fact]
    public void Classify_ForAHeldOutToolUseId_DecidesHeldAndInjectsNothing()
    {
        var verdict = BatchNudge.Classify(HeldPayload);

        Assert.Equal(new BatchVerdict("test", NudgeArm.Held), verdict);
        Assert.Null(BatchNudge.Decide(verdict));
        Assert.NotNull(BatchNudge.Decide(BatchNudge.Classify(NudgedPayload)));
    }

    [Theory]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"a b"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":7}]}""")]
    public void Classify_ForABatchWithNoUsableToolUseId_NudgesAndRecordsItUnidentified(string payload) =>
        Assert.Equal(new BatchVerdict(NudgeHoldOut.Unidentified, NudgeArm.Nudged), BatchNudge.Classify(payload));

    [Theory]
    [InlineData(HeldPayload, " test held", false)]
    [InlineData(NudgedPayload, " abc nudged", true)]
    public async Task RunAsync_ForAnEligibleBatch_LogsItsArmAndNudgesOnlyTheNudgedHalf(string payload, string logTail, bool nudged)
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
}
