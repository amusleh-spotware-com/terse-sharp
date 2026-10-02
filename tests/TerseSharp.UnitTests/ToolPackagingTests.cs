using System.Xml.Linq;

namespace TerseSharp.UnitTests;

public sealed class ToolPackagingTests
{
    private const string Switch = "TerseRidPackages";

    private static readonly string[] ReleasedRids = ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "any"];

    private static readonly XDocument Project =
        XDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot, "src", "TerseSharp.Server", "TerseSharp.Server.csproj")));

    private static XElement RidGroup() =>
        Project.Root!.Elements("PropertyGroup").Single(group => ((string?)group.Attribute("Condition"))?.Contains(Switch, StringComparison.Ordinal) == true);

    private static string RidList() => (string?)RidGroup().Element("ToolPackageRuntimeIdentifiers") ?? string.Empty;

    [Fact]
    public void TheRidSwitch_ShipsReadyToRunPackagesForEveryReleasedRidAndAFrameworkDependentFallback()
    {
        var group = RidGroup();

        Assert.Equal("'$(TerseRidPackages)' == 'true'", (string?)group.Attribute("Condition"));
        Assert.Equal(ReleasedRids, RidList().Split(';'));
        Assert.Equal(ReleasedRids, ((string?)group.Element("RuntimeIdentifiers") ?? string.Empty).Split(';'));
        Assert.Contains(group.Elements("PublishReadyToRun"), element => element.Value == "true" && element.Attribute("Condition") is null);
        Assert.Contains(group.Elements("PublishReadyToRun"), element => element.Value == "false" && ((string?)element.Attribute("Condition"))?.Contains("'any'", StringComparison.Ordinal) == true);
        Assert.Equal("false", (string?)group.Element("SelfContained"));
    }

    [Fact]
    public void TheRidPackages_RunThroughTheDotnetHost_SoTheWindowsShimStaysAnExecutable() =>
        Assert.Equal("false", (string?)RidGroup().Element("UseAppHost"));

    [Theory]
    [InlineData("RuntimeIdentifier")]
    [InlineData("RuntimeIdentifiers")]
    [InlineData("ToolPackageRuntimeIdentifiers")]
    [InlineData("PublishReadyToRun")]
    [InlineData("SelfContained")]
    [InlineData("UseAppHost")]
    public void ThePlainBuild_DeclaresNoRidProperty_SoTheE2EBinaryStaysAtBinConfigurationNet10(string property) =>
        Assert.DoesNotContain(Project.Root!.Elements("PropertyGroup").Where(group => group.Attribute("Condition") is null), group => group.Element(property) is not null);

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("release.yml")]
    public void EveryWorkflowThatPacks_PassesTheRidSwitchWithoutNoBuildAndVerifiesEveryReleasedRid(string workflow)
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot, ".github", "workflows", workflow));
        var pack = text.Split('\n').Single(line => line.Contains("dotnet pack", StringComparison.Ordinal));

        Assert.Contains("-p:TerseRidPackages=true", pack, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-build", pack, StringComparison.Ordinal);
        Assert.Contains("for rid in " + RidList().Replace(';', ' ') + "; do", text, StringComparison.Ordinal);
        Assert.Contains("-ipath '*linux-x64*' -name terse.dll", text, StringComparison.Ordinal);
    }
}
