using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class GuardProfileCoherenceE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task EveryToolTheGuardNames_IsEitherInTheCoreProfileOrInTheRecordedDebt()
    {
        var advertised = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToArray();
        var named = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var verdict in Verdicts())
            Collect(verdict, advertised, named);

        var missing = named.Where(name => !ToolProfile.CoreTools.Contains(name)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(named.Count >= 10, string.Join(", ", named));
        Assert.True(
            missing.Length <= MaxUnadvertisedGuardTools,
            $"the core profile hides {missing.Length} tools the guard names: {string.Join(", ", missing)}");
    }

    private const int MaxUnadvertisedGuardTools = 33;

    private static void Collect(GuardVerdict verdict, string[] advertised, SortedSet<string> named)
    {
        Assert.True(verdict.Denied, verdict.Reason);

        var text = verdict.Reason + " " + verdict.Routing;

        foreach (var tool in advertised)
        {
            if (text.Contains(tool, StringComparison.Ordinal))
                named.Add(tool);
        }
    }

    private static IEnumerable<GuardVerdict> Verdicts()
    {
        foreach (var path in Paths)
        {
            yield return ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = path });
            yield return ToolGuard.Inspect("Edit", new JsonObject { ["file_path"] = path });
            yield return ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "cat " + path });
        }

        foreach (var type in new[] { "cs", "xaml", "razor" })
            yield return ToolGuard.Inspect("Grep", new JsonObject { ["type"] = type, ["pattern"] = "Order" });

        yield return ToolGuard.Inspect("Glob", new JsonObject { ["pattern"] = "**/*.cs" });

        foreach (var command in Commands)
            yield return ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }, TerseServerFixture.RepositoryRoot);
    }

    private static string[] Paths { get; } =
        ["src/App/OrderService.cs", "Views/Main.xaml", "src/App/Strings.resx", "Pages/Counter.razor", "src/App/App.csproj"];

    private static string[] Commands { get; } =
    [
        "dotnet build",
        "dotnet test",
        "dotnet clean",
        "dotnet format",
        "dotnet format analyzers",
        "dotnet format style",
        "git status",
        "git diff",
        "git ls-files",
    ];
}
