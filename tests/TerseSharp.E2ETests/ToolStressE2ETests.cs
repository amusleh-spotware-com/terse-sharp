namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolStressE2ETests(TerseServerFixture server)
{
    private const string SubmitId = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)";

    [Fact]
    public async Task FindUsages_RepeatedManyTimes_IsByteForByteIdentical()
    {
        var answers = await RepeatAsync(20, () => server.CallAsync("find_usages", new() { ["symbolId"] = SubmitId }));

        Assert.Single(answers.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Analyze_RepeatedManyTimes_IsByteForByteIdentical()
    {
        var answers = await RepeatAsync(10, () => server.CallAsync("analyze", new() { ["minSeverity"] = "info" }));

        Assert.Single(answers.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SearchSymbols_RepeatedManyTimes_RanksIdentically()
    {
        var answers = await RepeatAsync(20, () => server.CallAsync("search_symbols", new() { ["query"] = "Order" }));

        Assert.Single(answers.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetDiagnostics_RepeatedManyTimes_IsByteForByteIdentical()
    {
        var answers = await RepeatAsync(10, () => server.CallAsync("get_diagnostics", new() { ["minSeverity"] = "info" }));

        Assert.Single(answers.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ManyReadToolsInParallel_AllAnswerAndTheServerStaysHealthy()
    {
        var work = Enumerable.Range(0, 40).Select(index => Call(index)).ToArray();
        var answers = await Task.WhenAll(work);

        Assert.All(answers, answer => Assert.False(string.IsNullOrWhiteSpace(answer)));
        Assert.All(answers, answer => Assert.DoesNotContain("ERROR", answer, StringComparison.Ordinal));
        Assert.Contains("projects=", await server.CallAsync("workspace_status", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASearchOverEveryFileWithNoGlob_CompletesAndSkipsBinaries()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = "Order", ["maxResults"] = 500 });

        Assert.Contains("matches", text, StringComparison.Ordinal);
        Assert.DoesNotContain("logo.png", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVeryLargeMaxResults_IsClampedRatherThanExhausting()
    {
        var text = await server.CallAsync("search_symbols", new() { ["query"] = "O", ["maxResults"] = 100000 });

        Assert.Contains("symbols", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVeryLongPattern_IsHandledWithoutFailing()
    {
        var text = await server.CallAsync("search_text", new() { ["pattern"] = new string('z', 20000) });

        Assert.Contains("0 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedOutlineCalls_DoNotDegradeOrLeak()
    {
        var answers = await RepeatAsync(30, () => server.CallAsync(
            "get_file_outline",
            new() { ["path"] = "src/Fixture.Trading/OrderBook.cs" }));

        Assert.Single(answers.Distinct(StringComparer.Ordinal));
        Assert.All(answers, answer => Assert.Contains("T:Fixture.Trading.OrderBook", answer, StringComparison.Ordinal));
    }

    private Task<string> Call(int index) => (index % 5) switch
    {
        0 => server.CallAsync("get_file_outline", new() { ["path"] = "src/Fixture.Trading/OrderService.cs" }),
        1 => server.CallAsync("find_usages", new() { ["symbolId"] = SubmitId }),
        2 => server.CallAsync("search_symbols", new() { ["query"] = "Order" }),
        3 => server.CallAsync("list_projects", []),
        _ => server.CallAsync("workspace_status", []),
    };

    private static async Task<string[]> RepeatAsync(int times, Func<Task<string>> call)
    {
        var answers = new string[times];

        for (var index = 0; index < times; index++)
            answers[index] = await call();

        return answers;
    }
}
