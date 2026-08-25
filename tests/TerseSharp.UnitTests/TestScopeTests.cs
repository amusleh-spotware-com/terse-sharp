using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TestScopeTests
{
    [Fact]
    public void Of_ForAProjectReferencingATestFramework_ReportsTest() =>
        Assert.Equal("test", TestScope.Of(Project(typeof(FactAttribute).Assembly.Location)));

    [Fact]
    public void Of_ForAProjectWithNoTestFramework_ReportsSrc() =>
        Assert.Equal("src", TestScope.Of(Project(typeof(object).Assembly.Location)));

    [Fact]
    public void Of_ForAProjectWithNoReferences_ReportsSrc() =>
        Assert.Equal("src", TestScope.Of(Project(null)));

    private static Project Project(string? reference)
    {
        using var workspace = new AdhocWorkspace();

        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            metadataReferences: reference is null ? [] : [MetadataReference.CreateFromFile(reference)]);

        return workspace.AddProject(info);
    }

    [Fact]
    public void TestProjectsOf_WhenOneProjectFileIsLoadedOncePerTargetFramework_FallsBackToTheProjectRatherThanOneFrameworksAssembly()
    {
        var runnable = typeof(TestScopeTests).Assembly.Location;

        Assert.Equal([runnable], TestScope.TestProjectsOf(Solution([("Multi.Tests.csproj", runnable)]), allowDirect: true));

        Assert.Equal(
            ["Multi.Tests.csproj"],
            TestScope.TestProjectsOf(Solution([("Multi.Tests.csproj", runnable), ("Multi.Tests.csproj", runnable)]), allowDirect: true));
    }

    [Fact]
    public void TestProjectsOf_WithoutDirectExecution_NamesTheProjectFileEvenWhenTheAssemblyIsRunnable()
    {
        var runnable = typeof(TestScopeTests).Assembly.Location;

        Assert.Equal(["One.Tests.csproj"], TestScope.TestProjectsOf(Solution([("One.Tests.csproj", runnable)]), allowDirect: false));
    }

    [Fact]
    public void TestProjectsOf_KeepsOnlyTheProjectsThatReferenceATestFramework()
    {
        var solution = Solution([("One.Tests.csproj", "One.Tests.dll")]).AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Library",
            "Library",
            LanguageNames.CSharp,
            filePath: "Library.csproj",
            outputFilePath: "Library.dll"));

        Assert.Equal(["One.Tests.csproj"], TestScope.TestProjectsOf(solution, allowDirect: false));
    }

    private static Solution Solution((string Project, string Assembly)[] projects)
    {
        var solution = new AdhocWorkspace().CurrentSolution;

        foreach (var (project, assembly) in projects)
        {
            solution = solution.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Default,
                Path.GetFileNameWithoutExtension(project),
                Path.GetFileNameWithoutExtension(project),
                LanguageNames.CSharp,
                filePath: project,
                outputFilePath: assembly,
                metadataReferences: [
                    MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "xunit.v3.runner.inproc.console.dll")),
                ]));
        }

        return solution;
    }
}
