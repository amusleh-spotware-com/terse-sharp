using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TerseConfigFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "terse-tests", Guid.NewGuid().ToString());

    public TerseConfigFileTests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Directory.CreateDirectory(Nested);
        Directory.CreateDirectory(Home);
    }

    private string Repository => Path.Combine(root, "repo");

    private string Nested => Path.Combine(Repository, "src", "App");

    private string Home => Path.Combine(root, "home");

    [Fact]
    public void Chain_ListsTheHomeFileFirstAndTheNearestFileLast()
    {
        var global = Written(Home);
        var repository = Written(Repository);
        var nested = Written(Nested);

        string[] expected = [global, repository, nested];

        Assert.Equal(expected, TerseConfigFile.Chain(Nested, Home));
    }

    [Fact]
    public void Chain_WithNoFileUnderTheRepository_StillCarriesTheHomeFile()
    {
        string[] expected = [Written(Home)];

        Assert.Equal(expected, TerseConfigFile.Chain(Nested, Home));
    }

    [Fact]
    public void Chain_WithNeitherFile_IsEmpty() => Assert.Empty(TerseConfigFile.Chain(Nested, Home));

    [Fact]
    public void Chain_WithNoHomeDirectory_CarriesOnlyWhatTheRepositoryDeclares()
    {
        string[] expected = [Written(Repository)];

        Written(Home);

        Assert.Equal(expected, TerseConfigFile.Chain(Nested, home: null));
    }

    [Fact]
    public void Chain_WithAFileAboveTheRepositoryRoot_LeavesItOutAndKeepsTheHomeFile()
    {
        Written(root);

        string[] expected = [Written(Home)];

        Assert.Equal(expected, TerseConfigFile.Chain(Nested, Home));
    }

    [Fact]
    public void Chain_ForADirectoryThatDoesNotExist_IsEmpty() =>
        Assert.Empty(TerseConfigFile.Chain(Path.Combine(root, "absent"), home: null));

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string Written(string directory)
    {
        var path = Path.Combine(directory, TerseConfigFile.FileName);

        File.WriteAllText(path, "{}");

        return path;
    }
}
