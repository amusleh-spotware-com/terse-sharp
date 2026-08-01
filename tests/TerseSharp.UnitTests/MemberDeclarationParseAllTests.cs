using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MemberDeclarationParseAllTests
{
    [Fact]
    public void ParseAll_WithOneMember_ReturnsIt()
    {
        var parsed = MemberDeclaration.ParseAll("public int Count => 1;");

        Assert.True(parsed.IsOk);
        Assert.Single(parsed.Value!);
    }

    [Fact]
    public void ParseAll_WithTwoMutuallyReferencingMembers_ReturnsBoth()
    {
        var parsed = MemberDeclaration.ParseAll("private int Total() => Half() + Half();\n\nprivate int Half() => 2;");

        Assert.True(parsed.IsOk);
        Assert.Equal(2, parsed.Value!.Length);
    }

    [Fact]
    public void ParseAll_WithTwoOverloads_ReturnsBoth()
    {
        var parsed = MemberDeclaration.ParseAll("public void Add(int value) { } public void Add(string value) { }");

        Assert.True(parsed.IsOk);
        Assert.Equal(2, parsed.Value!.Length);
    }

    [Fact]
    public void ParseAll_WithAMalformedMember_Fails()
    {
        var parsed = MemberDeclaration.ParseAll("public int Broken( => 1;");

        Assert.False(parsed.IsOk);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void ParseAll_WithNothing_Fails() => Assert.False(MemberDeclaration.ParseAll("   ").IsOk);
}
