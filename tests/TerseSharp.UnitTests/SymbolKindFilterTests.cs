using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SymbolKindFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("type")]
    [InlineData("Record")]
    [InlineData("namedtype")]
    [InlineData("method")]
    public void IsKnown_AcceptsEveryAdvertisedKindAndNoKind(string? kind) => Assert.True(SymbolKindFilter.IsKnown(kind));

    [Theory]
    [InlineData("bogus")]
    [InlineData("types")]
    public void IsKnown_RefusesAKindNoSymbolCanCarry(string kind) => Assert.False(SymbolKindFilter.IsKnown(kind));

    [Fact]
    public void Refusal_NamesTheKindAndEveryAcceptedKind() =>
        Assert.Contains("type, class, record, struct, interface, enum, delegate, method, ctor, property, field, event, namespace, namedtype", SymbolKindFilter.Refusal("bogus")!.Remedy, StringComparison.Ordinal);
}
