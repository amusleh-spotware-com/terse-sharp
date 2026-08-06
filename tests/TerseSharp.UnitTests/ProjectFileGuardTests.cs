using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ProjectFileGuardTests
{
    private const string Original = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void OnlyRedundantCompileItems_ForTheItemMsBuildAdds_IsAttributable()
    {
        var rewritten = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="IOrderService.cs" />
              </ItemGroup>
            </Project>
            """;

        Assert.True(ProjectFileGuard.OnlyRedundantCompileItems(Original, rewritten, ["src/IOrderService.cs"]));
    }

    [Fact]
    public void OnlyRedundantCompileItems_WhenAConcurrentEditAlsoLanded_IsRefused()
    {
        var rewritten = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="IOrderService.cs" />
              </ItemGroup>
            </Project>
            """;

        Assert.False(ProjectFileGuard.OnlyRedundantCompileItems(Original, rewritten, ["src/IOrderService.cs"]));
    }

    [Fact]
    public void OnlyRedundantCompileItems_WhenALineWasRemoved_IsRefused()
    {
        var rewritten = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="IOrderService.cs" />
              </ItemGroup>
            </Project>
            """;

        Assert.False(ProjectFileGuard.OnlyRedundantCompileItems(Original, rewritten, ["src/IOrderService.cs"]));
    }

    [Fact]
    public void OnlyRedundantCompileItems_ForACompileItemNamingAnotherFile_IsRefused()
    {
        var rewritten = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="SomethingElse.cs" />
              </ItemGroup>
            </Project>
            """;

        Assert.False(ProjectFileGuard.OnlyRedundantCompileItems(Original, rewritten, ["src/IOrderService.cs"]));
    }

    [Fact]
    public void OnlyRedundantCompileItems_ForAnUnchangedFile_IsAttributable() =>
        Assert.True(ProjectFileGuard.OnlyRedundantCompileItems(Original, Original, ["src/IOrderService.cs"]));

    [Fact]
    public void Capture_WithNoAddedFiles_TakesNoSnapshot() =>
        Assert.Null(ProjectFileGuard.Capture("any.csproj", []));

    [Fact]
    public void Capture_ForAProjectThatDoesNotExist_TakesNoSnapshot() =>
        Assert.Null(ProjectFileGuard.Capture("terse-no-such-project.csproj", ["a.cs"]));
}
