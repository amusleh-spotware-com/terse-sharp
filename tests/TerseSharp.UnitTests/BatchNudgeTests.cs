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
        var hook = JsonNode.Parse(BatchNudge.Decide(payload)!)!["hookSpecificOutput"]!;

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
        Assert.Null(BatchNudge.Decide(payload));

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
}
