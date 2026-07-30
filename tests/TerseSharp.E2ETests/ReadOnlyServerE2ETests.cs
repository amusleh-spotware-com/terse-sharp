using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TerseSharp.E2ETests;

public sealed class ReadOnlyServerE2ETests : IAsyncLifetime
{
    private McpClient client = null!;

    public async ValueTask InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "terse-sharp-readonly",
            Command = "dotnet",
            Arguments =
            [
                TerseServerFixture.ServerAssemblyPath(),
                "serve",
                "--read-only",
                "--workspace",
                Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
            ],
            WorkingDirectory = TerseServerFixture.FixtureRoot,
        });

        client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await client.DisposeAsync();

    [Fact]
    public async Task ReadTools_StillWork()
    {
        var text = await CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" });

        Assert.Contains("T:Fixture.Trading.OrderService", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryMutatingTool_IsRefusedWithTheReadOnlyCode()
    {
        var refusals = new[]
        {
            await CallAsync("replace_symbol_body", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused", ["body"] = "{ return 1; }" }),
            await CallAsync("rename_symbol", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused", ["newName"] = "Renamed" }),
            await CallAsync("delete_symbol", new() { ["symbolId"] = "M:Fixture.Trading.OrderService.Unused" }),
            await CallAsync("write_text", new() { ["path"] = "scratch.txt", ["content"] = "x" }),
            await CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100", ["newText"] = "200" }),
        };

        Assert.All(refusals, text => Assert.Contains("ERROR ReadOnly", text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusedWrite_LeavesTheFileUntouched()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "appsettings.json");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100", ["newText"] = "200" });

        Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
