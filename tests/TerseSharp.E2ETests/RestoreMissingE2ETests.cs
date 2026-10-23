namespace TerseSharp.E2ETests;

public sealed class RestoreMissingE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-unrestored-").FullName;

    private TerseServerProcess server = null!;

    private string Fresh => Path.Combine(root, "fresh");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Fresh, ".git"));
        await File.WriteAllTextAsync(Path.Combine(Fresh, "Fresh.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Fresh, "Order.cs"), "namespace Fresh;\n\npublic sealed class Order;\n", TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            Fresh,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all"],
            new Dictionary<string, string> { ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = root },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task LoadWorkspace_OnAProjectNeverRestored_NamesItAndSaysToBuildThenReload()
    {
        var text = await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(Fresh, "Fresh.csproj") }, TestContext.Current.CancellationToken);

        Assert.Contains("restore=missing in 1 project(s): Fresh.csproj", text, StringComparison.Ordinal);
        Assert.Contains("build once, then load_workspace reload=true", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_OnARestoredProject_SaysNothingAboutRestore()
    {
        var text = await server.CallAsync("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "FixtureSolution", "FixtureSolution.slnx") }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("restore=missing", text, StringComparison.Ordinal);
    }
}
