namespace TerseSharp.E2ETests;

public sealed class ProbeFileE2ETests
{
    private const string Untidy = "namespace Fixture.Trading;\n\ninternal sealed class UntidyProbe\n{\n    public int Value   { get; }   \n}\n";

    [Fact]
    public async Task CleanupVerify_WithFixAll_NamesTheStepBesideEachFileAndTheCiEquivalentPair()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        await solution.CallAsync("write_text", new()
        {
            ["path"] = Path,
            ["force"] = true,
            ["content"] = Untidy,
        });

        var text = await solution.CallAsync("cleanup", new() { ["verify"] = true, ["fix"] = "all", ["path"] = Path });

        Assert.Contains("VERIFY_FAILED", text, StringComparison.Ordinal);
        Assert.Contains("UntidyProbe.cs  whitespace", text, StringComparison.Ordinal);
        Assert.Contains("cleanup verify=true fix=style", text, StringComparison.Ordinal);
        Assert.Contains("cleanup verify=true fix=analyzers", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupVerify_WithFixAnalyzers_DoesNotSteerToTheCiPairItAlreadyIs()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        await solution.CallAsync("write_text", new()
        {
            ["path"] = Path,
            ["force"] = true,
            ["content"] = Untidy,
        });

        var reformatting = await solution.CallAsync("cleanup", new() { ["verify"] = true, ["fix"] = "all", ["path"] = Path });
        var ciEquivalent = await solution.CallAsync("cleanup", new() { ["verify"] = true, ["fix"] = "analyzers", ["path"] = Path });

        Assert.Contains("byte-equivalent CI pair", reformatting, StringComparison.Ordinal);
        Assert.DoesNotContain("byte-equivalent CI pair", ciEquivalent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithSinceLast_DoesNotReportAnExistingOccurrenceAsFixedWhenAnotherWasAdded()
    {
        const string Probe = "src/Fixture.Trading/SinceLastProbe.cs";
        const string Header = "namespace Fixture.Trading;\n\ninternal sealed class SinceLastProbe\n{\n    public int Value() => 1;\n\n    public void Run()\n    {\n        Value();\n";

        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        await solution.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["force"] = true,
            ["content"] = Header + "    }\n}\n",
        });

        var first = await solution.CallAsync("analyze", new() { ["path"] = Probe, ["minSeverity"] = "hidden", ["sinceLast"] = true });

        Assert.Contains("SinceLastProbe.cs:9:9", first, StringComparison.Ordinal);
        Assert.Contains("IDE0058", first, StringComparison.Ordinal);

        await solution.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["force"] = true,
            ["content"] = Header + "        Value();\n    }\n}\n",
        });

        var second = await solution.CallAsync("analyze", new() { ["path"] = Probe, ["minSeverity"] = "hidden", ["sinceLast"] = true });

        Assert.True(second.Contains("SinceLastProbe.cs:10:9", StringComparison.Ordinal), second);
        Assert.False(second.Contains("FIXED IDE0058", StringComparison.Ordinal), second);
    }

    private const string Path = "src/Fixture.Trading/UntidyProbe.cs";
}
