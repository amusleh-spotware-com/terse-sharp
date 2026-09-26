using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SkillBudgetTests
{
    [Theory]
    [InlineData(@"C:\repo\src\TerseSharp.Server\Assets\SKILL.md", true)]
    [InlineData("/repo/src/TerseSharp.Server/Assets/SKILL.md", true)]
    [InlineData("src/TerseSharp.Server/Assets/SKILL.md", true)]
    [InlineData("/repo/docs/Assets/SKILL.md", false)]
    [InlineData("/repo/src/TerseSharp.Server/Assets/skill.md", false)]
    [InlineData("/repo/src/NotTerseSharp.Server/Assets/SKILL.md", false)]
    [InlineData("SKILL.md", false)]
    public void IsShippedSkill_MatchesOnlyTheServersEmbeddedSkill(string path, bool expected) =>
        Assert.Equal(expected, SkillBudget.IsShippedSkill(path));

    [Fact]
    public void Stamp_CountsTheSkillAsCiChecksItOut_IgnoringCarriageReturns()
    {
        const string Path = "/repo/src/TerseSharp.Server/Assets/SKILL.md";

        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"\nbudget={SkillBudget.Tokens} used=2 left={SkillBudget.Tokens - 2}"),
            SkillBudget.Stamp(Path, requested: true, "ab\r\ncd\r\n"));
        Assert.Equal(string.Empty, SkillBudget.Stamp(Path, requested: false, "ab\ncd\n"));
        Assert.Equal(string.Empty, SkillBudget.Stamp("/repo/README.md", requested: true, "ab\ncd\n"));
    }

    [Fact]
    public void Stamp_OverTheBudget_SaysByHowMuch()
    {
        var text = new string('x', (SkillBudget.Tokens + 1) * 4);

        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"\nbudget={SkillBudget.Tokens} used={SkillBudget.Tokens + 1} over=1"),
            SkillBudget.Stamp("/r/src/TerseSharp.Server/Assets/SKILL.md", requested: true, text));
    }
}
