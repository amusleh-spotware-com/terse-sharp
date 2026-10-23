using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DroppedDeclarationsWarningTests
{
    private const string Source = "src/Sample.cs";

    [Fact]
    public void Warning_ForAMethodThatGainedATrailingOptionalParameter_IsNull() =>
        Assert.Null(DroppedDeclarations.Warning(
            Source,
            "namespace N;\npublic sealed class C\n{\n    public int Unused() => 7;\n}\n",
            "namespace N;\npublic sealed class C\n{\n    public int Unused(int seed = 0) => 7 + seed;\n}\n"));

    [Fact]
    public void Warning_ForTwoOverloadsCollapsedIntoOneNewSignature_NamesBothRemovedOverloads() =>
        Assert.Equal(
            "WARNING this overwrite drops 2 declaration(s) the file on disk held just before this write: C.Foo(int), C.Foo(string) - dryRun=true shows the diff, write_text ref=HEAD restores the file",
            DroppedDeclarations.Warning(
                Source,
                "public sealed class C\n{\n    public void Foo(int value) { }\n\n    public void Foo(string value) { }\n}\n",
                "public sealed class C\n{\n    public void Foo(int value, bool flag) { }\n}\n"));

    [Fact]
    public void Warning_ForAMemberRemovedBesideAnotherReSigned_NamesOnlyTheRemovedOne() =>
        Assert.Equal(
            "WARNING this overwrite drops 1 declaration(s) the file on disk held just before this write: C.NeverCalled() - dryRun=true shows the diff, write_text ref=HEAD restores the file",
            DroppedDeclarations.Warning(
                Source,
                "public sealed class C\n{\n    public int Unused() => 7;\n\n    private int NeverCalled() => 42;\n}\n",
                "public sealed class C\n{\n    public int Unused(int seed) => seed;\n}\n"));
}
