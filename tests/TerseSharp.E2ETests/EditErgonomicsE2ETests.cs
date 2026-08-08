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

        Assert.StartsWith("1/3 styles truncated", text, StringComparison.Ordinal);
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

    [Fact]
    public async Task EditText_WithSeveralEdits_AppliesThemAllAsOneWrite()
    {
        const string Probe = "terse-batch-edit-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\ngamma\n" });
        try
        {
            var applied = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "alpha", ["newText"] = "ALPHA" },
                new Dictionary<string, object?> { ["oldText"] = "gamma", ["newText"] = "GAMMA" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("edits=2/2", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("FAILED", applied, StringComparison.Ordinal);
            Assert.Contains("ALPHA", after, StringComparison.Ordinal);
            Assert.Contains("GAMMA", after, StringComparison.Ordinal);
            Assert.Contains("beta", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WhenOneEditOfABatchDoesNotMatch_LandsTheRestAndNamesTheFailure()
    {
        const string Probe = "terse-batch-edit-partial-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\n" });
        try
        {
            var applied = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "alpha", ["newText"] = "ALPHA" },
                new Dictionary<string, object?> { ["oldText"] = "nowhere", ["newText"] = "x" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("edits=1/2", applied, StringComparison.Ordinal);
            Assert.Contains("FAILED edit 2", applied, StringComparison.Ordinal);
            Assert.Contains("matched 0 times", applied, StringComparison.Ordinal);
            Assert.Contains("remedy:", applied, StringComparison.Ordinal);
            Assert.Contains("ALPHA", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithMoreEditsThanTheCap_RefusesInsteadOfApplyingSome()
    {
        const string Probe = "terse-batch-cap-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha0\nalpha1\nalpha2\n" });
        try
        {
            var edits = Enumerable
                .Range(0, 11)
                .Select(index => (object)new Dictionary<string, object?>
                {
                    ["oldText"] = "alpha" + index.ToString(CultureInfo.InvariantCulture),
                    ["newText"] = "beta",
                })
                .ToArray();

            var refused = await server.CallAsync("edit_text", new() { ["path"] = Probe, ["edits"] = edits });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("ERROR", refused, StringComparison.Ordinal);
            Assert.Contains("at most 10", refused, StringComparison.Ordinal);
            Assert.Contains("remedy:", refused, StringComparison.Ordinal);
            Assert.Contains("alpha0", after, StringComparison.Ordinal);
            Assert.DoesNotContain("beta", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithNeitherNewTextNorEdits_NamesBothSpellings()
    {
        var text = await server.CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("newText", text, StringComparison.Ordinal);
        Assert.Contains("edits", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WhenTheAnchorMatchesSeveralTimes_ShowsTheCandidateLines()
    {
        const string Probe = "terse-occurrence-probe.md";
        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "| row |\n| keep |\n| row |\n| keep |\n| row |\n",
        });
        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["oldText"] = "| row |",
                ["newText"] = "| picked |",
            });

            Assert.Contains("matched 3 times", refused, StringComparison.Ordinal);
            Assert.Contains("occurrence=1  line 1:", refused, StringComparison.Ordinal);
            Assert.Contains("occurrence=3  line 5:", refused, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task ASuccessfulEdit_DoesNotRepeatTheFileCountAboveThePerFileLine()
    {
        var applied = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "=> 8;",
        });
        var restored = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "=> 7;",
        });

        Assert.DoesNotContain("files changed", applied, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs  changedLines=", applied, StringComparison.Ordinal);
        Assert.Contains("changedLines=", restored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithBothATopLevelEditAndABatch_RefusesInsteadOfDroppingOne()
    {
        const string Probe = "terse-batch-plus-single-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\n" });
        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["oldText"] = "alpha",
                ["newText"] = "ALPHA",
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "beta", ["newText"] = "BETA" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("ERROR", refused, StringComparison.Ordinal);
            Assert.Contains("silently dropped", refused, StringComparison.Ordinal);
            Assert.Contains("alpha", after, StringComparison.Ordinal);
            Assert.Contains("beta", after, StringComparison.Ordinal);
            Assert.DoesNotContain("BETA", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }
}
