using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class SeverityRemapsTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "terse-remap", "App.csproj");

    [Fact]
    public void Between_ALoadedTreatWarningsAsErrorsTheMappedConfigurationTurnsOff_RemapsTheGeneralOptionToDefault()
    {
        var remap = SeverityRemaps.Between(Project, ["true", "", "", ""], ["false", "", "", ""]);

        Assert.NotNull(remap);
        Assert.Equal(ReportDiagnostic.Default, remap.General);
    }

    [Fact]
    public void Between_AMappedWarningsAsErrorsAndNoWarn_MapsEachIdToItsSeverity()
    {
        var remap = SeverityRemaps.Between(Project, ["false", "", "", ""], ["false", "CS4014", "", "CS1591;1701"]);

        Assert.NotNull(remap);
        Assert.Equal(ReportDiagnostic.Error, remap.Specific["CS4014"]);
        Assert.Equal(ReportDiagnostic.Suppress, remap.Specific["CS1591"]);
        Assert.Equal(ReportDiagnostic.Suppress, remap.Specific["CS1701"]);
    }

    [Fact]
    public void Between_ALoadedNoWarnTheMappedConfigurationDropsIt_NamesItAsAnIdToClear()
    {
        var remap = SeverityRemaps.Between(Project, ["false", "", "", "CS0168"], ["false", "", "", ""]);

        Assert.NotNull(remap);
        Assert.Contains("CS0168", remap.Loaded);
        Assert.False(remap.Specific.ContainsKey("CS0168"));
    }

    [Fact]
    public void Between_WhenTheMappedEvaluationFailed_IsNull() =>
        Assert.Null(SeverityRemaps.Between(Project, ["false", "", "", ""], null));
}
