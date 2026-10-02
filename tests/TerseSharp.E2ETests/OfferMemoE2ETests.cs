namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class OfferMemoE2ETests
{
    [Fact]
    public async Task EveryStandingOffer_IsPrintedOncePerToolPerProcess_AndEveryTruncationSteerOnEveryCall()
    {
        var server = await TerseServerProcess.StartAsync(
            TerseServerFixture.RepositoryRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx")],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERSE_OFFERS"] = "once" },
            TestContext.Current.CancellationToken);

        try
        {
            await SearchTextOffersContainersOnceAndSteersEveryTruncationAsync(server);
            await ReadTextSaysCondensedOnceAsync(server);
            await GetFileOutlineSteersWideOutlineOnceAsync(server);
            await FindFilesAdvertisesNarrowingOnceAsync(server);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    private static async Task SearchTextOffersContainersOnceAndSteersEveryTruncationAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs" });
        var second = await CallAsync(server, "search_text", new() { ["query"] = "Submit", ["glob"] = "src/**/*.cs" });
        var clipped = await CallAsync(server, "search_text", new() { ["query"] = "public", ["glob"] = "src/**/*.cs", ["maxResults"] = 1 });
        var again = await CallAsync(server, "search_text", new() { ["query"] = "class", ["glob"] = "src/**/*.cs", ["maxResults"] = 1 });

        Assert.Contains("containers=true names the C# declaration each hit sits in", first, StringComparison.Ordinal);
        Assert.DoesNotContain("containers=true names", second, StringComparison.Ordinal);
        Assert.Contains("truncated - ", clipped.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(" - narrow with glob=", clipped.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(" - narrow with glob=", again.Split('\n')[0], StringComparison.Ordinal);
    }

    private static async Task ReadTextSaysCondensedOnceAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-17" });
        var second = await CallAsync(server, "read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-16" });

        Assert.Equal("condensed=true - blank lines dropped, a number shown only after a gap; verbose=true numbers every line", first.Split('\n')[1]);
        Assert.True(second.Split('\n').Length < 17, second);
        Assert.DoesNotContain("condensed=true", second, StringComparison.Ordinal);
    }

    private static async Task GetFileOutlineSteersWideOutlineOnceAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "get_file_outline", new() { ["path"] = "src/Fixture.Trading/WideSurface.cs", ["all"] = true });
        var second = await CallAsync(server, "get_file_outline", new() { ["path"] = "src/Fixture.Trading/WideSurface.cs", ["all"] = true, ["signatures"] = false });

        Assert.Contains("45 members - narrow with contains=", first, StringComparison.Ordinal);
        Assert.Contains("WideSurface", second, StringComparison.Ordinal);
        Assert.DoesNotContain("narrow with contains=", second, StringComparison.Ordinal);
    }

    private static async Task FindFilesAdvertisesNarrowingOnceAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "find_files", new() { ["glob"] = "src/**/*.cs" });
        var second = await CallAsync(server, "find_files", new() { ["glob"] = "**/*.cs" });

        Assert.Matches(@"^\d+ files - narrow with ", first.Split('\n')[0]);
        Assert.Matches(@"^\d+ files$", second.Split('\n')[0]);
    }
}
