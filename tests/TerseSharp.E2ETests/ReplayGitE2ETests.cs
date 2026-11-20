namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ReplayGitE2ETests(TerseServerFixture server)
{
    private const string Probe = "src/Fixture.Trading/ReplayGitProbe.cs";

    private static string Repository => TerseServerFixture.RepositoryRoot;

    [Fact]
    public async Task Analyze_WithABaseRefRepeatedWithNoWriteAndNoGitMove_ReplaysUnchangedAndRunsAgainOnceThatRefMoves()
    {
        var reference = "refs/terse-replay-probe/" + Guid.NewGuid().ToString("N");
        var arguments = new Dictionary<string, object?> { ["path"] = Probe, ["minSeverity"] = "info", ["baseRef"] = reference };

        await GitAsync("update-ref", reference, "HEAD");
        await WriteProbeAsync("private int Hidden() => 1;");
        try
        {
            var first = await AnalyzeAsync(arguments);
            var settled = await SettledAsync(arguments);

            await GitAsync("update-ref", reference, "HEAD~1");
            var moved = await AnalyzeAsync(arguments);

            Assert.DoesNotContain("UNCHANGED", first, StringComparison.Ordinal);
            Assert.Contains("analyze UNCHANGED", settled, StringComparison.Ordinal);
            Assert.DoesNotContain("UNCHANGED", moved, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
            await GitAsync("update-ref", "-d", reference);
        }
    }

    [Fact]
    public async Task Analyze_WithBaseRefHeadRepeatedAfterAWrite_RunsAgainInsteadOfReplaying()
    {
        var arguments = new Dictionary<string, object?> { ["path"] = Probe, ["minSeverity"] = "info", ["baseRef"] = "HEAD" };

        await WriteProbeAsync("private int Hidden() => 1;");
        try
        {
            var settled = await SettledAsync(arguments);

            await WriteProbeAsync("private int Buried() => 2;");
            var after = await AnalyzeAsync(arguments);

            Assert.Contains("analyze UNCHANGED", settled, StringComparison.Ordinal);
            Assert.DoesNotContain("UNCHANGED", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    private Task<string> WriteProbeAsync(string member) => server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class ReplayGitProbe\n{\n    " + member + "\n}\n", ["force"] = true, ["allowErrors"] = true });

    private Task<string> AnalyzeAsync(Dictionary<string, object?> arguments) => server.CallAsync("analyze", [with(arguments)]);

    private async Task<string> SettledAsync(Dictionary<string, object?> arguments)
    {
        var answer = await AnalyzeAsync(arguments);

        for (var attempt = 0; attempt < 150 && !answer.Contains("analyze UNCHANGED", StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            answer = await AnalyzeAsync(arguments);
        }

        return answer;
    }

    private static Task<string> GitAsync(params string[] arguments) => GitToolsE2ETests.GitOutputAsync(Repository, arguments);
}
