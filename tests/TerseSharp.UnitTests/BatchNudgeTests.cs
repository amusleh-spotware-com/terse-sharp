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

    private const string OddHashPayload = """{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"test"}]}""";
    private const string EvenHashPayload = """{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"abc"}]}""";

    [Theory]
    [InlineData(OddHashPayload, "test")]
    [InlineData(EvenHashPayload, "abc")]
    public void Classify_ForTheIdTheHoldOutUsedToHoldAndTheOneItNudged_NudgesBoth(string payload, string toolUseId)
    {
        var classified = BatchNudge.Classify(payload);

        Assert.Equal(toolUseId, classified);
        Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(EvenHashPayload)), BatchNudge.Decide(classified));
    }

    [Fact]
    public void Decide_ForEveryEligibleToolUseId_NudgesAllOfThem()
    {
        var nudged = Enumerable.Range(0, 200)
            .Select(index => "toolu_" + index.ToString("D3", CultureInfo.InvariantCulture))
            .Count(id => BatchNudge.Decide(BatchNudge.Classify("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__read_text","tool_use_id":""" + "\"" + id + "\"}]}")) is not null);

        Assert.Equal(200, nudged);
    }

    [Theory]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":"a b"}]}""")]
    [InlineData("""{"tool_calls":[{"tool_name":"mcp__terse-sharp__get_file_outline","tool_use_id":7}]}""")]
    public void Classify_ForABatchWithNoUsableToolUseId_NudgesAndRecordsItUnidentified(string payload)
    {
        var classified = BatchNudge.Classify(payload);

        Assert.Equal(NudgeLog.Unidentified, classified);
        Assert.NotNull(BatchNudge.Decide(classified));
    }

    [Theory]
    [InlineData(OddHashPayload, " test nudged")]
    [InlineData(EvenHashPayload, " abc nudged")]
    public async Task RunAsync_WithTheLogOptedIn_LogsEveryEligibleBatchAndNudgesIt(string payload, string logTail)
    {
        var home = Directory.CreateTempSubdirectory("terse-nudge-");

        try
        {
            using var output = new StringWriter();

            await BatchNudge.RunAsync(new StringReader(payload), output, home.FullName, log: true, TestContext.Current.CancellationToken);

            var logged = await File.ReadAllLinesAsync(Path.Combine(home.FullName, ".terse", NudgeLog.FileName), TestContext.Current.CancellationToken);
            Assert.EndsWith(logTail, Assert.Single(logged), StringComparison.Ordinal);
            Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(payload)) + output.NewLine, output.ToString());
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(OddHashPayload)]
    [InlineData(EvenHashPayload)]
    public async Task RunAsync_WithoutTheLogOptIn_NudgesAndWritesNoLog(string payload)
    {
        var home = Directory.CreateTempSubdirectory("terse-nudge-");

        try
        {
            using var output = new StringWriter();

            await BatchNudge.RunAsync(new StringReader(payload), output, home.FullName, log: false, TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(Path.Combine(home.FullName, ".terse")));
            Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(payload)) + output.NewLine, output.ToString());
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData(null, false)]
    public void Enabled_IsOnOnlyWhenTheOptInVariableIsOne(string? value, bool enabled)
    {
        var previous = Environment.GetEnvironmentVariable(NudgeLog.Variable);
        Environment.SetEnvironmentVariable(NudgeLog.Variable, value);

        try
        {
            Assert.Equal(enabled, NudgeLog.Enabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(NudgeLog.Variable, previous);
        }
    }

    [Fact]
    public async Task RunAsync_WhenTheLogCannotBeWritten_StillNudgesAndExitsZero()
    {
        var home = Path.GetTempFileName();

        try
        {
            using var output = new StringWriter();

            var exit = await BatchNudge.RunAsync(new StringReader(EvenHashPayload), output, home, log: true, TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            Assert.Equal(BatchNudge.Decide(BatchNudge.Classify(EvenHashPayload)) + output.NewLine, output.ToString());
        }
        finally
        {
            File.Delete(home);
        }
    }
}
