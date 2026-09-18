namespace TerseSharp.E2ETests;

[Collection(nameof(PolicySolutionCollection))]
public sealed class ConfigCascadeE2ETests : IAsyncLifetime
{
    private const string HomeConfig = """
        {
          "policy": {
            "meaninglessSuffixes": ["Ledger"]
          }
        }
        """;

    private static readonly string PolicyRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "PolicySolution");

    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-cascade", Guid.NewGuid().ToString("N"));

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(home);

        await File.WriteAllTextAsync(
            Path.Combine(home, ".terse.json"),
            HomeConfig,
            TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            PolicyRoot,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--tools",
                "all",
                "--workspace",
                Path.Combine(PolicyRoot, "PolicySolution.slnx")
            ],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERSE_HOME"] = home },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();

        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task AddMember_WithASuffixOnlyTheHomeFileDeclares_IsRejectedBecauseTheProjectFileInheritsIt()
    {
        var response = await AddAsync("public sealed class QueueLedger { }");

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE106", response, StringComparison.Ordinal);
        Assert.Contains("ends with 'Ledger'", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithARuleTheProjectFileDeclaresItself_StillAnswersWithTheProjectFilesVerdict()
    {
        var response = await AddAsync("public int Go() => 1;");

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE105", response, StringComparison.Ordinal);
    }

    private Task<string> AddAsync(string declaration) => server.CallAsync(
        "add_member",
        new Dictionary<string, object?>
        {
            ["typeSymbolId"] = "Ledger",
            ["declaration"] = declaration,
        },
        TestContext.Current.CancellationToken);
}
