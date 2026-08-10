using Selection.Core;
using Xunit;

namespace Selection.CoreTests;

public sealed class AdderTests
{
    [Fact]
    public void Add_SumsBothOperands() => Assert.Equal(7, Adder.Add(3, 4));
}
