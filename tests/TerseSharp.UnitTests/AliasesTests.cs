using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class AliasesTests
{
    [Theory]
    [InlineData("*.cs", "src", "src/**/*.cs")]
    [InlineData("**/*.cs", "src/", "src/**/*.cs")]
    [InlineData("**/*.cs", "fixtures/FixtureSolution", "fixtures/FixtureSolution/**/*.cs")]
    public void ScopeRemedy_WithADirectoryAndAPattern_NamesTheGlobThatSearchesThePatternInsideIt(string glob, string path, string composed)
    {
        var chosen = Aliases.Chosen(Aliases.ScopeRemedy, new ParameterSpelling("glob", glob), new ParameterSpelling("path", path));

        Assert.False(chosen.IsOk);
        Assert.Contains("glob=\"" + composed + "\"", chosen.Error!.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeRemedy_WithARootAnchoredPattern_OffersNoComposedGlobBecauseItsMeaningWouldChange()
    {
        var chosen = Aliases.Chosen(Aliases.ScopeRemedy, new ParameterSpelling("glob", "src/Fixture.Trading/*.cs"), new ParameterSpelling("path", "tests"));

        Assert.False(chosen.IsOk);
        Assert.DoesNotContain("glob=\"", chosen.Error!.Remedy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/**/*.cs", "src/**/*.cs", "src/**/*.cs")]
    [InlineData(null, "src", "src")]
    [InlineData("", "src", "src")]
    [InlineData("*.cs", null, "*.cs")]
    public void Chosen_WithOneValueOrTheSameValueTwice_AnswersThatValue(string? glob, string? path, string expected)
    {
        var chosen = Aliases.Chosen(Aliases.ScopeRemedy, new ParameterSpelling("glob", glob), new ParameterSpelling("path", path));

        Assert.True(chosen.IsOk);
        Assert.Equal(expected, chosen.Value);
    }
}
