namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class FileToolsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task ReadText_WithALineRange_ReturnsOnlyThoseLines()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "appsettings.json",
            ["startLine"] = 2,
            ["endLine"] = 3,
        });

        Assert.Contains("2: ", text, StringComparison.Ordinal);
        Assert.Contains("3: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1: {", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_MatchesTheGlobAndExcludesBuildOutput()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "*.csproj" });

        Assert.Contains("Fixture.Trading.csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("obj", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithADirectoryGlob_MatchesOnTheRelativePath()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/Views/*.xaml" });

        Assert.Contains("OrderView.xaml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Order.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFiles_WithADirectoryGlobThatMatchesNothing_ReportsNoneRatherThanFailing()
    {
        var text = await server.CallAsync("find_files", new() { ["glob"] = "**/NoSuchFolder/*.cs" });

        Assert.Contains("0 files", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithADirectoryGlob_SearchesOnlyThatSubtree()
    {
        var text = await server.CallAsync("search_text", new()
        {
            ["pattern"] = "Button",
            ["glob"] = "**/Views/OrderView.xaml",
        });

        Assert.Contains("OrderView.xaml", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_TagsResultsHeuristic()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "MaxVolume", ["glob"] = "*.json" });

        Assert.Contains("HEURISTIC", text, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchRegex_MatchesAPattern()
    {
        var text = await server.CallAsync("search_regex", new() { ["pattern"] = @"public\s+sealed\s+record", ["glob"] = "*.cs" });

        Assert.Contains("Order.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_ForAMissingFile_ReturnsDocumentNotFound()
    {
        var text = await server.CallAsync("read_text", new() { ["path"] = "nope.json" });

        Assert.Contains("ERROR DocumentNotFound", text, StringComparison.Ordinal);
    }
}
