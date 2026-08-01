namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class EditErgonomicsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task AddMember_WithTwoMutuallyReferencingMembers_LandsThemInOneEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "private static int Doubled(int value) => Halved(value) * 4;\n\nprivate static int Halved(int value) => value / 2;",
            ["dryRun"] = true,
        });

        Assert.Contains("Doubled", text, StringComparison.Ordinal);
        Assert.Contains("Halved", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithTwoOverloads_ReplacesTheTargetWithBoth()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["declaration"] = "public int Unused() => 7;\n\npublic int Unused(int extra) => 7 + extra;",
            ["dryRun"] = true,
            ["allowErrors"] = true,
        });

        Assert.DoesNotContain("is not exactly one member", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_OnAnExpressionBodiedMember_AcceptsABareExpression()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "42",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0161", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlStyles_CapsItsResultsAndSaysSo()
    {
        var text = await server.CallAsync("xaml_styles", new()
        {
            ["typeName"] = "Button",
            ["maxResults"] = 1,
        });

        Assert.StartsWith("xaml_styles", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithAnAbsolutePathOutsideTheWorkspace_ReadsItAndSaysSo()
    {
        var outside = Path.Combine(Path.GetTempPath(), "terse-outside-workspace.md");

        await File.WriteAllTextAsync(outside, "# Outside\n\nbody\n", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("read_text", new() { ["path"] = outside });

            Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
            Assert.Contains("# Outside", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AmbiguousWorkspace", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task WriteText_OutsideTheWorkspace_IsStillRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "terse-outside-write.md");
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = outside,
            ["content"] = "no",
        });

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.False(File.Exists(outside));
    }
}
