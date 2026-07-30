using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TerseSharp.E2ETests;

public sealed class CompileGateE2ETests : IAsyncLifetime
{
    private static readonly string BrokenRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "BrokenSolution");

    private static readonly string CalculatorPath =
        Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Calculator.cs");

    private McpClient client = null!;
    private string original = null!;

    public async ValueTask InitializeAsync()
    {
        original = await File.ReadAllTextAsync(CalculatorPath);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "terse-sharp-broken",
            Command = "dotnet",
            Arguments = [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(BrokenRoot, "BrokenSolution.slnx")],
            WorkingDirectory = BrokenRoot,
        });

        client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        await File.WriteAllTextAsync(CalculatorPath, original);
    }

    [Fact]
    public async Task EditingAValidMember_AppliesEvenThoughTheFileAlreadyHasAnUnrelatedError()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ var first = 1; var second = 2; return first + second; }",
            ["dryRun"] = false,
        });

        Assert.DoesNotContain("CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("applied", text, StringComparison.Ordinal);

        var onDisk = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        Assert.Contains("first + second", onDisk, StringComparison.Ordinal);
        Assert.Contains("this does not compile", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntroducingANewError_IsStillRefusedAndRolledBack()
    {
        var before = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return \"a brand new error\"; }",
            ["dryRun"] = false,
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken));
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
