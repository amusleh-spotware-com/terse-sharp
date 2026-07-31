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

    [Theory]
    [InlineData("public required int Volume { get; init; }")]
    [InlineData("public static T Widen<T>(T value) where T : struct, IEquatable<T> => value;")]
    [InlineData("public string Text => \"\"\"a raw \"string\" literal\"\"\";")]
    [InlineData("public event EventHandler<OrderEventArgs>? Filled;")]
    [InlineData("public sealed record Money(decimal Amount, string Currency);")]
    [InlineData("public sealed class Repository(IClock clock) { }")]
    [InlineData("public int this[int index] => index;")]
    [InlineData("public static explicit operator int(Order order) => 0;")]
    [InlineData("[Obsolete(\"use Submit\")]\npublic void Send() { }")]
    [InlineData("public async Task<int> CountAsync(CancellationToken cancellationToken) => await Task.FromResult(0);")]
    [InlineData("private const string Separator = \";\";")]
    [InlineData("public partial void OnChanged();")]
    public void Parse_AnUncommonButLegalSingleMember_IsStillAccepted(string declaration) =>
        Assert.True(MemberDeclaration.Parse(declaration).IsOk, declaration);
}
