namespace TerseSharp.UnitTests;

public sealed class TemporarySolution : IDisposable
{
    private TemporarySolution(string root)
    {
        Root = root;
        SolutionPath = Path.Combine(root, "FixtureSolution.slnx");
    }

    public string Root { get; }

    public string SolutionPath { get; }

    public string ProjectDirectory => Path.Combine(Root, "src", "Fixture.Trading");

    public string ProjectPath => Path.Combine(ProjectDirectory, "Fixture.Trading.csproj");

    public string OrderServicePath => Path.Combine(ProjectDirectory, "OrderService.cs");

    public static TemporarySolution Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-fixture-" + Guid.NewGuid().ToString("N"));

        Copy(Path.Combine(Fixtures.RepositoryRoot, "fixtures", "FixtureSolution"), root);

        return new TemporarySolution(root);
    }

    public void Dispose() => Delete(Root);

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var directory in Directory.EnumerateDirectories(source).Where(Copyable))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyFile(string file, string destination)
    {
        try
        {
            File.Copy(file, destination);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static bool Copyable(string directory) => Path.GetFileName(directory) is not ("bin" or "obj");

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
