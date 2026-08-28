namespace TerseSharp.E2ETests;

[Collection(nameof(PolicySolutionCollection))]
public sealed class PolicyE2ETests : IAsyncLifetime
{
    private static readonly string PolicyRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "PolicySolution");

    private static readonly string LedgerPath =
        Path.Combine(PolicyRoot, "src", "Fixture.Policy", "Ledger.cs");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            PolicyRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(PolicyRoot, "PolicySolution.slnx")],
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task AddMember_WithADeclarationThatViolatesTheProjectPolicy_IsRolledBackAndNamesTheRuleAndTheFix()
    {
        var response = await AddAsync("public int Go() => 1;", allowPolicy: false);

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE105", response, StringComparison.Ordinal);
        Assert.Contains("Ledger.Go", response, StringComparison.Ordinal);
        Assert.Contains("'Go' is 2 characters", response, StringComparison.Ordinal);
        Assert.Contains("fix: ", response, StringComparison.Ordinal);
        Assert.Contains("allowPolicy=true", response, StringComparison.Ordinal);
        Assert.Contains("retryWith=", response, StringComparison.Ordinal);
        Assert.Equal(await OriginalAsync(), await CurrentAsync());
    }

    [Fact]
    public async Task AddMember_WithAllowPolicy_LandsTheEditAndNamesEveryRuleItBypassed()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync("public int Go() => 1;", allowPolicy: true);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", response, StringComparison.Ordinal);
            Assert.Contains("TERSE105", response, StringComparison.Ordinal);
            Assert.Contains("Go", await CurrentAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task AddMember_WithARuleTheProjectSetToWarn_LandsTheEditAndReportsTheWarning()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync(Long(), allowPolicy: false);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy  TERSE101", response, StringComparison.Ordinal);
            Assert.Contains("11 statements", response, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task AddMember_WithAConformingDeclaration_AppliesWithNoPolicyLineAtAll()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync("public int Balance() => 0;", allowPolicy: false);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.DoesNotContain("WARNING policy", response, StringComparison.Ordinal);
            Assert.DoesNotContain("PolicyViolation", response, StringComparison.Ordinal);
            Assert.DoesNotContain("TERSE1", response, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    private static string Long() =>
        "public int Long()\n{\n" + string.Concat(Enumerable.Range(0, 10).Select(index => "    var a" + index.ToString(CultureInfo.InvariantCulture) + " = " + index.ToString(CultureInfo.InvariantCulture) + ";\n")) + "    return 0;\n}";

    private Task<string> AddAsync(string declaration, bool allowPolicy) =>
        server.CallAsync(
            "add_member",
            new()
            {
                ["typeSymbolId"] = "Ledger",
                ["declaration"] = declaration,
                ["allowPolicy"] = allowPolicy,
            },
            TestContext.Current.CancellationToken);

    private static Task<string> CurrentAsync() =>
        File.ReadAllTextAsync(LedgerPath, TestContext.Current.CancellationToken);

    private static Task<string> OriginalAsync() => CurrentAsync();

    private static async Task RestoreAsync(string content)
    {
        var written = File.GetLastWriteTimeUtc(LedgerPath);

        await File.WriteAllTextAsync(LedgerPath, content, TestContext.Current.CancellationToken);

        File.SetLastWriteTimeUtc(LedgerPath, written);
    }

    [Fact]
    public async Task AddMember_ReplayingAPolicyRejectionWithItsToken_LandsTheHeldDeclarationWithoutResendingIt()
    {
        var before = await OriginalAsync();

        try
        {
            var rejected = await AddAsync("public int Go() => 1;", allowPolicy: false);
            var token = Token(rejected);

            var response = await server.CallAsync(
                "add_member",
                new() { ["retryWith"] = token, ["allowPolicy"] = true },
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", response, StringComparison.Ordinal);
            Assert.Contains("Go", await CurrentAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    private static string Token(string rejected)
    {
        var marker = rejected.IndexOf("retryWith=", StringComparison.Ordinal);

        Assert.True(marker >= 0, "the rejection carried no retryWith token");

        var start = marker + "retryWith=".Length;
        var length = rejected.AsSpan(start).IndexOfAny(' ', '\r', '\n');

        return length < 0 ? rejected[start..] : rejected.Substring(start, length);
    }

    [Fact]
    public async Task WriteText_WithContentTheProjectPolicyRejects_IsRolledBackUnlessAllowPolicyIsPassed()
    {
        const string probe = "src/Fixture.Policy/PolicyWriteProbe.cs";
        const string content = "namespace Fixture.Policy;\n\npublic sealed class PolicyWriteProbe\n{\n    public int Go() => 1;\n}\n";

        try
        {
            var rejected = await WrittenAsync(probe, content, allowPolicy: false);
            var applied = await WrittenAsync(probe, content, allowPolicy: true);

            Assert.Contains("ERROR", rejected, StringComparison.Ordinal);
            Assert.Contains("TERSE105", rejected, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", applied, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync(
                "write_text",
                new() { ["path"] = probe, ["delete"] = true, ["force"] = true },
                TestContext.Current.CancellationToken);
        }
    }

    private Task<string> WrittenAsync(string path, string content, bool allowPolicy) =>
        server.CallAsync(
            "write_text",
            new()
            {
                ["path"] = path,
                ["content"] = content,
                ["force"] = true,
                ["allowPolicy"] = allowPolicy,
            },
            TestContext.Current.CancellationToken);
}
