using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class EmptyProjectLoadTests
{
    [Fact]
    public void Failures_NamesTheProjectThatLoadedWithNoDocuments_AndLeavesThePopulatedOneAlone()
    {
        using var workspace = new AdhocWorkspace();
        var contended = Added(workspace, "Contended");
        var populated = Added(workspace, "Populated");

        workspace.AddDocument(populated.Id, "Order.cs", SourceText.From("public sealed class Order;"));

        var failures = EmptyProjectLoad.Failures(workspace.CurrentSolution, _ => true);

        Assert.Single(failures);
        Assert.Equal("Contended.csproj", new string(LoadFailureSummary.ProjectOf(failures[0])));
        Assert.Contains(contended.FilePath!, failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Failures_WhenEveryProjectCarriesADocument_ReportsNothing()
    {
        using var workspace = new AdhocWorkspace();
        var populated = Added(workspace, "Populated");

        workspace.AddDocument(populated.Id, "Order.cs", SourceText.From("public sealed class Order;"));

        Assert.Empty(EmptyProjectLoad.Failures(workspace.CurrentSolution, _ => true));
    }

    [Fact]
    public void Failures_ForAProjectThatGlobsNoSourcesByDesign_ReportsNothing()
    {
        using var workspace = new AdhocWorkspace();

        Added(workspace, "ResourcesOnly");

        Assert.Empty(EmptyProjectLoad.Failures(workspace.CurrentSolution, _ => false));
        Assert.Single(EmptyProjectLoad.Failures(workspace.CurrentSolution, _ => true));
    }

    private static Project Added(AdhocWorkspace workspace, string name) =>
        workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name,
            name,
            LanguageNames.CSharp,
            filePath: Path.Combine(Path.GetTempPath(), name + ".csproj")));
}
