using ModelContextProtocol.Protocol;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolExamplesTests
{
    [Fact]
    public void Decorate_AppendsTheExampleToAPromotedToolOnceAndLeavesEveryOtherToolAlone()
    {
        var promoted = new Tool { Name = "find_files", Description = "Find files." };
        var plain = new Tool { Name = "get_symbol", Description = "Get a symbol." };
        Tool[] tools = [promoted, plain];

        ToolExamples.Decorate(tools);
        ToolExamples.Decorate(tools);

        Assert.Equal("Find files." + ToolExamples.Separator + ToolExamples.For("find_files"), promoted.Description);
        Assert.Equal("Get a symbol.", plain.Description);
        Assert.Equal(ToolExamples.DecorationLength("find_files"), promoted.Description!.Length - "Find files.".Length);
    }
}
