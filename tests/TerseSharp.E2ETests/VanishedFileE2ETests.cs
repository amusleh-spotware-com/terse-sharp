namespace TerseSharp.E2ETests;

public sealed class VanishedFileE2ETests : IAsyncLifetime
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-e2e-vanished-").FullName;

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.slnx"), "<Solution>\n  <Project Path=\"Probe.csproj\" />\n</Solution>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "Vanish.cs"), "namespace Probe;\n\npublic sealed class Vanish;\n", TestContext.Current.CancellationToken);

        server = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(root, "Probe.slnx")],
            new Dictionary<string, string> { ["TERSE_WATCH"] = "0", ["TERSE_UPDATE"] = "0", ["TERSE_HOME"] = root },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadText_OfAPathNamedAfterATypeWhoseFileLeftTheDisk_NeverNamesTheVanishedFileAsTheDeclaration()
    {
        var outline = await CallAsync("get_file_outline", new() { ["path"] = "Vanish.cs" });

        File.Delete(Path.Combine(root, "Vanish.cs"));

        var text = await CallAsync("read_text", new() { ["path"] = "Other/Vanish.cs" });

        Assert.Contains("Vanish", outline, StringComparison.Ordinal);
        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
        Assert.DoesNotContain("is declared in Vanish.cs", text, StringComparison.Ordinal);
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);
}
