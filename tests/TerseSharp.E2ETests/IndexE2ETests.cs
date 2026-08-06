namespace TerseSharp.E2ETests;

public sealed class IndexE2ETests
{
    private static readonly TimeSpan WatcherDeadline = TimeSpan.FromSeconds(30);

    private const string AddedKey = "IndexedBrush";

    private const string AddedMarkup = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <SolidColorBrush x:Key="IndexedBrush" Color="#405060" />
        </ResourceDictionary>
        """;

    [Fact]
    public async Task XamlResolve_CalledThreeTimes_BuildsTheIndexOnceAndAnswersIdentically()
    {
        await using var solution = await StartAsync(watch: true);

        var first = await Resolve(solution, "AccentBrush");
        var second = await Resolve(solution, "AccentBrush");
        var third = await Resolve(solution, "AccentBrush");

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Contains("scope=", first, StringComparison.Ordinal);
        Assert.Contains("xaml(hit=2 miss=1 files=10)", await Status(solution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidateStylesAndLocalization_ShareOneXamlIndexBuild()
    {
        await using var solution = await StartAsync(watch: true);

        await solution.CallAsync("xaml_validate", new()
        {
            ["path"] = "src/Fixture.Trading/Views/ShellView.xaml",
        });

        await solution.CallAsync("xaml_styles", new() { ["typeName"] = "Button" });
        await solution.CallAsync("xaml_localization", []);

        var status = await Status(solution);

        Assert.Contains("xaml(hit=2 miss=1 files=10)", status, StringComparison.Ordinal);
        Assert.Contains("resx(hit=0 miss=1 ", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFind_AfterXamlLocalization_ReusesTheResxIndex()
    {
        await using var solution = await StartAsync(watch: true);

        await solution.CallAsync("xaml_localization", []);
        await solution.CallAsync("resx_find", new() { ["query"] = "Caption_Count" });

        Assert.Contains("resx(hit=1 miss=1 ", await Status(solution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalXamlCreate_WithTheWatcherOn_RebuildsTheIndexAndResolvesTheNewKey()
    {
        await using var solution = await StartAsync(watch: true);

        await Resolve(solution, "AccentBrush");
        await WriteMarkupAsync(solution);

        Assert.True(
            await PollAsync(() => Resolve(solution, AddedKey), "Indexed.xaml"),
            "the watcher never rebuilt the XAML index after an external create");

        Assert.Contains("files=11", await Status(solution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalCodeChange_LeavesTheXamlIndexIntact()
    {
        await using var solution = await StartAsync(watch: true);

        await Resolve(solution, "AccentBrush");
        await AppendAsync(solution.OrderServicePath, "\npublic sealed class IndexUntouched\n{\n}\n");

        Assert.True(
            await PollAsync(() => Symbols(solution, "IndexUntouched"), "IndexUntouched  class"),
            "the watcher never delivered the external code edit");

        await Resolve(solution, "AccentBrush");

        var status = await PollForAsync(() => Status(solution), "gen=c1/");

        Assert.True(status is not null, "the code generation never advanced after the external edit");
        Assert.Contains("xaml(hit=1 miss=1 files=10)", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalXamlCreate_WithTheWatcherOff_IsResolvedOnTheNextCall()
    {
        await using var solution = await StartAsync(watch: false);

        await Resolve(solution, "AccentBrush");
        await WriteMarkupAsync(solution);

        var resolved = await Resolve(solution, AddedKey);

        Assert.Contains("Indexed.xaml", resolved, StringComparison.Ordinal);
        Assert.Contains("scanned=11 files", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindRegistrations_CalledTwice_ScansTheSolutionOnce()
    {
        await using var solution = await StartAsync(watch: true);

        await solution.CallAsync("find_registrations", new() { ["query"] = "IOrderRepository" });
        await solution.CallAsync("find_registrations", new() { ["query"] = "IOrderRepository" });

        Assert.Contains("code(hit=1 miss=1 ", await Status(solution), StringComparison.Ordinal);
    }

    private static Task<string> Resolve(TerseTempSolution solution, string key) =>
        solution.CallAsync("xaml_resolve", new() { ["key"] = key });

    private static Task<string> Symbols(TerseTempSolution solution, string query) =>
        solution.CallAsync("search_symbols", new() { ["query"] = query });

    private static Task<string> Status(TerseTempSolution solution) =>
        solution.CallAsync("workspace_status", new() { ["verbose"] = true });

    private static Task WriteMarkupAsync(TerseTempSolution solution) => File.WriteAllTextAsync(
        Path.Combine(solution.ProjectDirectory, "Views", "Indexed.xaml"),
        AddedMarkup,
        TestContext.Current.CancellationToken);

    private static async Task AppendAsync(string path, string addition)
    {
        var existing = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(path, existing + addition, TestContext.Current.CancellationToken);
    }

    private static async Task<bool> PollAsync(Func<Task<string>> call, string expected)
    {
        var deadline = DateTime.UtcNow + WatcherDeadline;

        while (DateTime.UtcNow < deadline)
        {
            if ((await call()).Contains(expected, StringComparison.Ordinal))
                return true;

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static Task<TerseTempSolution> StartAsync(bool watch) =>
        TerseTempSolution.StartAsync(watch, TestContext.Current.CancellationToken);
    [Fact]
    public async Task FindRegistrations_AfterAnEditThroughTheSymbolTools_SeesTheNewRegistration()
    {
        await using var solution = await StartAsync(watch: true);

        Assert.Contains("0 registrations", await Registrations(solution), StringComparison.Ordinal);

        var container = await solution.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderService",
            ["declaration"] = "    private void AddSingleton(string name) => _ = name;",
        });

        Assert.Contains("changedLines=", container, StringComparison.Ordinal);

        var registration = await solution.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "OrderService",
            ["declaration"] =
                "    public void RegisterIndexProbe()\n    {\n        var services = this;\n\n        services.AddSingleton(\"IndexProbeService\");\n    }",
        });

        Assert.Contains("changedLines=", registration, StringComparison.Ordinal);

        var found = await Registrations(solution);

        Assert.Contains("IndexProbeService", found, StringComparison.Ordinal);
        Assert.DoesNotContain("0 registrations", found, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlValidate_OnAValidFileTheIndexDoesNotEnumerate_StillValidatesIt()
    {
        await using var solution = await StartAsync(watch: false);
        var directory = Path.Combine(solution.ProjectDirectory, "obj");

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Generated.xaml"),
            AddedMarkup,
            TestContext.Current.CancellationToken);

        var text = await solution.CallAsync("xaml_validate", new()
        {
            ["path"] = "src/Fixture.Trading/obj/Generated.xaml",
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("0 issues", text, StringComparison.Ordinal);
        Assert.Contains("dialect=wpf", text, StringComparison.Ordinal);
    }

    private static Task<string> Registrations(TerseTempSolution solution) =>
        solution.CallAsync("find_registrations", new() { ["query"] = "IndexProbeService" });

    private static async Task<string?> PollForAsync(Func<Task<string>> call, string expected)
    {
        var deadline = DateTime.UtcNow + WatcherDeadline;

        while (DateTime.UtcNow < deadline)
        {
            var answer = await call();

            if (answer.Contains(expected, StringComparison.Ordinal))
                return answer;

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return null;
    }
}
