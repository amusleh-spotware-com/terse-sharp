using TerseSharp.Core;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class RejectedEditsTests
{
    [Fact]
    public void Origin_OfATokenPushedOutOfTheHeldWindow_StillNamesTheToolThatIssuedIt()
    {
        var first = RejectedEdits.Remember("root", "edit_text", ["a.cs"], ["x"]);

        for (var index = 0; index < RejectedEdits.Capacity; index++)
            RejectedEdits.Remember("root", "write_text", ["b.md"], ["y"]);

        var origin = RejectedEdits.Origin(first);
        var answer = EditTools.Unknown(first, "write_text");

        Assert.Null(RejectedEdits.Recall(first));
        Assert.True(origin.Issued);
        Assert.Equal("edit_text", origin.Tool);
        Assert.Contains("retryWith=" + first + " was issued by edit_text and has expired", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("names no held rejection", answer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("r2147483647")]
    [InlineData("r0")]
    [InlineData("token")]
    public void Origin_OfATokenThisProcessNeverIssued_SaysSo(string token)
    {
        Assert.False(RejectedEdits.Origin(token).Issued);
        Assert.Contains("was never issued by this server process", EditTools.Unknown(token, "write_text"), StringComparison.Ordinal);
    }

    [Fact]
    public void Carried_ForABatchRefusedOnlyForWantingForce_EndsWithATokenHoldingEveryFile()
    {
        var refusal = Errors.Invalid(FileService.UnforcedRefusal + "a.cs, b.cs", "pass force=true");
        var answer = EditTools.Carried(Result.Fail<string>(refusal), new EditTools.Carry("write_text", ["a.cs", "b.cs"], ["class A;", "class B;"]), "root");
        var last = answer.Split('\n')[^1];

        Assert.StartsWith("ERROR InvalidArgument", answer, StringComparison.Ordinal);
        Assert.Contains("the retry is the token plus force=true", answer, StringComparison.Ordinal);
        Assert.StartsWith("retryWith=r", last, StringComparison.Ordinal);

        var held = RejectedEdits.Recall(last["retryWith=".Length..]);

        Assert.NotNull(held);
        Assert.Equal(new[] { "a.cs", "b.cs" }, held!.Targets);
        Assert.Equal(new[] { "class A;", "class B;" }, held.Payloads);
    }

    [Fact]
    public void Carried_ForABatchRefusedForAnyOtherInvalidArgument_IssuesNoToken()
    {
        var answer = EditTools.Carried(Result.Fail<string>(Errors.Invalid("content is empty", "pass content")), new EditTools.Carry("write_text", ["a.cs", "b.cs"], ["x", "y"]), "root");

        Assert.DoesNotContain("retryWith=", answer, StringComparison.Ordinal);
    }
}
