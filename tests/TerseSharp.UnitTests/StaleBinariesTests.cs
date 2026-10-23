using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class StaleBinariesTests
{
    private static readonly DateTime BuiltAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Detect_WithRoots_ReportsOnlyThoseProjectsAndTheirDependencyClosure()
    {
        var root = Directory.CreateTempSubdirectory("terse-stale-");

        try
        {
            var library = Project(root, "Library", stale: true);
            var tests = Project(root, "Library.Tests", stale: false, new ProjectReference(library.Id));
            var unrelated = Project(root, "Unrelated", stale: true);
            using var workspace = new AdhocWorkspace();
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, projects: [library, tests, unrelated]));

            var scoped = StaleBinaries.Detect(solution, [tests.FilePath!]);
            var whole = StaleBinaries.Detect(solution, []);

            Assert.Equal(tests.FilePath, Assert.Single(scoped).ProjectPath);
            Assert.DoesNotContain(scoped, binary => binary.ProjectPath == unrelated.FilePath);
            Assert.Contains(whole, binary => binary.ProjectPath == unrelated.FilePath);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ProjectInfo Project(DirectoryInfo root, string name, bool stale, params ProjectReference[] references)
    {
        var directory = Directory.CreateDirectory(Path.Join(root.FullName, name)).FullName;
        var projectPath = Path.Join(directory, name + ".csproj");
        var source = Path.Join(directory, "Source.cs");
        var output = Path.Join(directory, "bin", name + ".dll");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(source, "class C { }");
        File.WriteAllText(output, string.Empty);
        File.SetLastWriteTimeUtc(output, BuiltAt);
        File.SetLastWriteTimeUtc(projectPath, BuiltAt.AddHours(-2));
        File.SetLastWriteTimeUtc(source, stale ? BuiltAt.AddHours(1) : BuiltAt.AddHours(-1));

        var id = ProjectId.CreateNewId(name);

        return ProjectInfo.Create(
            id,
            VersionStamp.Default,
            name,
            name,
            LanguageNames.CSharp,
            filePath: projectPath,
            outputFilePath: output,
            documents: [DocumentInfo.Create(DocumentId.CreateNewId(id), "Source.cs", filePath: source)],
            projectReferences: references);
    }

    [Fact]
    public void Detect_WithARootNamedByItsOutputAssembly_StillProbesThatProject()
    {
        var root = Directory.CreateTempSubdirectory("terse-stale-");

        try
        {
            var tests = Project(root, "Direct.Tests", stale: true);
            using var workspace = new AdhocWorkspace();
            var solution = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, projects: [tests]));

            var scoped = StaleBinaries.Detect(solution, [tests.OutputFilePath!]);

            Assert.Equal(tests.FilePath, Assert.Single(scoped).ProjectPath);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
