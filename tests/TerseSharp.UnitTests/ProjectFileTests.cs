using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ProjectFileTests : IDisposable
{
    private const string Central = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
          </ItemGroup>
        </Project>
        """;

    private readonly string root = Directory.CreateTempSubdirectory("terse-project-").FullName;

    [Fact]
    public void AddPackage_WhenCentralPackageManagementIsInsideTheWorkspace_WritesTheVersionThere()
    {
        var project = Project("src");

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), Central);

        var result = ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.0.0" />""", Props(), StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="Serilog" />""", File.ReadAllText(project), StringComparison.Ordinal);
    }

    [Fact]
    public void AddPackage_WhenCentralPackageManagementSitsAboveTheWorkspace_IsRefusedAndWritesNothing()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var project = Project(Path.Combine("nested", "src"));

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), Central);

        var before = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        var result = ProjectFile.AddPackage(workspace, project, "Serilog", "4.0.0", dryRun: false);

        Assert.False(result.IsOk);
        Assert.Contains("above the workspace root", result.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, Props());
    }

    [Fact]
    public void AddPackage_WithoutCentralPackageManagement_WritesTheVersionOnTheReference()
    {
        var project = Project("src");

        var result = ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""Include="Serilog" Version="4.0.0" """, File.ReadAllText(project), StringComparison.Ordinal);
    }

    [Fact]
    public void AddPackage_WithABlankPackage_IsRefused()
    {
        var result = ProjectFile.AddPackage(root, Project("src"), "  ", "4.0.0", dryRun: false);

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public void ListPackages_ForAProjectThatDoesNotExist_IsRefused()
    {
        var result = ProjectFile.ListPackages(Path.Combine(root, "nope", "Nope.csproj"));

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.DocumentNotFound, result.Error!.Code);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Props() => File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));

    private string Project(string relativeDirectory)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, relativeDirectory)).FullName;
        var path = Path.Combine(directory, "App.csproj");

        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n");

        return path;
    }
}
