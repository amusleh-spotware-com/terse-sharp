using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class LoadAdmissionTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void Describe_ASecondLargeSolutionThatWouldExhaustMemory_IsRefusedNamingWhatToUnload()
    {
        var refusal = LoadAdmission.Describe(new MemoryBudget(10 * Gigabyte, 140, 155, 32 * Gigabyte, 24 * Gigabyte), ["xTrader.Mac.slnx"]);

        Assert.NotNull(refusal);
        Assert.Equal(TerseErrorCode.InsufficientMemory, refusal.Code);
        Assert.Contains("xTrader.Mac.slnx", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("unload_workspace", refusal.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ASecondSolutionThatFits_IsAdmitted() => Assert.Null(LoadAdmission.Describe(new MemoryBudget(3 * Gigabyte, 40, 40, 64 * Gigabyte, 16 * Gigabyte), ["One.slnx"]));

    [Fact]
    public void Describe_ASmallLoadOnAnAlmostFullMachine_IsNeverRefused() => Assert.Null(LoadAdmission.Describe(new MemoryBudget(700L * 1024 * 1024, 1, 4, 7 * Gigabyte, 7 * Gigabyte), ["Fixture.slnx"]));
}
