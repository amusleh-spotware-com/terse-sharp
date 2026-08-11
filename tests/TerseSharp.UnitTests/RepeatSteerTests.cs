using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[Collection(nameof(RepeatSteerCollection))]
public sealed class RepeatSteerTests
{
    [Fact]
    public void Steer_SaysNothingUntilTheThirdCallOfTheSameTool()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.Equal("3 read_text calls in a row - pass paths=[...] for the rest", RepeatSteer.Steer("read_text"));
        Assert.Equal("4 read_text calls in a row - pass paths=[...] for the rest", RepeatSteer.Steer("read_text"));
    }

    [Fact]
    public void Steer_ResetsOnADifferentTool()
    {
        RepeatSteer.Forget();

        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.Null(RepeatSteer.Steer("read_text"));
        Assert.Null(RepeatSteer.Steer("build"));
        Assert.Null(RepeatSteer.Steer("read_text"));
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
    public void Steer_NamesThePluralParameterOfTheToolItRepeated(string tool, string plural)
    {
        RepeatSteer.Forget();

        RepeatSteer.Steer(tool);
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
}

[CollectionDefinition(nameof(RepeatSteerCollection), DisableParallelization = true)]
public sealed class RepeatSteerCollection;
