using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class UnrestoredProjectsTests
{
    [Fact]
    public void Find_NamesOnlyTheProjectThePredicateCallsUnrestored()
    {
        using var workspace = new AdhocWorkspace();
        var fresh = Added(workspace, "Fresh");

        Added(workspace, "Restored");

        var missing = UnrestoredProjects.Find(workspace.CurrentSolution, path => string.Equals(path, fresh.FilePath, StringComparison.Ordinal));

        Assert.Equal([fresh.FilePath!], missing);
    }

    [Fact]
    public void Find_ListsAMultiTargetedProjectOnce()
    {
        using var workspace = new AdhocWorkspace();

        Added(workspace, "Multi");
        Added(workspace, "Multi");

        Assert.Single(UnrestoredProjects.Find(workspace.CurrentSolution, _ => true));
    }

    [Fact]
    public void Find_WhenEveryProjectIsRestored_ReportsNothing()
    {
        using var workspace = new AdhocWorkspace();

        Added(workspace, "Restored");

        Assert.Empty(UnrestoredProjects.Find(workspace.CurrentSolution, _ => false));
    }

    [Fact]
    public async Task AssetsPresent_IsFalseUntilTheAssetsFileExists()
    {
        var directory = Directory.CreateTempSubdirectory("terse-restore-");

        try
        {
            var project = Path.Combine(directory.FullName, "Fresh.csproj");

            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);

            Assert.False(UnrestoredProjects.AssetsPresent(project));

            Directory.CreateDirectory(Path.Combine(directory.FullName, "obj"));
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "obj", "project.assets.json"), "{}", TestContext.Current.CancellationToken);

            Assert.True(UnrestoredProjects.AssetsPresent(project));
            Assert.False(UnrestoredProjects.IsUnrestored(project));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
