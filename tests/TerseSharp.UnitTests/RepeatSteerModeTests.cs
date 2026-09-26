using System.Text.Json;
using ModelContextProtocol.Protocol;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[Collection(nameof(RepeatSteerCollection))]
public sealed class RepeatSteerModeTests
{
    [Fact]
    public void Steer_ForASecondCallWhoseDryRunDiffersFromTheFirst_SaysNothingBecauseOneCallCannotCarryBoth()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("replace_symbol", value: "SymbolEditService.Rewritten", mode: "dryRun=true;"));
        Assert.Null(RepeatSteer.Steer("replace_symbol", value: "OrderService.Submit", mode: string.Empty));
    }

    [Fact]
    public void Steer_ForCallsAgainstDifferentWorkspaces_SaysNothing()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", value: "src/A.cs", mode: "workspace=\"agent-1\";"));
        Assert.Null(RepeatSteer.Steer("read_text", value: "src/B.cs", mode: "workspace=\"TerseSharp\";"));
    }

    [Fact]
    public void Steer_ForTwoCallsSharingTheirCallWideArguments_StillNamesTheConcreteBatch()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("replace_symbol", value: "OrderService.Submit", mode: "dryRun=true;"));
        Assert.Equal(
            "2 replace_symbol calls in a row - these are ONE call: symbolIds=[\"OrderService.Submit\", \"OrderService.Cancel\"]",
            RepeatSteer.Steer("replace_symbol", value: "OrderService.Cancel", mode: "dryRun=true;"));
    }

    [Fact]
    public void Steer_WhenTheModeChangesMidRun_CountsTheNewRunFromOne()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", value: "src/A.cs"));
        Assert.Null(RepeatSteer.Steer("read_text", value: "src/B.cs", mode: "verbose=true;"));
        Assert.Equal(
            "2 read_text calls in a row - these are ONE call: paths=[\"src/B.cs\", \"src/C.cs\"]",
            RepeatSteer.Steer("read_text", value: "src/C.cs", mode: "verbose=true;"));
    }

    [Fact]
    public void Mode_NamesOnlyTheCallWideArgumentsThatAreSet_InAFixedOrder()
    {
        var parameters = new CallToolRequestParams
        {
            Name = "replace_symbol",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["verbose"] = JsonSerializer.SerializeToElement(false),
                ["symbolId"] = JsonSerializer.SerializeToElement("OrderService.Submit"),
                ["workspace"] = JsonSerializer.SerializeToElement("TerseSharp"),
                ["dryRun"] = JsonSerializer.SerializeToElement(true),
            },
        };

        Assert.Equal("dryRun=true;workspace=\"TerseSharp\";", RepeatSteer.Mode(parameters));
    }

    [Fact]
    public void Mode_ForACallWithNoArguments_IsEmpty() =>
        Assert.Equal(string.Empty, RepeatSteer.Mode(new CallToolRequestParams { Name = "read_text" }));
}
