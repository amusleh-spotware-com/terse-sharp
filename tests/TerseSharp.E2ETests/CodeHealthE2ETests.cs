namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class CodeHealthE2ETests(TerseServerFixture shared) : IAsyncLifetime
{
    private const string Probe = "src/Fixture.Trading/CodeHealthProbe.cs";

    private readonly string home = Path.Combine(Path.GetTempPath(), "terse-codehealth", Guid.NewGuid().ToString("N"));

    private TerseServerProcess policy = null!;

    private static string ProbePath => Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "CodeHealthProbe.cs");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(home);

        await File.WriteAllTextAsync(Path.Combine(home, ".terse.json"), """{"policy":{}}""", TestContext.Current.CancellationToken);

        policy = await TerseServerProcess.StartAsync(
            TerseServerFixture.FixtureRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx")],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERSE_HOME"] = home },
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await policy.StopAsync();

        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task WriteText_ForAnUnhealthyFileWithNoPolicySection_LandsWithNoCodeHealthLine()
    {
        try
        {
            var applied = await WriteAsync(shared.CallAsync, BrainMethodClass("CodeHealthProbe"));

            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("TERSE114", applied, StringComparison.Ordinal);
            Assert.True(File.Exists(ProbePath));
        }
        finally
        {
            await DeleteAsync(shared.CallAsync);
        }
    }

    [Fact]
    public async Task WriteText_ForAnUnhealthyFileUnderTheDefaultPolicy_LandsWithACodeHealthWarningAndAnalyzeReportsIt()
    {
        try
        {
            var applied = await WriteAsync(PolicyCallAsync, BrainMethodClass("CodeHealthProbe"));
            var analyzed = await PolicyCallAsync("analyze", new() { ["path"] = Probe, ["ids"] = "TERSE114" });

            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains("WARNING policy  TERSE114", applied, StringComparison.Ordinal);
            Assert.Contains("code health 7.5 (Brain Method CodeHealthProbe.Brain", applied, StringComparison.Ordinal);
            Assert.Contains("is below 9", applied, StringComparison.Ordinal);
            Assert.True(File.Exists(ProbePath));
            Assert.Contains("TERSE114", analyzed, StringComparison.Ordinal);
            Assert.Contains("code health 7.5", analyzed, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(PolicyCallAsync);
        }
    }

    [Fact]
    public async Task WriteText_ForAHealthyFileUnderTheDefaultPolicy_LandsWithNoCodeHealthLine()
    {
        try
        {
            var applied = await WriteAsync(
                PolicyCallAsync,
                "namespace Fixture.Trading;\n\npublic sealed class CodeHealthProbe\n{\n    public int Double(int amount) => amount * 2;\n}\n");

            Assert.DoesNotContain("TERSE114", applied, StringComparison.Ordinal);
            Assert.True(File.Exists(ProbePath));
        }
        finally
        {
            await DeleteAsync(PolicyCallAsync);
        }
    }

    private static string BrainMethodClass(string name)
    {
        var branches = Enumerable.Range(1, 9).Select(index => string.Create(CultureInfo.InvariantCulture, $"        if (amount == {index}) total += {index};"));
        var statements = Enumerable.Range(0, 70).Select(index => string.Create(CultureInfo.InvariantCulture, $"        total += amount * {index};"));

        return "namespace Fixture.Trading;\n\npublic sealed class " + name + "\n{\n    public int Brain(int amount)\n    {\n        var total = 0;\n"
            + string.Join('\n', branches.Concat(statements))
            + "\n        return total;\n    }\n}\n";
    }

    private Task<string> PolicyCallAsync(string tool, Dictionary<string, object?> arguments) =>
        policy.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    private static async Task<string> WriteAsync(Func<string, Dictionary<string, object?>, Task<string>> call, string content)
    {
        var text = string.Empty;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            text = await call("write_text", new() { ["path"] = Probe, ["content"] = content, ["force"] = true });

            if (!text.StartsWith("ERROR Transient", StringComparison.Ordinal))
                break;

            await DeleteAsync(call);
        }

        return text;
    }

    private static Task<string> DeleteAsync(Func<string, Dictionary<string, object?>, Task<string>> call) =>
        call("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
}
