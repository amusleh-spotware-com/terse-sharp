using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceTargetTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-workspace-target");

    private static readonly string Trading = Path.Combine(Root, "src", "Trading", "Trading.csproj");

    private static readonly string TradingTests = Path.Combine(Root, "tests", "Trading.Tests", "Trading.Tests.csproj");

    [Fact]
    public void ResolveProject_WithAProjectName_ReturnsThatProjectFile()
    {
        var resolved = Target(Trading, TradingTests).ResolveProject("Trading.Tests");

        Assert.True(resolved.IsOk);
        Assert.Equal(TradingTests, resolved.Value);
    }

    [Fact]
    public void ResolveProject_WithAProjectNameInAnotherCase_StillResolves()
    {
        var resolved = Target(Trading, TradingTests).ResolveProject("trading");

        Assert.Equal(Trading, resolved.Value);
    }

    [Fact]
    public void ResolveProject_WithAnUnknownName_ReportsProjectNotFoundAndNamesTheClosest()
    {
        var resolved = Target(Trading, TradingTests).ResolveProject("Trading.Host");

        Assert.False(resolved.IsOk);
        Assert.Equal(TerseErrorCode.ProjectNotFound, resolved.Error!.Code);
        Assert.Contains("Trading", resolved.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveProject_WithANameSharedByTwoProjects_IsAmbiguousInsteadOfAGuess()
    {
        var other = Path.Combine(Root, "other", "Trading", "Trading.csproj");

        var resolved = Target(Trading, other).ResolveProject("Trading");

        Assert.False(resolved.IsOk);
        Assert.Equal(TerseErrorCode.AmbiguousProject, resolved.Error!.Code);
        Assert.Contains(other, resolved.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveProject_WithNoProjectsKnown_StillReportsProjectNotFound()
    {
        var resolved = new WorkspaceTarget(Path.Combine(Root, "S.slnx"), Root).ResolveProject("Trading");

        Assert.Equal(TerseErrorCode.ProjectNotFound, resolved.Error!.Code);
        Assert.Contains("list_projects", resolved.Error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveProject_WithAPathOutsideTheRoot_IsRefused()
    {
        var resolved = Target(Trading).ResolveProject(Path.Combine(Path.GetTempPath(), "elsewhere", "Other.csproj"));

        Assert.Equal(TerseErrorCode.OutOfWorkspace, resolved.Error!.Code);
    }

    [Fact]
    public void ResolveProject_WithAnExistingRelativePath_ReturnsItWithoutConsultingTheProjectList()
    {
        var sandbox = Directory.CreateTempSubdirectory("terse-target-").FullName;

        try
        {
            var project = Path.Combine(sandbox, "App.csproj");

            File.WriteAllText(project, "<Project />");

            var resolved = new WorkspaceTarget(project, sandbox).ResolveProject("App.csproj");

            Assert.Equal(project, resolved.Value);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static WorkspaceTarget Target(params string[] projects) =>
        new(Path.Combine(Root, "Solution.slnx"), Root, [.. projects]);

    [Fact]
    public void ResolveProject_WithANameCarryingItsProjectExtension_StillResolves()
    {
        var resolved = Target(Trading, TradingTests).ResolveProject("Trading.Tests.csproj");

        Assert.Equal(TradingTests, resolved.Value);
    }

    [Fact]
    public void ResolveProject_WithAPathShapedMissThatDoesNotExist_ReportsProjectNotFound()
    {
        var resolved = Target(Trading).ResolveProject("tests/Nope/Nope.csproj");

        Assert.Equal(TerseErrorCode.ProjectNotFound, resolved.Error!.Code);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("Trading.*")]
    [InlineData("Trading?")]
    public void ResolveProject_WithAWildcardName_RefusesInsteadOfGlobbingTheWorkspace(string project)
    {
        var resolved = Target(Trading, TradingTests).ResolveProject(project);

        Assert.False(resolved.IsOk);
        Assert.Equal(TerseErrorCode.ProjectNotFound, resolved.Error!.Code);
    }

    [Fact]
    public void ResolveProject_WhenTheNameOnlyExistsUnderAnExcludedDirectory_DoesNotResolveToIt()
    {
        var sandbox = Directory.CreateTempSubdirectory("terse-target-").FullName;

        try
        {
            var hidden = Path.Combine(sandbox, ".claude", "worktrees", "agent-1", "App");

            Directory.CreateDirectory(hidden);
            File.WriteAllText(Path.Combine(hidden, "App.csproj"), "<Project />");

            var resolved = new WorkspaceTarget(Path.Combine(sandbox, "S.slnx"), sandbox).ResolveProject("App");

            Assert.False(resolved.IsOk);
            Assert.Equal(TerseErrorCode.ProjectNotFound, resolved.Error!.Code);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void ResolveProject_ForAnAbsolutePathToALoadedProjectOutsideTheRoot_ResolvesItInsteadOfRefusingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-target", "tests", "App.Tests");
        var sibling = Path.Combine(Path.GetTempPath(), "terse-target", "src", "App", "App.csproj");
        var target = new WorkspaceTarget(Path.Combine(root, "App.Tests.csproj"), root, [Path.Combine(root, "App.Tests.csproj"), sibling]);

        var resolved = target.ResolveProject(sibling);

        Assert.True(resolved.IsOk, resolved.Error?.Render());
        Assert.Equal(sibling, resolved.Value);
    }

    [Fact]
    public void ResolveProject_ForAnAbsolutePathOutsideTheRootThatNoLoadedProjectNames_StillRefusesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-target", "tests", "App.Tests");
        var target = new WorkspaceTarget(Path.Combine(root, "App.Tests.csproj"), root, [Path.Combine(root, "App.Tests.csproj")]);

        var resolved = target.ResolveProject(Path.Combine(Path.GetTempPath(), "terse-target", "src", "Other", "Other.csproj"));

        Assert.False(resolved.IsOk);
        Assert.StartsWith("ERROR OutOfWorkspace", resolved.Error!.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveProject_ForAnExistingProjectFileOutsideTheRoot_NamesLoadWorkspaceAndTheWorkspaceToPassNext()
    {
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "terse-i751-" + Guid.NewGuid().ToString("N"))).FullName;
        var project = Path.Combine(outside, "Probe.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);

        try
        {
            var error = Target(Trading).ResolveProject(project).Error!;

            Assert.Equal(TerseErrorCode.OutOfWorkspace, error.Code);
            Assert.Equal("load_workspace path=" + project + " loads it; then pass workspace=Probe to build, run_tests, list_tests or clean", error.Remedy);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void ResolveProject_ForAMissingProjectFileOutsideTheRoot_KeepsThePlainRemedy()
    {
        var resolved = Target(Trading).ResolveProject(Path.Combine(Path.GetTempPath(), "terse-i751-missing-" + Guid.NewGuid().ToString("N"), "Other.csproj"));

        Assert.Equal(TerseErrorCode.OutOfWorkspace, resolved.Error!.Code);
        Assert.Equal("pass a path inside the loaded workspace", resolved.Error.Remedy);
    }

    [Fact]
    public async Task ResolveProject_ForAnExistingNonProjectFileOutsideTheRoot_KeepsThePlainRemedy()
    {
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "terse-i751-" + Guid.NewGuid().ToString("N"))).FullName;
        var notes = Path.Combine(outside, "notes.txt");
        await File.WriteAllTextAsync(notes, string.Empty, TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal("pass a path inside the loaded workspace", Target(Trading).ResolveProject(notes).Error!.Remedy);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }
}
