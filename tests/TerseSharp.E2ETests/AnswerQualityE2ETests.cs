namespace TerseSharp.E2ETests;

internal sealed record Question(string Ask, string Tool, Dictionary<string, object?> Arguments, string[] Facts);

[Collection(nameof(TerseServerCollection))]
public sealed class AnswerQualityE2ETests(TerseServerFixture server)
{
    private const int MinimumQuestions = 14;

    private static Question[] Reference =>
    [
        new(
            "which members does OrderService declare, and on which lines?",
            "get_file_outline",
            new() { ["path"] = "src/Fixture.Trading/OrderService.cs" },
            ["OrderService.Submit", "OrderService.SubmitTwice", ":11-11", "class public"]),
        new(
            "what is the body of OrderService.SubmitTwice?",
            "get_symbol_source",
            new() { ["symbolId"] = "OrderService.SubmitTwice" },
            ["Submit(order) && Submit(order)"]),
        new(
            "which files reference Order, and is any of them a test?",
            "find_usages",
            new() { ["symbolId"] = "T:Fixture.Trading.Order" },
            ["OrderRouter.cs", "Reconciler.cs", "EXACT", "src"]),
        new(
            "who implements IOrderRepository?",
            "find_implementations",
            new() { ["symbolId"] = "T:Fixture.Trading.IOrderRepository" },
            ["InMemoryOrderRepository", "NullOrderRepository"]),
        new(
            "where is IOrderRepository registered for dependency injection?",
            "find_registrations",
            new() { ["query"] = "IOrderRepository" },
            ["Composition.cs", "NullOrderRepository"]),
        new(
            "which declarations are named like OrderService, and where?",
            "search_symbols",
            new() { ["query"] = "OrderService" },
            ["OrderService.cs", "EXACT", "class"]),
        new(
            "which lines of which files contain the literal PendingCount?",
            "search_text",
            new() { ["query"] = "PendingCount", ["glob"] = "**/*.cs" },
            ["OrderService.cs:9", "HEURISTIC"]),
        new(
            "which .csproj files exist, and how big is the production one?",
            "find_files",
            new() { ["glob"] = "**/*.csproj", ["stamps"] = true },
            ["Fixture.Trading.csproj", "Z  "]),
        new(
            "what does line 11 of OrderService.cs say?",
            "read_text",
            new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["startLine"] = 11, ["endLine"] = 11 },
            ["public bool Submit(Order order)"]),
        new(
            "which sections does the solution's appsettings carry?",
            "read_text",
            new() { ["path"] = "appsettings.json" },
            ["MaxVolume"]),
        new(
            "what is the signature and accessibility of OrderService.Unused?",
            "get_symbol",
            new() { ["symbolId"] = "OrderService.Unused" },
            ["int Unused()", "public"]),
        new(
            "which members does the OrderBook type declare?",
            "get_type_outline",
            new() { ["symbolId"] = "T:Fixture.Trading.OrderBook" },
            ["OrderBook.TotalVolume"]),
        new(
            "what does analyze report about dead code in OrderService.cs?",
            "analyze",
            new() { ["path"] = "src/Fixture.Trading/OrderService.cs" },
            ["TERSE001", "NeverCalled", "OrderService.cs"]),
        new(
            "which projects does this solution contain, and how many documents does each have?",
            "list_projects",
            [],
            ["Fixture.Trading", "documents="]),
        new(
            "what did the last commit touching OrderService.cs do?",
            "history",
            new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["maxResults"] = 3 },
            ["commits", " amusleh "]),
        new(
            "what does the shell view's markup contain, and what is its code-behind class?",
            "xaml_outline",
            new() { ["path"] = "src/Fixture.Trading/Views/ShellView.xaml" },
            ["StackPanel", "Button", "TextBlock"]),
        new(
            "is the Symbol binding on the shell view resolvable against its data context?",
            "xaml_bindings",
            new() { ["path"] = "src/Fixture.Trading/Views/ShellView.xaml", ["validate"] = true },
            ["Symbol"]),
    ];

    [Fact]
    public async Task EveryQuestionInTheReferenceSet_IsStillAnsweredByTheToolSurface()
    {
        var unanswered = new List<string>();
        var questions = Reference;

        foreach (var question in questions)
        {
            var text = await server.CallAsync(question.Tool, question.Arguments);
            var missing = question.Facts.Where(fact => !text.Contains(fact, StringComparison.Ordinal)).ToArray();

            if (missing.Length > 0)
                unanswered.Add(question.Ask + " -> missing " + string.Join(" / ", missing) + " [answered: " + ToolCensus.FirstLine(text) + "]");
        }

        Assert.True(
            questions.Length >= MinimumQuestions,
            string.Create(CultureInfo.InvariantCulture, $"the reference set is {questions.Length} questions, below the {MinimumQuestions} this gate needs"));

        Assert.True(
            unanswered.Count is 0,
            string.Create(CultureInfo.InvariantCulture, $"{unanswered.Count}/{questions.Length} reference questions are no longer answered: {string.Join(" | ", unanswered)}"));
    }

    [Fact]
    public async Task TheReferenceSet_ReportsWhatItCostsToAnswerEveryQuestionOnce()
    {
        var tokens = 0;

        foreach (var question in Reference)
            tokens += ToolCensus.Tokens(await server.CallAsync(question.Tool, question.Arguments));

        Assert.True(
            tokens <= 4000,
            string.Create(CultureInfo.InvariantCulture, $"answering the whole reference set costs {tokens} tokens"));
    }
}
