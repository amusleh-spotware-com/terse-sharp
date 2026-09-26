using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class LockHoldersTests
{
    [Fact]
    public void UnloadCannotRelease_WhenEveryNamedHolderIsAnotherLiveProcess_IsTrue() =>
        Assert.True(LockHolders.UnloadCannotRelease("MSB3027: Could not copy \"a.dll\". The file is locked by: \"testhost (41)\", \"Visual Studio (42)\"", 7, Names));

    [Theory]
    [InlineData("The file is locked by: \"terse (7)\"")]
    [InlineData("The file is locked by: \"testhost (41)\", \".NET Host (43)\"")]
    [InlineData("The file is locked by: \".NET Host (44)\"")]
    [InlineData("The file is locked by: \"testhost (45)\"")]
    [InlineData("MSB3027: Could not copy \"a.dll\". Exceeded retry count of 10. Failed.")]
    public void UnloadCannotRelease_WhenAHolderMayBeThisServerOrIsGoneOrNoneIsNamed_IsFalse(string output) =>
        Assert.False(LockHolders.UnloadCannotRelease(output, 7, Names));

    private static string? Names(int pid) => pid switch
    {
        41 => "testhost",
        42 => "devenv",
        43 => "dotnet",
        44 => "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost",
        _ => null,
    };
}
