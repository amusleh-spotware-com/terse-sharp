namespace TerseSharp.UnitTests;

public static class Fixtures
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string SolutionPath { get; } =
        Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution", "FixtureSolution.slnx");

    public static string OrderServicePath { get; } =
        Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution", "src", "Fixture.Trading", "OrderService.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TerseSharp.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("TerseSharp.slnx not found above the test binaries");
    }
}
