namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class GlobalConfigE2ETests : IAsyncLifetime
{
    private const string HomeConfig = """
        {
          "policy": {
            "action": "off",
            "rules": {
              "comments": { "action": "reject" },
              "methodNameLength": { "action": "reject" }
            }
          }
        }
        """;

    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-global", Guid.NewGuid().ToString("N"));

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(home);

        await File.WriteAllTextAsync(
            Path.Combine(home, ".terse.json"),
            HomeConfig,
            TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            TerseServerFixture.FixtureRoot,
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--tools",
                "all",
                "--workspace",
                Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx")
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
    public async Task AddMember_WithACommentWhereTheHomeConfigRejectsThem_IsRolledBackInASolutionCarryingNoConfigOfItsOwn()
    {
        var response = await AddAsync("public int Settled() => 1; // the settled count");

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE112", response, StringComparison.Ordinal);
        Assert.Contains("OrderBook.Settled", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithARuleTheAmbientPolicyNeverEnforces_ProvesTheHomeConfigWasRead()
    {
        var response = await AddAsync("public int Do() => 1;");

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE105", response, StringComparison.Ordinal);
    }

    private Task<string> AddAsync(string declaration) => server.CallAsync(
        "add_member",
        new Dictionary<string, object?>
        {
            ["typeSymbolId"] = "OrderBook",
            ["declaration"] = declaration,
        },
        TestContext.Current.CancellationToken);
}
