using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MemberDeclarationTests
{
    [Fact]
    public void Parse_OneMethod_IsAccepted()
    {
        var result = MemberDeclaration.Parse("public int Ping() => 42;");

        Assert.True(result.IsOk);
        Assert.Contains("Ping", result.Value!.ToFullString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TwoMethods_IsRefusedInsteadOfKeepingOnlyTheFirst()
    {
        var result = MemberDeclaration.Parse("public int Ping() => 42;\npublic int Pong() => 43;");

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Contains("not exactly one member", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AMethodFollowedByAField_IsRefused()
    {
        var result = MemberDeclaration.Parse("public int Ping() => 42;\nprivate readonly int count;");

        Assert.False(result.IsOk);
    }

    [Fact]
    public void Parse_Garbage_IsRefused()
    {
        var result = MemberDeclaration.Parse("this is not C#");

        Assert.False(result.IsOk);
    }

    [Fact]
    public void Parse_AMemberWithAttributesAndDocComment_IsAccepted()
    {
        var result = MemberDeclaration.Parse("[Fact]\npublic void Works()\n{\n}");

        Assert.True(result.IsOk);
    }

    [Fact]
    public void Parse_AType_IsAccepted()
    {
        var result = MemberDeclaration.Parse("public sealed class Order { }");

        Assert.True(result.IsOk);
    }
}
