using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ProjectGlobsTests
{
    private static string Shape(string name) =>
        Path.Combine(Fixtures.RepositoryRoot, "fixtures", "ProjectShapes", name, name + ".csproj");

    [Fact]
    public void CompilesByGlob_ForAnSdkProjectWithNoOptOut_IsTrue()
    {
        MsBuildBootstrap.Ensure();

        Assert.True(ProjectGlobs.CompilesByGlob(Shape("Globbing")));
    }

    [Fact]
    public void CompilesByGlob_WithEnableDefaultItemsFalse_IsFalse()
    {
        MsBuildBootstrap.Ensure();

        Assert.False(ProjectGlobs.CompilesByGlob(Shape("NoDefaultItems")));
    }

    [Fact]
    public void CompilesByGlob_WithTheSdkElementFormAndCompileItemsOff_IsFalse()
    {
        MsBuildBootstrap.Ensure();

        Assert.False(ProjectGlobs.CompilesByGlob(Shape("SdkElement")));
    }

    [Fact]
    public void CompilesByGlob_ForALegacyNonSdkProject_IsFalse()
    {
        MsBuildBootstrap.Ensure();

        Assert.False(ProjectGlobs.CompilesByGlob(Shape("Legacy")));
    }

    [Fact]
    public void CompilesByGlob_ForTheFixtureTestProject_IsTrue()
    {
        MsBuildBootstrap.Ensure();

        Assert.True(ProjectGlobs.CompilesByGlob(Fixtures.TestProjectPath));
    }

    [Fact]
    public void CompilesByGlob_ForAProjectThatDoesNotExist_IsNull()
    {
        MsBuildBootstrap.Ensure();

        Assert.Null(ProjectGlobs.CompilesByGlob("terse-no-such-project.csproj"));
    }
}
