using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[Collection(nameof(RepeatSteerCollection))]
public sealed class RepeatSteerTests
{
    [Fact]
    public void Steer_SaysNothingUntilTheSecondCallOfTheSameTool()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.Equal("2 read_text calls in a row - pass paths=[...] with the next 2+ in ONE call", RepeatSteer.Steer("read_text"));
        Assert.Equal("3 read_text calls in a row - pass paths=[...] with the next 3+ in ONE call", RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_ResetsOnADifferentTool()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.NotNull(RepeatSteer.Steer("read_text"));
        Assert.Null(RepeatSteer.Steer("build"));
        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.NotNull(RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_SaysNothingForAToolThatDeclaresNoPluralParameter()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("build"));
        Assert.Null(RepeatSteer.Steer("build"));
        Assert.Null(RepeatSteer.Steer("build"));
        Assert.Null(RepeatSteer.Steer("build"));
    }

    [Theory]
    [InlineData("get_symbol_source", "symbolIds")]
    [InlineData("search_text", "queries")]
    [InlineData("run_tests", "projects")]
    [InlineData("write_text", "files")]
    [InlineData("edit_text", "edits")]
    [InlineData("resx_set", "entries")]
    public void Steer_NamesThePluralParameterOfTheToolItRepeated(string tool, string plural)
    {
        RepeatSteer.Forget();

        RepeatSteer.Steer(tool);

        Assert.Contains(plural + "=[...]", RepeatSteer.Steer(tool)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Steer_SaysNothingToAnAgentThatIsAlreadyBatching()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", batched: true));
        Assert.Null(RepeatSteer.Steer("read_text", batched: true));
        Assert.Null(RepeatSteer.Steer("read_text", batched: true));
        Assert.NotNull(RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_ForARangedRead_SaysNothingAndBreaksTheRun()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.NotNull(RepeatSteer.Steer("read_text"));
        Assert.Null(RepeatSteer.Steer("read_text", batched: false, unbatchable: true));
        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.NotNull(RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_ForASequenceOfRangedReads_NeverOffersABatchThatCannotAnswerThem()
    {
        RepeatSteer.Forget();

        for (var call = 0; call < 5; call++)
            Assert.Null(RepeatSteer.Steer("read_text", batched: false, unbatchable: true));
    }

    [Theory]
    [InlineData("startLine", true)]
    [InlineData("endLine", true)]
    [InlineData("tail", true)]
    [InlineData("section", true)]
    [InlineData("maxLines", false)]
    [InlineData("verbose", false)]
    [InlineData("bytes", false)]
    public void Unbatchable_IsExactlyTheReadArgumentsPathsCannotExpressPerEntry(string argument, bool expected)
    {
        var parameters = new ModelContextProtocol.Protocol.CallToolRequestParams
        {
            Name = "read_text",
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["path"] = System.Text.Json.JsonDocument.Parse("\"a.md\"").RootElement,
                [argument] = System.Text.Json.JsonDocument.Parse("1").RootElement,
            },
        };

        Assert.Equal(expected, RepeatSteer.Unbatchable(parameters, "read_text"));
        Assert.False(RepeatSteer.Unbatchable(parameters, "get_file_outline"));
    }

    [Fact]
    public void Steer_WhenEveryCallOfTheRunCarriedItsSingularArgument_NamesTheConcreteBatchInsteadOfAPlaceholder()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", value: "src/A.cs"));

        Assert.Equal(
            "2 read_text calls in a row - these are ONE call: paths=[\"src/A.cs\", \"src/B.cs\"]",
            RepeatSteer.Steer("read_text", value: "src/B.cs"));
    }

    [Fact]
    public void Steer_WhenACallOfTheRunCarriedNoSingularArgument_FallsBackToThePlaceholder()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", value: "src/A.cs"));

        Assert.Equal(
            "2 read_text calls in a row - pass paths=[...] with the next 2+ in ONE call",
            RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_ForAnArgumentLongerThanTheCap_FallsBackToThePlaceholderRatherThanEchoingIt()
    {
        RepeatSteer.Forget();

        var oversized = new string('x', 200);

        Assert.Null(RepeatSteer.Steer("search_text", value: oversized));

        var steer = RepeatSteer.Steer("search_text", value: oversized);

        Assert.Equal("2 search_text calls in a row - pass queries=[...] with the next 2+ in ONE call", steer);
        Assert.DoesNotContain(oversized, steer, StringComparison.Ordinal);
    }

    [Fact]
    public void Singular_NamesNoToolWhoseSingularArgumentIsAPayloadRatherThanAnIdentifier()
    {
        string[] payloadCarrying = ["add_member", "write_text", "edit_text", "resx_set"];

        Assert.All(payloadCarrying, tool =>
        {
            Assert.Contains(tool, RepeatSteer.Plural.Keys, StringComparer.Ordinal);
            Assert.DoesNotContain(tool, RepeatSteer.Singular.Keys, StringComparer.Ordinal);
        });

        Assert.All(RepeatSteer.Singular.Keys, tool => Assert.Contains(tool, RepeatSteer.Plural.Keys, StringComparer.Ordinal));
    }

    [Fact]
    public void Steer_WhenTheRunKeepsAskingForTheSameTarget_FallsBackRatherThanOfferingARepeatedBatch()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text", value: "src/A.cs"));

        var steer = RepeatSteer.Steer("read_text", value: "src/A.cs");

        Assert.Equal("2 read_text calls in a row - pass paths=[...] with the next 2+ in ONE call", steer);
        Assert.DoesNotContain("\"src/A.cs\", \"src/A.cs\"", steer, StringComparison.Ordinal);
    }
}

[CollectionDefinition(nameof(RepeatSteerCollection), DisableParallelization = true)]
public sealed class RepeatSteerCollection;
