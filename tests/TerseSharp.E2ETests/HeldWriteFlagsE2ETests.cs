namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class HeldWriteFlagsE2ETests(TerseServerFixture server)
{
    private const string First = "src/Fixture.Trading/HeldFlagProbe.cs";
    private const string Second = "src/Fixture.Trading/HeldFlagProbeTwo.cs";

    [Fact]
    public async Task WriteText_AForcedBatchRolledBack_ReplaysFromItsTokenWithoutForce()
    {
        var rejected = await server.CallAsync("write_text", new()
        {
            ["files"] = Batch("public string Text => 7.ToString(CultureInfo.InvariantCulture);"),
            ["force"] = true,
        });

        try
        {
            Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(TerseServerFixture.FixtureRoot, First)));

            var replayed = await server.CallAsync("write_text", new()
            {
                ["retryWith"] = Token(rejected),
                ["usings"] = new[] { "System.Globalization" },
            });

            Assert.DoesNotContain("ERROR", replayed, StringComparison.Ordinal);
            Assert.StartsWith("using System.Globalization;", await File.ReadAllTextAsync(Path.Combine(TerseServerFixture.FixtureRoot, First), TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(TerseServerFixture.FixtureRoot, Second)));
        }
        finally
        {
            await DeleteAsync();
        }
    }

    [Fact]
    public async Task WriteText_AForcedBatchDryRunThatWouldLand_AppliesFromItsTokenWithoutForce()
    {
        var preview = await server.CallAsync("write_text", new()
        {
            ["files"] = Batch("public int Value => 1;"),
            ["force"] = true,
            ["dryRun"] = true,
        });

        try
        {
            Assert.DoesNotContain("ERROR", preview, StringComparison.Ordinal);

            var applied = await server.CallAsync("write_text", new() { ["retryWith"] = Token(preview) });

            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains("public int Value => 1;", await File.ReadAllTextAsync(Path.Combine(TerseServerFixture.FixtureRoot, First), TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(TerseServerFixture.FixtureRoot, Second)));
        }
        finally
        {
            await DeleteAsync();
        }
    }

    [Fact]
    public async Task WriteText_AForcedSingleFileDryRunThatWouldLand_AppliesFromItsTokenWithoutForce()
    {
        var preview = await server.CallAsync("write_text", new()
        {
            ["path"] = First,
            ["content"] = "namespace Fixture.Trading;\n\npublic sealed class HeldFlagProbe;\n",
            ["force"] = true,
            ["dryRun"] = true,
        });

        try
        {
            var applied = await server.CallAsync("write_text", new() { ["retryWith"] = Token(preview) });

            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains("public sealed class HeldFlagProbe;", await File.ReadAllTextAsync(Path.Combine(TerseServerFixture.FixtureRoot, First), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync();
        }
    }

    private static List<Dictionary<string, object?>> Batch(string member) =>
    [
        new() { ["path"] = First, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class HeldFlagProbe\n{\n    " + member + "\n}\n" },
        new() { ["path"] = Second, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class HeldFlagProbeTwo;\n" },
    ];

    private static string Token(string answer)
    {
        var line = answer.Split('\n').Select(text => text.TrimEnd('\r')).Last(text => text.StartsWith("retryWith=", StringComparison.Ordinal));

        return line["retryWith=".Length..].Split(' ')[0];
    }

    private async Task DeleteAsync()
    {
        await server.CallAsync("write_text", new() { ["path"] = First, ["delete"] = true, ["force"] = true });
        await server.CallAsync("write_text", new() { ["path"] = Second, ["delete"] = true, ["force"] = true });
    }
}
