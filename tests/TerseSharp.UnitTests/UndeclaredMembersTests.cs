using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class UndeclaredMembersTests
{
    [Fact]
    public void Hint_MissingMemberErrorsOnly_NamesEachMemberOnItsType()
    {
        string[] errors =
        [
            "CS1061 src/A.cs: 'Fixture.Broken.Calculator' does not contain a definition for 'NotYetWritten' and no accessible extension method 'NotYetWritten' accepting a first argument of type 'Calculator' could be found",
            "CS0117 src/B.cs: 'Strings' does not contain a definition for 'Caption_New'",
        ];

        var hint = UndeclaredMembers.Hint(errors);

        Assert.NotNull(hint);
        Assert.Equal(["Calculator.NotYetWritten", "Strings.Caption_New"], hint);
    }

    [Fact]
    public void Hint_AnyOtherErrorBeside_NamesNothing() => Assert.Null(UndeclaredMembers.Hint(["CS0117 src/B.cs: 'Strings' does not contain a definition for 'X'", "CS0103 src/A.cs: The name 'y' does not exist in the current context"]));

    [Fact]
    public void Hint_AMemberOfAGenericType_NamesTheTypeWithoutItsArguments()
    {
        var hint = UndeclaredMembers.Hint(["CS1061 src/A.cs: 'List<Fixture.Trading.Order>' does not contain a definition for 'Totl' and no accessible extension method 'Totl' accepting a first argument of type 'List<Order>' could be found"]);

        Assert.NotNull(hint);
        Assert.Equal(["List.Totl"], hint);
    }
}
