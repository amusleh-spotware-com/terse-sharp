using Xunit;

namespace Fixture.Warning.Tests;

public sealed class CalculatorTests
{
    [Fact]
    public void TotalAddsOneAndOne() => Assert.Equal(2, new Calculator().Total());

    [Fact]
    public void FailsByDesignSoRerunFailedHasSomethingToRerun() => Assert.Equal(3, new Calculator().Total());
}
