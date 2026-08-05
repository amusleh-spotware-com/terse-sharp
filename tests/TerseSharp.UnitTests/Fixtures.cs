namespace TerseSharp.UnitTests;

public static class Fixtures
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string SolutionPath { get; } =
        Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution", "FixtureSolution.slnx");

    public static string OrderServicePath { get; } =
        Path.Combine(RepositoryRoot, "fixtures", "FixtureSolution", "src", "Fixture.Trading", "OrderService.cs");

    public static string TestProjectPath { get; } = Path.Combine(
        RepositoryRoot, "fixtures", "FixtureSolution", "tests", "Fixture.Trading.Tests", "Fixture.Trading.Tests.csproj");

    public static string TrxRoot { get; } = "C:\\repo";

    public static string Trx(string name) => Path.Combine(RepositoryRoot, "fixtures", "trx", name);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TerseSharp.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("TerseSharp.slnx not found above the test binaries");
    }

    public static string RazorSolutionPath { get; } =
        Path.Combine(RepositoryRoot, "fixtures", "RazorSolution", "RazorSolution.slnx");
}
