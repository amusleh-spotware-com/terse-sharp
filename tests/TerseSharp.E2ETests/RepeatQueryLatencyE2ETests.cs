using System.Diagnostics;

namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class RepeatQueryLatencyE2ETests(TerseServerFixture server)
{
    private const int RepeatBudgetMs = 500;
    private const int Repeats = 5;

    [Fact]
    public async Task GetFileOutline_AskedAgainForAnUnchangedFile_CostsFarLessThanTheFirstCall()
    {
        var timings = await TimeAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" });

        Assert.All(timings[1..], elapsed => Assert.True(elapsed <= RepeatBudgetMs, Report("get_file_outline", timings)));
    }

    [Fact]
    public async Task FindUsages_AskedAgainForAnUnchangedSymbol_CostsFarLessThanTheFirstCall()
    {
        var timings = await TimeAsync("find_usages", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
        });

        Assert.All(timings[1..], elapsed => Assert.True(elapsed <= RepeatBudgetMs, Report("find_usages", timings)));
    }

    [Fact]
    public async Task SearchSymbols_AskedAgainForTheSameQuery_CostsFarLessThanTheFirstCall()
    {
        var timings = await TimeAsync("search_symbols", new() { ["query"] = "Order" });

        Assert.All(timings[1..], elapsed => Assert.True(elapsed <= RepeatBudgetMs, Report("search_symbols", timings)));
    }

    private async Task<long[]> TimeAsync(string tool, Dictionary<string, object?> arguments)
    {
        var timings = new long[Repeats];

        for (var index = 0; index < Repeats; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var text = await server.CallAsync(tool, arguments);
            timings[index] = stopwatch.ElapsedMilliseconds;

            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        }

        return timings;
    }

    private static string Report(string tool, long[] timings) =>
        tool + " ms per call: " + string.Join(", ", timings.Select(elapsed => elapsed.ToString(CultureInfo.InvariantCulture)));

    [Fact]
    public async Task OnThisRepositorysOwnSolution_ARepeatedSemanticQuery_IsNotRederivedFromScratch()
    {
        var root = TerseServerFixture.RepositoryRoot;
        var probe = await TerseServerProcess.StartAsync(
            root,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(root, "TerseSharp.slnx")],
            TestContext.Current.CancellationToken);
        try
        {
            var timings = new long[Repeats];

            for (var index = 0; index < Repeats; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                await probe.CallAsync(
                    "get_file_outline",
                    new() { ["path"] = "src/TerseSharp.Core/SymbolEditService.cs" },
                    TestContext.Current.CancellationToken);
                timings[index] = stopwatch.ElapsedMilliseconds;
            }

            Assert.All(timings[1..], elapsed => Assert.True(elapsed <= RepeatBudgetMs, Report("get_file_outline on TerseSharp.slnx", timings)));
        }
        finally
        {
            await probe.StopAsync();
        }
    }
}
