using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ChangedTestSelectionTests
{
    [Fact]
    public void Select_WithNothingChanged_FallsBackToTheWholeSolutionAndSaysWhy()
    {
        var solution = Build(out _, out _, out _);

        var selection = ChangedTestSelection.Select(solution, []);

        Assert.True(selection.IsFullRun);
        Assert.Contains("no document has changed", selection.FullRunReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_WhenAProductionFileChanged_RunsOnlyTheTestProjectThatReferencesIt()
    {
        var solution = Build(out var trading, out _, out _);

        var selection = ChangedTestSelection.Select(solution, [trading]);

        Assert.False(selection.IsFullRun, selection.FullRunReason);
        Assert.Equal(["Trading.Tests.csproj"], Names(selection.Run));
        Assert.Equal(["Unrelated.Tests.csproj"], Names(selection.Skipped));
    }

    [Fact]
    public void Select_WhenATestFileChanged_RunsThatTestProjectItself()
    {
        var solution = Build(out _, out var tradingTest, out _);

        var selection = ChangedTestSelection.Select(solution, [tradingTest]);

        Assert.False(selection.IsFullRun, selection.FullRunReason);
        Assert.Equal(["Trading.Tests.csproj"], Names(selection.Run));
    }

    [Fact]
    public void Select_WhenOnlyAProjectWithNoDependentTestChanged_FallsBackRatherThanRunningNothing()
    {
        var solution = Build(out _, out _, out var orphan);

        var selection = ChangedTestSelection.Select(solution, [orphan]);

        Assert.True(selection.IsFullRun);
        Assert.Contains("no test project depends", selection.FullRunReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_WithADocumentIdThatIsNotInTheSolution_FallsBackRatherThanGuessing()
    {
        var solution = Build(out _, out _, out _);

        var selection = ChangedTestSelection.Select(solution, [DocumentId.CreateNewId(ProjectId.CreateNewId())]);

        Assert.True(selection.IsFullRun);
        Assert.Contains("belongs to no project", selection.FullRunReason, StringComparison.Ordinal);
    }

    private static string XunitAssembly { get; } = typeof(FactAttribute).Assembly.Location;

    private static string[] Names(IEnumerable<string> paths) => [.. paths.Select(Path.GetFileName).OfType<string>()];

    private static Solution Build(out DocumentId trading, out DocumentId tradingTest, out DocumentId orphan)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var tradingId = Add(ref solution, "Trading", []);
        var orphanId = Add(ref solution, "Orphan", []);
        var tradingTestsId = Add(ref solution, "Trading.Tests", [tradingId], test: true);

        Add(ref solution, "Unrelated.Tests", [], test: true);

        trading = Document(ref solution, tradingId, "Trading", "Order.cs");
        tradingTest = Document(ref solution, tradingTestsId, "Trading.Tests", "OrderTests.cs");
        orphan = Document(ref solution, orphanId, "Orphan", "Orphan.cs");

        return solution;
    }

    private static ProjectId Add(ref Solution solution, string name, ProjectId[] references, bool test = false)
    {
        var id = ProjectId.CreateNewId(name);
        var info = ProjectInfo
            .Create(id, VersionStamp.Default, name, name, LanguageNames.CSharp)
            .WithFilePath(Path.Combine(Path.GetTempPath(), name, name + ".csproj"))
            .WithProjectReferences(references.Select(reference => new ProjectReference(reference)))
            .WithMetadataReferences(test ? [MetadataReference.CreateFromFile(XunitAssembly)] : []);

        solution = solution.AddProject(info);

        return id;
    }

    private static DocumentId Document(ref Solution solution, ProjectId project, string folder, string name)
    {
        var id = DocumentId.CreateNewId(project);

        solution = solution.AddDocument(DocumentInfo.Create(
            id,
            name,
            filePath: Path.Combine(Path.GetTempPath(), folder, name),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(string.Empty), VersionStamp.Default))));

        return id;
    }
}
