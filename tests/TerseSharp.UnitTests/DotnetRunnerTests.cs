using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DotnetRunnerTests
{
    [Theory]
    [InlineData("error MSB3021: Unable to copy file")]
    [InlineData("warning MSB3027: Could not copy")]
    [InlineData("The process cannot access the file because it is being used by another process.")]
    [InlineData("BEING USED BY ANOTHER PROCESS")]
    public void IsLockedOutput_ForALockSignatureOnAFailedBuild_IsTrue(string output) =>
        Assert.True(DotnetRunner.IsLockedOutput(1, output));

    [Fact]
    public void IsLockedOutput_ForALockSignatureOnASuccessfulBuild_IsFalse() =>
        Assert.False(DotnetRunner.IsLockedOutput(0, "warning MSB3026: being used by another process"));

    [Theory]
    [InlineData("error CS1002: ; expected")]
    [InlineData("")]
    [InlineData("Build succeeded.")]
    public void IsLockedOutput_ForAnOrdinaryFailure_IsFalse(string output) =>
        Assert.False(DotnetRunner.IsLockedOutput(1, output));
}
