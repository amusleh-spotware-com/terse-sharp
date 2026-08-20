using Mtp.Trading;
using Xunit;

namespace Mtp.Trading.Tests;

public sealed class LedgerTests
{
    [Fact]
    public void Balance_SubtractsTheDebitsFromTheCredits()
    {
        Assert.Equal(7, Ledger.Balance(10, 3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Balance_WithNoDebits_IsTheCredits(int credits)
    {
        Assert.Equal(credits, Ledger.Balance(credits, 0));
    }
}

public sealed class DeliberateMtpOutcomesTests
{
    [Fact]
    public void FailsAssertion()
    {
        Assert.Equal(4, 2 + 3);
    }

    [Fact(Skip = "deliberately skipped so the fixture exercises the skipped counter")]
    public void SkippedByDesign()
    {
        Assert.Fail("never runs");
    }
}
