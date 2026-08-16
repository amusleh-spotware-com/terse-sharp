namespace TerseSharp.E2ETests;

public sealed class CompileGateE2ETests : IAsyncLifetime
{
    private static readonly string BrokenRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "BrokenSolution");

    private static readonly string CalculatorPath =
        Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Calculator.cs");

    private TerseServerProcess server = null!;
    private string original = null!;

    public async ValueTask InitializeAsync()
    {
        original = await File.ReadAllTextAsync(CalculatorPath);

        server = await TerseServerProcess.StartAsync(
            BrokenRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(BrokenRoot, "BrokenSolution.slnx")],
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await server.StopAsync();
        await File.WriteAllTextAsync(CalculatorPath, original);
    }

    [Fact]
    public async Task AnEdit_ReportsTheDiagnosticCountsAndTheDeltaItCaused()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return 3; }",
            ["dryRun"] = true,
        });

        Assert.Contains("errors=1 (+0)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("would be rolled back", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunThatWouldBreakTheBuild_SaysSoInsteadOfReportingAZeroDelta()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return undefinedSymbol; }",
            ["dryRun"] = true,
        });

        Assert.Contains("would be rolled back", text, StringComparison.Ordinal);
        Assert.Contains("CS0103", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditingAValidMember_AppliesEvenThoughTheFileAlreadyHasAnUnrelatedError()
    {
        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ var first = 1; var second = 2; return first + second; }",
            ["dryRun"] = false,
        });

        Assert.DoesNotContain("CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=", text, StringComparison.Ordinal);

        var onDisk = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        Assert.Contains("first + second", onDisk, StringComparison.Ordinal);
        Assert.Contains("this does not compile", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntroducingANewError_IsStillRefusedAndRolledBack()
    {
        var before = await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken);

        var text = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return \"a brand new error\"; }",
            ["dryRun"] = false,
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(CalculatorPath, TestContext.Current.CancellationToken));
    }

    private Task<string> CallAsync(string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ARollback_NamesARetryTokenThatHoldsTheRejectedDeclaration()
    {
        var rejected = await CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["declaration"] = "public int Healthy() => MissingHelperThatDoesNotExist();",
        });

        var token = Token(rejected);
        var retried = await CallAsync("replace_symbol", new() { ["retryWith"] = token, ["dryRun"] = true });
        var unknown = await CallAsync("replace_symbol", new() { ["retryWith"] = "r9999" });

        Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
        Assert.Contains("retryWith=", rejected, StringComparison.Ordinal);
        Assert.Contains("MissingHelperThatDoesNotExist", retried, StringComparison.Ordinal);
        Assert.Contains("ERROR", unknown, StringComparison.Ordinal);
        Assert.Contains("names no held rejection", unknown, StringComparison.Ordinal);
    }

    private static string Token(string rejection)
    {
        var marker = rejection.IndexOf("retryWith=", StringComparison.Ordinal) + "retryWith=".Length;
        var tail = rejection.AsSpan(marker);
        var end = tail.IndexOfAny(" \r\n");

        return new string(end < 0 ? tail : tail[..end]);
    }

    [Fact]
    public async Task ARejectedDiagnostic_NamesItsFileRelativeToTheWorkspaceRoot()
    {
        var rejected = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["body"] = "{ return \"a rejected string\"; }",
        });

        Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("src", "Fixture.Broken", "Calculator.cs"), rejected, StringComparison.Ordinal);
        Assert.DoesNotContain(BrokenRoot, rejected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryToken_ReplayedAgainstAnotherWorkspace_IsRefusedInsteadOfEditingIt()
    {
        await CallAsync("load_workspace", new()
        {
            ["path"] = Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "FixtureSolution", "FixtureSolution.slnx"),
        });

        var rejected = await CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["declaration"] = "public int Healthy() => AHelperThatIsNowhere();",
            ["workspace"] = "BrokenSolution",
        });

        var replayed = await CallAsync("replace_symbol", new()
        {
            ["retryWith"] = Token(rejected),
            ["workspace"] = "FixtureSolution",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
        Assert.Contains("the held rejection belongs to", replayed, StringComparison.Ordinal);
        Assert.Contains("BrokenSolution", replayed, StringComparison.Ordinal);
        Assert.DoesNotContain("changedLines=", replayed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_CreatingANewFileWhoseCodeDoesNotCompile_IsRolledBackByTheCompileGate()
    {
        var full = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "GateProbe.cs");

        try
        {
            var text = await CallAsync("write_text", new()
            {
                ["path"] = "src/Fixture.Broken/GateProbe.cs",
                ["content"] = "namespace Fixture.Broken;\n\npublic static class GateProbe\n{\n    public static int Probe() => undefinedSymbol;\n}\n",
                ["force"] = true,
            });

            Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
            Assert.Contains("CS0103", text, StringComparison.Ordinal);
            Assert.False(File.Exists(full));
        }
        finally
        {
            File.Delete(full);
        }
    }

    [Fact]
    public async Task WriteText_CreatingAValidNewFile_AppliesAndLeavesTheProjectFileByteIdentical()
    {
        var full = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "GateProbeOk.cs");
        var project = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Fixture.Broken.csproj");
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        try
        {
            var text = await CallAsync("write_text", new()
            {
                ["path"] = "src/Fixture.Broken/GateProbeOk.cs",
                ["content"] = "namespace Fixture.Broken;\n\npublic static class GateProbeOk\n{\n    public static int Probe() => 7;\n}\n",
                ["force"] = true,
            });

            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
            Assert.Contains("changedLines=", text, StringComparison.Ordinal);
            Assert.True(File.Exists(full));
            Assert.Equal(before, await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(full);
            await File.WriteAllBytesAsync(project, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WriteText_CreatingAFileNoProjectCompiles_StaysUngated()
    {
        var full = Path.Combine(BrokenRoot, "Scratch.cs");

        try
        {
            var text = await CallAsync("write_text", new()
            {
                ["path"] = "Scratch.cs",
                ["content"] = "public static class Scratch\n{\n    public static int Probe() => undefinedSymbol;\n}\n",
                ["force"] = true,
            });

            Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
            Assert.True(File.Exists(full));
        }
        finally
        {
            File.Delete(full);
        }
    }

    [Fact]
    public async Task WriteText_WithSeveralNewFilesWhereOneDoesNotCompile_WritesNoneOfThem()
    {
        var sound = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "BatchSound.cs");
        var broken = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "BatchBroken.cs");

        try
        {
            var text = await CallAsync("write_text", new()
            {
                ["force"] = true,
                ["files"] = new object[]
                {
                new Dictionary<string, object?>
                {
                    ["path"] = "src/Fixture.Broken/BatchSound.cs",
                    ["content"] = "namespace Fixture.Broken;\n\npublic static class BatchSound\n{\n    public static int Probe() => 1;\n}\n",
                },
                new Dictionary<string, object?>
                {
                    ["path"] = "src/Fixture.Broken/BatchBroken.cs",
                    ["content"] = "namespace Fixture.Broken;\n\npublic static class BatchBroken\n{\n    public static int Probe() => undefinedSymbol;\n}\n",
                },
                },
            });

            Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
            Assert.False(File.Exists(sound));
            Assert.False(File.Exists(broken));
        }
        finally
        {
            File.Delete(sound);
            File.Delete(broken);
        }
    }

    [Fact]
    public async Task CleanupVerify_ForStyleAndAnalyzers_IgnoresWhitespaceTheCiCommandsDoNotCheck()
    {
        var full = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "WhitespaceSample.cs");
        var project = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Fixture.Broken.csproj");
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        try
        {
            await CallAsync("write_text", new()
            {
                ["path"] = "src/Fixture.Broken/WhitespaceSample.cs",
                ["content"] = "namespace Fixture.Broken;\n\npublic static class WhitespaceSample\n{\n    public static int Sign(int value) => (value, 0) switch\n    {\n        (< 0, _) => -1,\n        (> 0, _) => 1,\n        _ => 0,\n    };\n}\n",
                ["force"] = true,
            });

            var style = await CallAsync("cleanup", new() { ["path"] = "src/Fixture.Broken/WhitespaceSample.cs", ["fix"] = "style", ["verify"] = true });
            var analyzers = await CallAsync("cleanup", new() { ["path"] = "src/Fixture.Broken/WhitespaceSample.cs", ["fix"] = "analyzers", ["verify"] = true });
            var whitespace = await CallAsync("format", new() { ["path"] = "src/Fixture.Broken/WhitespaceSample.cs", ["verify"] = true });

            Assert.DoesNotContain("would change", style, StringComparison.Ordinal);
            Assert.DoesNotContain("would change", analyzers, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", style, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", analyzers, StringComparison.Ordinal);
            Assert.Contains("VERIFY_FAILED", whitespace, StringComparison.Ordinal);
            Assert.Contains("WhitespaceSample.cs", whitespace, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(full);
            await File.WriteAllBytesAsync(project, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WriteText_CreatingAGatedNewFile_DoesNotLetUndoClaimItRevertedTheCreation()
    {
        var created = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "UndoProbe.cs");
        var project = Path.Combine(BrokenRoot, "src", "Fixture.Broken", "Fixture.Broken.csproj");
        var before = await File.ReadAllBytesAsync(project, TestContext.Current.CancellationToken);

        try
        {
            await CallAsync("write_text", new()
            {
                ["path"] = "src/Fixture.Broken/UndoProbe.cs",
                ["content"] = "namespace Fixture.Broken;\n\npublic static class UndoProbe\n{\n    public static int Probe() => 4;\n}\n",
                ["force"] = true,
            });

            var undone = await CallAsync("undo_last_change", []);

            Assert.DoesNotContain("reverted the last change", undone, StringComparison.Ordinal);
            Assert.True(File.Exists(created), "the file is still on disk, so nothing may claim the creation was reverted");
        }
        finally
        {
            File.Delete(created);
            await File.WriteAllBytesAsync(project, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task GetDiagnostics_NamesTheDeclarationTheErrorSitsIn()
    {
        var text = await CallAsync("get_diagnostics", new() { ["minSeverity"] = "error" });

        Assert.Matches(@"Calculator\.cs:\d+:\d+ Calculator\.PreExistingError:", text);
        Assert.DoesNotContain("Calculator.Healthy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddTo_WhenTwoContainersShareALeafName_RefusesInsteadOfPickingTheFirst()
    {
        var ambiguous = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => Helper();", "public int Value() => 2;" },
            ["add"] = new[] { "private static int Helper() => 1;" },
            ["addTo"] = "Duplicate",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", ambiguous, StringComparison.Ordinal);
        Assert.Contains("addTo=Duplicate names 2 different containing types", ambiguous, StringComparison.Ordinal);
        Assert.Contains("Fixture.Broken.Alpha.Duplicate", ambiguous, StringComparison.Ordinal);
        Assert.Contains("Fixture.Broken.Beta.Duplicate", ambiguous, StringComparison.Ordinal);

        var qualified = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => Helper();", "public int Value() => 2;" },
            ["add"] = new[] { "private static int Helper() => 1;" },
            ["addTo"] = "Fixture.Broken.Alpha.Duplicate",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", qualified, StringComparison.Ordinal);
        Assert.Contains("AlphaDuplicate.cs", qualified, StringComparison.Ordinal);
        Assert.Contains("Helper", qualified, StringComparison.Ordinal);
    }
}
