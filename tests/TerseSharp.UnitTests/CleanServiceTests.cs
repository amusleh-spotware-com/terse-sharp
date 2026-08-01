using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class CleanServiceTests
{
    [Fact]
    public void Clean_WithDryRun_CountsTheOutputAndLeavesItOnDisk()
    {
        var sandbox = Sandbox();

        try
        {
            var run = Clean(sandbox, null, includeIntermediate: true, dryRun: true);

            Assert.True(run.IsOk);
            Assert.Contains("2 directories", run.Value.Response, StringComparison.Ordinal);
            Assert.Contains("files=2", run.Value.Response, StringComparison.Ordinal);
            Assert.Contains("freedBytes=150", run.Value.Response, StringComparison.Ordinal);
            Assert.Contains("locked=0 state=dryRun", run.Value.Response, StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "obj")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_DeletesBinAndObj_AndKeepsEverythingElse()
    {
        var sandbox = Sandbox();

        try
        {
            var run = Clean(sandbox, null, includeIntermediate: true, dryRun: false);

            Assert.Contains("locked=0 state=deleted", run.Value.Response, StringComparison.Ordinal);
            Assert.Contains("freedBytes=150", run.Value.Response, StringComparison.Ordinal);
            Assert.False(run.Value.Locked);
            Assert.False(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
            Assert.False(Directory.Exists(Path.Combine(sandbox, "App", "obj")));
            Assert.True(File.Exists(Path.Combine(sandbox, "App", "src", "Keep.cs")));
            Assert.True(File.Exists(Path.Combine(sandbox, "App", "App.csproj")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_WithoutIntermediate_LeavesObjAlone()
    {
        var sandbox = Sandbox();

        try
        {
            var run = Clean(sandbox, null, includeIntermediate: false, dryRun: false);

            Assert.Contains("1 directories", run.Value.Response, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "obj")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_NeverTouchesADirectoryMerelyStartingWithBin()
    {
        var sandbox = Sandbox();

        try
        {
            Directory.CreateDirectory(Path.Combine(sandbox, "App", "binaries"));

            Clean(sandbox, null, includeIntermediate: true, dryRun: false);

            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "binaries")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_ForAProjectOutsideTheWorkspace_RefusesWithOutOfWorkspace()
    {
        var sandbox = Sandbox();

        try
        {
            var run = Clean(sandbox, "../../elsewhere", includeIntermediate: true, dryRun: true);

            Assert.False(run.IsOk);
            Assert.Equal(TerseErrorCode.OutOfWorkspace, run.Error!.Code);
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_ForAProjectThatDoesNotExist_RefusesInsteadOfCleaningTheRoot()
    {
        var sandbox = Sandbox();

        try
        {
            var run = Clean(sandbox, "Nope/Nope.csproj", includeIntermediate: true, dryRun: false);

            Assert.False(run.IsOk);
            Assert.Equal(TerseErrorCode.DocumentNotFound, run.Error!.Code);
            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_ScopedToOneProject_LeavesTheOtherProjectsOutput()
    {
        var sandbox = Sandbox();

        try
        {
            var other = Path.Combine(sandbox, "Other");

            Directory.CreateDirectory(Path.Combine(other, "bin"));
            File.WriteAllText(Path.Combine(other, "Other.csproj"), "<Project />");

            Clean(sandbox, "App/App.csproj", includeIntermediate: true, dryRun: false);

            Assert.False(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
            Assert.True(Directory.Exists(Path.Combine(other, "bin")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    [Fact]
    public void Clean_WhenAnOutputIsLocked_ReportsItAndCountsNoFreedBytesForIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete an open file");

        var sandbox = Sandbox();

        try
        {
            using var held = new FileStream(
                Path.Combine(sandbox, "App", "bin", "App.dll"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            var run = Clean(sandbox, null, includeIntermediate: false, dryRun: false);

            Assert.True(run.Value.Locked);
            Assert.Contains("LOCKED", run.Value.Response, StringComparison.Ordinal);
            Assert.Contains("files=0 freedBytes=0 locked=1 state=blocked", run.Value.Response, StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(sandbox, "App", "bin")));
        }
        finally
        {
            Discard(sandbox);
        }
    }

    private static Result<CleanRun> Clean(string root, string? project, bool includeIntermediate, bool dryRun) =>
        CleanService.Clean(
            new WorkspaceTarget(Path.Combine(root, "App", "App.csproj"), root),
            project,
            includeIntermediate,
            dryRun,
            TestContext.Current.CancellationToken);

    private static string Sandbox()
    {
        var root = Directory.CreateTempSubdirectory("terse-clean-").FullName;
        var project = Path.Combine(root, "App");

        Directory.CreateDirectory(Path.Combine(project, "bin"));
        Directory.CreateDirectory(Path.Combine(project, "obj"));
        Directory.CreateDirectory(Path.Combine(project, "src"));
        File.WriteAllText(Path.Combine(project, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(project, "bin", "App.dll"), new string('x', 100));
        File.WriteAllText(Path.Combine(project, "obj", "App.g.cs"), new string('y', 50));
        File.WriteAllText(Path.Combine(project, "src", "Keep.cs"), "class Keep;");

        return root;
    }

    private static void Discard(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
