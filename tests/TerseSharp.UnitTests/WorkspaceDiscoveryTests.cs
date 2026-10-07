using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceDiscoveryTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-discovery-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public async Task SolutionIn_ADirectoryWithOneSolution_AnswersItsFullPath()
    {
        var solution = await TouchAsync("Only.slnx");

        Assert.Equal(solution, WorkspaceDiscovery.SolutionIn(directory.FullName).Value);
    }

    [Fact]
    public async Task SolutionIn_ASolutionBesideItsFilter_AnswersTheWholeSolution()
    {
        var solution = await TouchAsync("Repo.slnx");
        await TouchAsync("Repo.slnf");

        Assert.Equal(solution, WorkspaceDiscovery.SolutionIn(directory.FullName).Value);
    }

    [Fact]
    public async Task SolutionIn_ASolutionBesideAProject_AnswersTheSolution()
    {
        var solution = await TouchAsync("Repo.sln");
        await TouchAsync("Repo.csproj");

        Assert.Equal(solution, WorkspaceDiscovery.SolutionIn(directory.FullName).Value);
    }

    [Fact]
    public async Task SolutionIn_TwoSolutions_RefusesAndNamesBothAsPathsToPass()
    {
        await TouchAsync("One.slnx");
        await TouchAsync("Two.sln");

        var error = WorkspaceDiscovery.SolutionIn(directory.FullName).Error;

        Assert.NotNull(error);
        Assert.Equal(TerseErrorCode.InvalidArgument, error.Code);
        Assert.Contains("holding 2 candidates", error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Join(directory.FullName, "One.slnx"), error.Remedy, StringComparison.Ordinal);
        Assert.Contains(Path.Join(directory.FullName, "Two.sln"), error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionIn_ADirectoryWithASolutionOnlyBelowIt_PointsAtDiscover()
    {
        directory.CreateSubdirectory("nested");
        await TouchAsync(Path.Join("nested", "Deep.slnx"));

        var error = WorkspaceDiscovery.SolutionIn(directory.FullName).Error;

        Assert.NotNull(error);
        Assert.Contains("directly in it", error.Message, StringComparison.Ordinal);
        Assert.Contains("discover=true", error.Remedy, StringComparison.Ordinal);
    }

    private async Task<string> TouchAsync(string name)
    {
        var path = Path.Join(directory.FullName, name);
        await File.WriteAllTextAsync(path, string.Empty, TestContext.Current.CancellationToken);
        return path;
    }
}
