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

    private const string CentralDisabled = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
          </PropertyGroup>
        </Project>
        """;

    private const string Enabled = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
        </Project>
        """;

    private const string EnabledProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
        </Project>
        """;

    private const string EmptyVersions = """
        <Project>
          <ItemGroup>
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public async Task AddPackage_WhenCentralPackageManagementIsInsideTheWorkspace_WritesTheVersionThere()
    {
        var project = Project("src");

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), Central);

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.0.0" />""", Props(), StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="Serilog" />""", File.ReadAllText(project), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WhenCentralPackageManagementSitsAboveTheWorkspace_IsRefusedAndWritesNothing()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var project = Project(Path.Combine("nested", "src"));

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), Central);

        var before = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        var result = await ProjectFile.AddPackage(workspace, project, "Serilog", "4.0.0", dryRun: false);

        Assert.False(result.IsOk);
        Assert.Contains("above the workspace root", result.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, Props());
    }
    [Fact]
    public async Task AddPackage_WithoutCentralPackageManagement_WritesTheVersionOnTheReference()
    {
        var project = Project("src");

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""Include="Serilog" Version="4.0.0" """, File.ReadAllText(project), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WhenDirectoryPackagesPropsExistsButCentralManagementIsOff_WritesTheVersionOnTheReference()
    {
        var project = Project("src");

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), CentralDisabled);

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""Include="Serilog" Version="4.0.0" """, File.ReadAllText(project), StringComparison.Ordinal);
        Assert.DoesNotContain("Serilog", Props(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WhenCentralManagementIsDeclaredInDirectoryBuildProps_WritesTheVersionCentrally()
    {
        var project = Project("src");

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), EmptyVersions);
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"), Enabled);

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.0.0" />""", Props(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WhenCentralManagementIsDeclaredInTheProject_WritesTheVersionCentrally()
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
        var project = Path.Combine(directory, "App.csproj");

        File.WriteAllText(project, EnabledProject);
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), EmptyVersions);

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.0.0" />""", Props(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WhenCentralManagementIsAnUnresolvedExpression_TreatsItAsEnabled()
    {
        var project = Project("src");
        var expression = """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>$(UseCpm)</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), expression);

        var result = await ProjectFile.AddPackage(root, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""<PackageVersion Include="Serilog" Version="4.0.0" />""", Props(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddPackage_WhenAnUnmanagedPropsFileSitsAboveTheWorkspace_IsNotTreatedAsCentralManagement()
    {
        var workspace = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var project = Project(Path.Combine("nested", "src"));

        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"), CentralDisabled);

        var result = await ProjectFile.AddPackage(workspace, project, "Serilog", "4.0.0", dryRun: false);

        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("""Include="Serilog" Version="4.0.0" """, File.ReadAllText(project), StringComparison.Ordinal);
    }
    [Fact]
    public async Task AddPackage_WithABlankPackage_IsRefused()
    {
        var result = await ProjectFile.AddPackage(root, Project("src"), "  ", "4.0.0", dryRun: false);

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
