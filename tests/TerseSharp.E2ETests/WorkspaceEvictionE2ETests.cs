namespace TerseSharp.E2ETests;

public sealed class WorkspaceEvictionE2ETests
{
    [Fact]
    public async Task LoadWorkspace_OfAPathTheLimitEvicted_NamesWhenAndByWhichLoad()
    {
        var root = Directory.CreateTempSubdirectory("terse-eviction-").FullName;
        var first = await ProjectAsync(root, "First");
        var second = await ProjectAsync(root, "Second");
        var server = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--max-workspaces", "1", "--workspace", first],
            TestContext.Current.CancellationToken);

        try
        {
            var evicting = await server.CallAsync("load_workspace", new() { ["path"] = second }, TestContext.Current.CancellationToken);
            var reloaded = await server.CallAsync("load_workspace", new() { ["path"] = first }, TestContext.Current.CancellationToken);

            Assert.DoesNotContain("evicted ", evicting, StringComparison.Ordinal);
            Assert.Contains("ago by the load of ", reloaded, StringComparison.Ordinal);
            Assert.Contains("Second.csproj - at most 1 workspace(s) stay loaded", reloaded, StringComparison.Ordinal);
        }
        finally
        {
            await server.StopAsync();
            TerseTempSolution.Delete(root);
        }
    }

    private static async Task<string> ProjectAsync(string root, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        var path = Path.Combine(directory, name + ".csproj");

        await File.WriteAllTextAsync(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "Class1.cs"), "namespace " + name + ";\n\npublic sealed class Class1;\n", TestContext.Current.CancellationToken);

        return path;
    }
}
