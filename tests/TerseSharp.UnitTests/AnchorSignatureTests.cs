using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class AnchorSignatureTests
{
    [Theory]
    [InlineData("Find(string,string?)", "Find(string, string)")]
    [InlineData("Find(string,string?)", "Find(string directory, string? home)")]
    [InlineData("Reconcile(Dictionary<string, int>,Order)", "Reconcile(Dictionary<string,int> map, Order order)")]
    [InlineData("Weigh(refint)", "Weigh(ref int value)")]
    [InlineData("Weigh(refint)", "Weigh(ref int)")]
    public void Canonical_Structurally_ResolvesTheSpellingAnOutlinePrints(string signature, string reference) =>
        Assert.Equal(
            AnchorSignature.Canonical(signature, AnchorTier.Structural),
            AnchorSignature.Canonical(reference, AnchorTier.Structural));

    [Theory]
    [InlineData("Find(string)", "Find(string, string)")]
    [InlineData("Submit(Order)", "Submit(Trade)")]
    [InlineData("Reconcile(Dictionary<string,int>)", "Reconcile(Dictionary<int,string>)")]
    public void Canonical_Structurally_StillSeparatesDifferentParameterLists(string left, string right) =>
        Assert.NotEqual(
            AnchorSignature.Canonical(left, AnchorTier.Structural),
            AnchorSignature.Canonical(right, AnchorTier.Structural));

    [Fact]
    public void Canonical_AtTheSpacingTier_KeepsTheNullableAnnotationThatPicksAnOverload() =>
        Assert.NotEqual(
            AnchorSignature.Canonical("Find(string?)", AnchorTier.Spacing),
            AnchorSignature.Canonical("Find(string)", AnchorTier.Spacing));

    [Fact]
    public void Canonical_AtTheSpacingTier_IgnoresOnlyWhitespace() =>
        Assert.Equal(
            AnchorSignature.Canonical("Reconcile(Dictionary<string, int>,Order)", AnchorTier.Spacing),
            AnchorSignature.Canonical("Reconcile( Dictionary<string,int> , Order )", AnchorTier.Spacing));

    [Fact]
    public void Canonical_ForANameWithNoParameterList_StripsOnlyWhitespace() =>
        Assert.Equal("Submit", AnchorSignature.Canonical(" Submit ", AnchorTier.Structural));

    [Fact]
    public void Canonical_ForAnEmptyParameterList_StaysEmpty() =>
        Assert.Equal("Submit()", AnchorSignature.Canonical("Submit()", AnchorTier.Structural));
}
