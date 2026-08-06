using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ProjectGlobsTests
{
    [Fact]
    public void CompilesByGlob_ForAnSdkProjectWithNoOptOut_IsTrue()
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
