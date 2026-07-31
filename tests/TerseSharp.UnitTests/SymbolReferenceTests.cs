using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SymbolReferenceTests
{
    [Theory]
    [InlineData("M:Ns.Type.Member(Ns.Arg)")]
    [InlineData("T:Ns.Type")]
    [InlineData("P:Ns.Type.Value")]
    [InlineData("F:Ns.Type.Field")]
    public void IsDocumentationId_ForARealId_IsTrue(string text) =>
        Assert.True(SymbolReference.IsDocumentationId(text));

    [Theory]
    [InlineData("OrderService.Submit")]
    [InlineData("Submit")]
    [InlineData("OrderService.Submit(Order)")]
    [InlineData("a:b")]
    public void IsDocumentationId_ForAName_IsFalse(string text) =>
        Assert.False(SymbolReference.IsDocumentationId(text));

    [Fact]
    public void Parse_ForABareName_TakesTheMemberOnly()
    {
        var query = SymbolReference.Parse("Submit")!.Value;

        Assert.Null(query.ContainingType);
        Assert.Equal("Submit", query.Member);
        Assert.Null(query.Parameters);
    }

    [Fact]
    public void Parse_ForAQualifiedName_SplitsTheContainingType()
    {
        var query = SymbolReference.Parse("OrderService.Submit")!.Value;

        Assert.Equal("OrderService", query.ContainingType);
        Assert.Equal("Submit", query.Member);
    }

    [Theory]
    [InlineData("OrderService.Submit()", 0)]
    [InlineData("OrderService.Submit(Order)", 1)]
    [InlineData("OrderService.Submit(Order, int)", 2)]
    [InlineData("OrderService.Submit(Dictionary<string,int>)", 1)]
    [InlineData("OrderService.Submit(Func<int,int>, CancellationToken)", 2)]
    [InlineData("OrderService.Submit(int[], Dictionary<string, List<int>>)", 2)]
    public void Parse_CountsParametersAtNestingDepthZero(string text, int expected) =>
        Assert.Equal(expected, SymbolReference.Parse(text)!.Value.Parameters!.Count);

    [Fact]
    public void Parse_KeepsEachParameterTypeText()
    {
        var query = SymbolReference.Parse("Reconcile(Dictionary<string, int>, Order)")!.Value;

        Assert.Equal(["Dictionary<string, int>", "Order"], query.Parameters);
    }

    [Fact]
    public void Parse_ForAnEmptyName_ReturnsNull() =>
        Assert.Null(SymbolReference.Parse("   "));
}
