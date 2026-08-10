using Xunit;

namespace Selection.OtherTests;

public sealed class StandaloneTests
{
    [Fact]
    public void DependsOnNothingUnderTest() => Assert.True(true);
}
