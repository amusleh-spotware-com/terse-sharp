using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class DoctorRoslynGapTests
{
    [Fact]
    public void RoslynGap_WhenTheSdkCarriesANewerRoslyn_WarnsWithBothVersions() =>
        Assert.StartsWith("WARNING roslyn=5.6.0.0 < sdk=5.12.0.0 - ", Doctor.RoslynGap(new Version(5, 6, 0, 0), new Version(5, 12, 0, 0)), StringComparison.Ordinal);

    [Theory]
    [InlineData("5.12.0.0", "5.12.0.0")]
    [InlineData("5.12.0.0", "5.6.0.0")]
    public void RoslynGap_WhenTheSdkIsNotAhead_SaysNothing(string carried, string sdk) =>
        Assert.Null(Doctor.RoslynGap(Version.Parse(carried), Version.Parse(sdk)));

    [Fact]
    public void RoslynGap_WhenTheSdkRoslynCannotBeRead_SaysNothing() =>
        Assert.Null(Doctor.RoslynGap(new Version(5, 12, 0, 0), null));

    [Fact]
    public void CleanupVerifyRefusal_UnderAGap_RefusesNamingBothVersionsWithARemedy()
    {
        var refusal = Doctor.CleanupVerifyRefusal(verify: true, Doctor.RoslynGap(new Version(5, 6, 0, 0), new Version(5, 12, 0, 0)));

        Assert.StartsWith("ERROR ToolchainMismatch: ", refusal, StringComparison.Ordinal);
        Assert.Contains("roslyn=5.6.0.0 < sdk=5.12.0.0", refusal, StringComparison.Ordinal);
        Assert.Contains("\nremedy: pin the SDK feature band", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "WARNING roslyn=5.6.0.0 < sdk=5.12.0.0 - x")]
    [InlineData(true, null)]
    public void CleanupVerifyRefusal_WithoutVerifyOrWithoutAGap_RefusesNothing(bool verify, string? gap) =>
        Assert.Null(Doctor.CleanupVerifyRefusal(verify, gap));
}
