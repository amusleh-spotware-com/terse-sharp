using Xunit;

namespace Fixture.Trading.Tests;

public sealed class DeliberateOutcomesTests
{
    [Fact]
    public void Succeeds()
    {
        var repository = new InMemoryOrderRepository();

        Assert.Equal(0, repository.PendingCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PassesWithData(int volume)
    {
        Assert.True(volume > 0);
    }

    [Fact]
    public void FailsAssertion()
    {
        Assert.Equal(4, 2 + 3);
    }

    [Theory]
    [InlineData(0)]
    public void FailsWithData(int volume)
    {
        Assert.True(volume > 0);
    }

    [Fact]
    public void Throws()
    {
        throw new InvalidOperationException("probe boom");
    }

    [Fact(Skip = "deliberately skipped so the fixture exercises the skipped counter")]
    public void SkippedByDesign()
    {
        Assert.Fail("never runs");
    }
}
