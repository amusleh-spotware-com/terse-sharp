using Microsoft.CodeAnalysis;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class RealizedNoteTests
{
    [Fact]
    public void Realized_OnTheFirstRealizationOfALoad_SaysItIsPaidOncePerLoad() =>
        Assert.Equal(
            "compilations=realized in 7414ms (once per load, not per call)",
            ToolContext.Realized(7414, drops: 0, TimeSpan.Zero));

    [Fact]
    public void Realized_AfterAnIdleDrop_SaysItIsARealizationAgainAndWhy() =>
        Assert.Equal(
            "compilations=realized in 84782ms (again - drop #3 released them after 2m idle; --idle-minutes or TERSE_IDLE_MINUTES=0 keeps them)",
            ToolContext.Realized(84782, drops: 3, TimeSpan.FromMinutes(2)));

    [Fact]
    public void Grew_OnAPartialRealization_SaysHowManyMoreOfHowManyProjectsAreCompiledNow() =>
        Assert.Equal(
            "compilations=realized in 2140ms (3 more of 12 projects, 7 compiled now)",
            ToolContext.Grew(2140, realized: 3, compiled: 7, total: 12, drops: 0));

    [Fact]
    public void Grew_AfterAnIdleDrop_NamesTheDropItIsPayingFor() =>
        Assert.Equal(
            "compilations=realized in 2140ms (3 more of 12 projects, 7 compiled now; after drop #2)",
            ToolContext.Grew(2140, realized: 3, compiled: 7, total: 12, drops: 2));

    [Fact]
    public async Task RealizedProjects_CountsOnlyTheProjectsWhoseCompilationWasBuilt()
    {
        using var workspace = new AdhocWorkspace();
        var first = workspace.AddProject("First", LanguageNames.CSharp);
        _ = workspace.AddProject("Second", LanguageNames.CSharp);
        var solution = workspace.CurrentSolution;
        var before = LoadedWorkspace.RealizedProjects(solution);

        _ = await solution.GetProject(first.Id)!.GetCompilationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, before);
        Assert.Equal(1, LoadedWorkspace.RealizedProjects(solution));
    }
}
