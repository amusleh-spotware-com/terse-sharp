using System.Xml.Linq;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ConfigurationMappingTests
{
    [Fact]
    public void XmlRead_WithAProjectMappedToReleaseX64_ReadsItsBuiltConfigurationUnderTheDefaultSolutionConfiguration()
    {
        var mapping = XmlSolutionMapping.Read(XDocument.Parse("""
            <Solution>
              <Project Path="src/Mapped/Mapped.csproj">
                <BuildType Project="Release" />
                <Platform Project="x64" />
              </Project>
              <Project Path="src/Plain/Plain.csproj" />
            </Solution>
            """));
        ProjectMapping[] expected =
        [
            new("src/Mapped/Mapped.csproj", new("Release", "x64")),
            new("src/Plain/Plain.csproj", new("Debug", "AnyCPU")),
        ];

        Assert.Equal(new BuildConfiguration("Debug", "Any CPU"), mapping.Active);
        Assert.Equal(expected, mapping.Projects);
    }

    [Fact]
    public void XmlRead_WithAMappingScopedToAnotherSolutionConfiguration_IgnoresIt()
    {
        var mapping = XmlSolutionMapping.Read(XDocument.Parse("""
            <Solution>
              <Project Path="src/Mapped/Mapped.csproj">
                <BuildType Solution="Release|*" Project="Debug" />
                <BuildType Solution="Debug|*" Project="Release" />
                <Platform Solution="*|x64" Project="x64" />
              </Project>
            </Solution>
            """));

        Assert.Equal(new ProjectMapping("src/Mapped/Mapped.csproj", new("Release", "AnyCPU")), Assert.Single(mapping.Projects));
    }

    [Fact]
    public void ClassicRead_WithAnActiveCfgLine_MapsTheProjectByItsGuidAndSlashesItsPath()
    {
        var mapping = ClassicSolutionMapping.Read(
        [
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Mapped\", \"src\\Mapped\\Mapped.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
            "EndProject",
            "Global",
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
            "\t\tDebug|Any CPU = Debug|Any CPU",
            "\t\tRelease|Any CPU = Release|Any CPU",
            "\tEndGlobalSection",
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
            "\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Release|x64",
            "\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Release|x64",
            "\t\t{11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Debug|AnyCPU",
            "\tEndGlobalSection",
            "EndGlobal",
        ]);

        Assert.Equal(new BuildConfiguration("Debug", "Any CPU"), mapping.Active);
        Assert.Equal(new ProjectMapping("src/Mapped/Mapped.csproj", new("Release", "x64")), Assert.Single(mapping.Projects));
    }

    [Fact]
    public void Same_TreatsAnyCpuSpellingsAsOnePlatformAndX64AsAnother()
    {
        Assert.True(new BuildConfiguration("debug", "Any CPU").Same(new("Debug", "AnyCPU")));
        Assert.False(new BuildConfiguration("Debug", "AnyCPU").Same(new("Debug", "x64")));
        Assert.False(new BuildConfiguration("Debug", "AnyCPU").Same(new("Release", "AnyCPU")));
    }
}
