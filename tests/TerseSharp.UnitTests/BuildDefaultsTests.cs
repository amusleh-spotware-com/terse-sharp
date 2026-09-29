using TerseSharp.Core;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class BuildDefaultsTests
{
    private const string Configured = """
        {
          "build": {
            "configuration": "Release",
            "projects": { "Bootstrapper.Tests": "Debug", "Views.Tests": "Debug" }
          }
        }
        """;

    [Fact]
    public void For_AProjectTheFileNames_AnswersItsOwnConfigurationAndAnyOtherTheDefault()
    {
        var defaults = BuildDefaults.Parse(Configured, ".terse.json", BuildDefaults.None);

        Assert.Equal("Debug", defaults.For(["UnitTests/Bootstrapper.Tests/Bootstrapper.Tests.csproj"]));
        Assert.Equal("Release", defaults.For(["src/App/App.csproj"]));
        Assert.Equal("Release", defaults.For([null]));
        Assert.Equal("Release", defaults.For([]));
    }

    [Fact]
    public void For_ABatchWhoseProjectsAgree_AnswersTheirConfigurationAndOneThatDisagreesTheDefault()
    {
        var defaults = BuildDefaults.Parse(Configured, ".terse.json", BuildDefaults.None);

        Assert.Equal("Debug", defaults.For(["Bootstrapper.Tests", "Views.Tests"]));
        Assert.Equal("Release", defaults.For(["Bootstrapper.Tests", "App"]));
    }

    [Fact]
    public void Parse_ANearerFile_OverridesOnlyTheSettingsItNames()
    {
        var home = BuildDefaults.Parse(Configured, "home/.terse.json", BuildDefaults.None);
        var repository = BuildDefaults.Parse("""{ "build": { "configuration": "Debug" } }""", "repo/.terse.json", home);

        Assert.Equal("Debug", repository.For(["App"]));
        Assert.Equal("Debug", repository.For(["Bootstrapper.Tests"]));
        Assert.Equal("repo/.terse.json", repository.Source);
    }

    [Theory]
    [InlineData("{ \"tools\": { \"xaml\": false } }")]
    [InlineData("{ \"build\": \"Debug\" }")]
    [InlineData("{ \"build\": { \"configuration\": 3 } }")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Parse_AFileWithoutAUsableBuildSection_KeepsTheSeed(string json)
    {
        var defaults = BuildDefaults.Parse(json, ".terse.json", BuildDefaults.None);

        Assert.Null(defaults.For(["App"]));
    }

    [Fact]
    public void Configured_MarksTheFirstLineOfTheVerdictAndLeavesTheRestUntouched()
    {
        Assert.Equal("build ok  errors=0  configuration=Debug (.terse.json)", BuildTools.Configured("build ok  errors=0", "Debug"));
        Assert.Equal("run_tests FAILED  configuration=Debug (.terse.json)\nsecond", BuildTools.Configured("run_tests FAILED\nsecond", "Debug"));
    }
}
