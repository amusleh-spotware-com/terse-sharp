using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolRobustnessE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task EveryTool_WithGarbageArguments_AnswersWithAStructuredErrorAndNoStackTrace()
    {
        var tools = await Surface();
        var checked_ = 0;

        foreach (var tool in tools)
        {
            AssertHandled(tool.Name, await CallAsync(tool, Garbage(tool)));
            checked_++;
        }

        Assert.True(checked_ >= 45, $"only {checked_} tools were exercised");
    }

    [Fact]
    public async Task EveryTool_WithNoArguments_NeverCrashesTheServer()
    {
        foreach (var tool in await Surface())
            AssertHandled(tool.Name, await CallAsync(tool, []));

        Assert.Contains("projects=", await server.CallAsync("workspace_status", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryTool_WithEmptyStringArguments_AnswersWithoutThrowing()
    {
        foreach (var tool in await Surface())
            AssertHandled(tool.Name, await CallAsync(tool, Empty(tool)));

        Assert.Contains("projects=", await server.CallAsync("workspace_status", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheServerSurvivesTheWholeSweep()
    {
        foreach (var tool in await Surface())
            await CallAsync(tool, Garbage(tool));

        var status = await server.CallAsync("workspace_status", []);
        var projects = await server.CallAsync("list_projects", []);

        Assert.Contains("Fixture.Trading", projects, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSweepEverWritesOutsideTheWorkspaceOrTouchesTheSolution()
    {
        var sentinels = Sentinels();
        var before = sentinels.Select(File.ReadAllBytes).ToArray();

        foreach (var tool in await Surface())
        {
            await CallAsync(tool, Garbage(tool));
            await CallAsync(tool, Empty(tool));
        }

        var after = sentinels.Select(File.ReadAllBytes).ToArray();

        for (var index = 0; index < sentinels.Length; index++)
            Assert.True(before[index].AsSpan().SequenceEqual(after[index]), sentinels[index] + " was modified");
    }

    private static string[] Sentinels() =>
    [
        Path.Combine(TerseServerFixture.RepositoryRoot, "Directory.Packages.props"),
        Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx"),
        Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Fixture.Trading.csproj"),
    ];

    private async Task<IReadOnlyList<McpClientTool>> Surface()
    {
        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        return [.. tools.Where(tool => !Excluded(tool.Name))];
    }

    private static bool Excluded(string name) =>
        Array.Exists(ToolCensus.RobustnessExcluded, exemption => string.Equals(exemption.Tool, name, StringComparison.Ordinal));

    private async Task<string> CallAsync(McpClientTool tool, Dictionary<string, object?> arguments)
    {
        try
        {
            var result = await server.Client.CallToolAsync(
                tool.Name,
                arguments,
                cancellationToken: TestContext.Current.CancellationToken);

            return string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(block => block.Text));
        }
        catch (McpException exception)
        {
            return "ERROR McpException\n" + exception.Message;
        }
    }

    private static Dictionary<string, object?> Garbage(McpClientTool tool) =>
        Build(tool, property => property.ValueKind is JsonValueKind.Object ? Value(property) : null);

    private static Dictionary<string, object?> Empty(McpClientTool tool) =>
        Build(tool, property => Type(property) is "string" ? string.Empty : Value(property));

    private static Dictionary<string, object?> Build(McpClientTool tool, Func<JsonElement, object?> value)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (!tool.JsonSchema.TryGetProperty("properties", out var properties))
            return arguments;

        foreach (var property in properties.EnumerateObject())
            arguments[property.Name] = value(property.Value);

        return arguments;
    }

    private static object? Value(JsonElement property) => Type(property) switch
    {
        "integer" or "number" => -12345,
        "boolean" => false,
        _ => "../../terse-does-not-exist-" + Guid.NewGuid().ToString("N"),
    };

    private static string Type(JsonElement property) =>
        property.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String
            ? type.GetString() ?? "string"
            : "string";

    private static void AssertHandled(string tool, string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), $"{tool} returned nothing");
        Assert.DoesNotContain("Unhandled exception", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at TerseSharp.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.NullReferenceException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", text, StringComparison.Ordinal);

        if (text.StartsWith("ERROR", StringComparison.Ordinal))
            Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }
}
