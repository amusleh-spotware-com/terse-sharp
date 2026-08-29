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

    [Fact]
    public async Task AddTo_WithOneContainerPerAddEntry_LandsEachHelperInItsOwnType()
    {
        var applied = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => AlphaHelper();", "public int Value() => BetaHelper();" },
            ["add"] = new[] { "private static int AlphaHelper() => 1;", "private static int BetaHelper() => 2;" },
            ["addTo"] = "Fixture.Broken.Alpha.Duplicate,Fixture.Broken.Beta.Duplicate",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("would be rolled back", applied, StringComparison.Ordinal);

        var alphaFile = applied.IndexOf("AlphaDuplicate.cs", StringComparison.Ordinal);
        var betaFile = applied.IndexOf("BetaDuplicate.cs", StringComparison.Ordinal);
        var alphaHelper = applied.IndexOf("AlphaHelper() => 1", StringComparison.Ordinal);
        var betaHelper = applied.IndexOf("BetaHelper() => 2", StringComparison.Ordinal);

        Assert.True(alphaFile >= 0 && betaFile >= 0 && alphaHelper >= 0 && betaHelper >= 0, applied);
        Assert.True(alphaFile < betaFile, applied);
        Assert.InRange(alphaHelper, alphaFile, betaFile);
        Assert.True(betaHelper > betaFile, applied);
    }

    [Fact]
    public async Task AddTo_WithADifferentNumberOfContainersThanAddEntries_RefusesNamingBothCounts()
    {
        var refused = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => 1;", "public int Value() => 2;" },
            ["add"] = new[] { "private static int One() => 1;", "private static int Two() => 2;", "private static int Three() => 3;" },
            ["addTo"] = "Fixture.Broken.Alpha.Duplicate,Fixture.Broken.Beta.Duplicate",
            ["dryRun"] = true,
        });

        var blank = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => 1;", "public int Value() => 2;" },
            ["add"] = new[] { "private static int One() => 1;" },
            ["addTo"] = " , ",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", refused, StringComparison.Ordinal);
        Assert.Contains("addTo= names 2 containing types but add= has 3 entries", refused, StringComparison.Ordinal);

        Assert.Contains("ERROR InvalidArgument", blank, StringComparison.Ordinal);
        Assert.Contains("names no containing type", blank, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchRejectedForAnUnresolvableId_HoldsItsDeclarationsBehindARetryToken()
    {
        var rejected = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Calculator.Healthy", "Calculator.NoMemberSpelledLikeThis" },
            ["declarations"] = new[] { "public int Healthy() => 1;", "public int PreExistingError() => 2;" },
        });

        var retried = await CallAsync("replace_symbol", new()
        {
            ["retryWith"] = Token(rejected),
            ["symbolIds"] = new[] { "M:Fixture.Broken.Calculator.Healthy", "M:Fixture.Broken.Calculator.PreExistingError" },
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR SymbolNotFound", rejected, StringComparison.Ordinal);
        Assert.Contains("retryWith=", rejected, StringComparison.Ordinal);
        Assert.Contains("public int PreExistingError() => 2;", retried, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", retried, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResolutionFailureCarryingNoPayload_MintsNoRetryToken()
    {
        var text = await CallAsync("replace_symbol_body", new() { ["symbolId"] = "Calculator.NoMemberSpelledLikeThis" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("retryWith=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchWhoseDeclarationNameDoesNotMatchItsPairedSymbol_IsRefusedFromSyntax()
    {
        var refused = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Alpha.Duplicate.Value", "M:Fixture.Broken.Beta.Duplicate.Value" },
            ["declarations"] = new[] { "public int Value() => 1;", "public int PerEntryOnly() => 2;" },
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", refused, StringComparison.Ordinal);
        Assert.Contains("declarations[1]", refused, StringComparison.Ordinal);
        Assert.Contains("'PerEntryOnly'", refused, StringComparison.Ordinal);
        Assert.Contains("'Value'", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("CompileRegression", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_WhenAnAddedUsingMakesANameAmbiguous_NamesTheUsingsEntryThatCausedIt()
    {
        const string first = "src/Fixture.Broken/AmbiguityFirst.cs";
        const string second = "src/Fixture.Broken/AmbiguitySecond.cs";
        const string probe = "src/Fixture.Broken/AmbiguityProbe.cs";

        var written = await CallAsync("write_text", new()
        {
            ["force"] = true,
            ["files"] = new object[]
            {
                new Dictionary<string, object> { ["path"] = first, ["content"] = "namespace Fixture.Broken.First;\n\npublic sealed class ProbeOrder\n{\n}\n" },
                new Dictionary<string, object> { ["path"] = second, ["content"] = "namespace Fixture.Broken.Second;\n\npublic sealed class ProbeOrder\n{\n}\n" },
                new Dictionary<string, object> { ["path"] = probe, ["content"] = "using Fixture.Broken.First;\n\nnamespace Fixture.Broken.Consumer;\n\npublic sealed class AmbiguityProbe\n{\n    public int Count() => 1;\n}\n" },
            },
        });

        try
        {
            Assert.DoesNotContain("ERROR", written, StringComparison.Ordinal);

            var rejected = await CallAsync("replace_symbol_body", new()
            {
                ["symbolId"] = "AmbiguityProbe.Count",
                ["body"] = "ProbeOrder local = null!;\n\nreturn local is null ? 1 : 2;",
                ["usings"] = new[] { "Fixture.Broken.Second" },
            });

            Assert.Contains("CS0104", rejected, StringComparison.Ordinal);
            Assert.Contains("the ambiguity was introduced by usings=[\"Fixture.Broken.Second\"]", rejected, StringComparison.Ordinal);
            Assert.Contains("retry with usings=[] and the retryWith token below to drop it", rejected, StringComparison.Ordinal);
        }
        finally
        {
            await CallAsync("write_text", new() { ["path"] = probe, ["delete"] = true, ["force"] = true });
            await CallAsync("write_text", new() { ["path"] = second, ["delete"] = true, ["force"] = true });
            await CallAsync("write_text", new() { ["path"] = first, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task ReplaceSymbolBody_RetriedWithAnEmptyUsings_DropsTheImportTheTokenHolds()
    {
        const string first = "src/Fixture.Broken/DroppedFirst.cs";
        const string second = "src/Fixture.Broken/DroppedSecond.cs";
        const string probe = "src/Fixture.Broken/DroppedProbe.cs";

        var written = await CallAsync("write_text", new()
        {
            ["force"] = true,
            ["files"] = new object[]
            {
                new Dictionary<string, object> { ["path"] = first, ["content"] = "namespace Fixture.Broken.DroppedFirst;\n\npublic sealed class DroppedOrder\n{\n}\n" },
                new Dictionary<string, object> { ["path"] = second, ["content"] = "namespace Fixture.Broken.DroppedSecond;\n\npublic sealed class DroppedOrder\n{\n}\n" },
                new Dictionary<string, object> { ["path"] = probe, ["content"] = "using Fixture.Broken.DroppedFirst;\n\nnamespace Fixture.Broken.DroppedConsumer;\n\npublic sealed class DroppedProbe\n{\n    public int Count() => 1;\n}\n" },
            },
        });

        try
        {
            Assert.DoesNotContain("ERROR", written, StringComparison.Ordinal);

            var rejected = await CallAsync("replace_symbol_body", new()
            {
                ["symbolId"] = "DroppedProbe.Count",
                ["body"] = "DroppedOrder local = null!;\n\nreturn local is null ? 1 : 2;",
                ["usings"] = new[] { "Fixture.Broken.DroppedSecond" },
            });

            var retried = await CallAsync("replace_symbol_body", new()
            {
                ["retryWith"] = Token(rejected),
                ["usings"] = Array.Empty<string>(),
            });

            Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", retried, StringComparison.Ordinal);
            Assert.Contains("changedLines=", retried, StringComparison.Ordinal);
        }
        finally
        {
            await CallAsync("write_text", new() { ["path"] = probe, ["delete"] = true, ["force"] = true });
            await CallAsync("write_text", new() { ["path"] = second, ["delete"] = true, ["force"] = true });
            await CallAsync("write_text", new() { ["path"] = first, ["delete"] = true, ["force"] = true });
        }
    }

    [Fact]
    public async Task WriteText_RolledBack_OffersNoParameterItDoesNotDeclare()
    {
        var rejected = await CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Broken/CultureProbe.cs",
            ["force"] = true,
            ["content"] = "namespace Fixture.Broken;\n\npublic sealed class CultureProbe\n{\n    public string Text => 7.ToString(CultureInfo.InvariantCulture);\n}\n",
        });

        Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
        Assert.Contains("write_text declares no usings= and no retryWith=", rejected, StringComparison.Ordinal);
        Assert.DoesNotContain("retry with usings=[", rejected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedEdit_EndsWithTheRetryTokenAloneOnItsLine()
    {
        var rejected = await CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "OrderService.Unused",
            ["body"] = "return MissingHelperThatDoesNotExistHere();",
        });

        var lines = rejected.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var last = lines[^1].TrimEnd('\r');

        Assert.StartsWith("retryWith=", last, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', last);
    }

    [Fact]
    public async Task WriteText_RolledBack_OffersNoSymbolBatchItCannotSend()
    {
        var rejected = await CallAsync("write_text", new()
        {
            ["path"] = "src/Fixture.Broken/MismatchProbe.cs",
            ["force"] = true,
            ["content"] = "namespace Fixture.Broken;\n\npublic sealed class MismatchProbe\n{\n    public int Value => \"not an int\";\n}\n",
        });

        Assert.Contains("ERROR CompileRegression", rejected, StringComparison.Ordinal);
        Assert.Contains("fix the edit in the content you send", rejected, StringComparison.Ordinal);
        Assert.DoesNotContain("replace_symbol symbolIds/declarations batch", rejected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetryCarryingFix_ReplacesOnlyTheHeldDeclarationItNamesAndReplaysTheRest()
    {
        var rejected = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "M:Fixture.Broken.Calculator.Healthy", "Calculator.NoMemberSpelledLikeThis" },
            ["declarations"] = new[] { "public int Healthy() => 11;", "public int PreExistingError() => 2;" },
        });

        var token = Token(rejected);
        var corrected = new[] { "M:Fixture.Broken.Calculator.Healthy", "M:Fixture.Broken.Calculator.PreExistingError" };

        var retried = await CallAsync("replace_symbol", new()
        {
            ["retryWith"] = token,
            ["symbolIds"] = corrected,
            ["fix"] = new[] { "1=public int PreExistingError() => 4242;" },
            ["dryRun"] = true,
        });

        var outOfRange = await CallAsync("replace_symbol", new()
        {
            ["retryWith"] = token,
            ["symbolIds"] = corrected,
            ["fix"] = new[] { "7=public int PreExistingError() => 4242;" },
            ["dryRun"] = true,
        });

        var malformed = await CallAsync("replace_symbol", new()
        {
            ["retryWith"] = token,
            ["symbolIds"] = corrected,
            ["fix"] = new[] { "public int PreExistingError() => 4242;" },
            ["dryRun"] = true,
        });

        var detached = await CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Broken.Calculator.Healthy",
            ["declaration"] = "public int Healthy() => 11;",
            ["fix"] = new[] { "0=public int Healthy() => 12;" },
            ["dryRun"] = true,
        });

        Assert.Contains("fix=[\"<index>=<corrected declaration>\"]", rejected, StringComparison.Ordinal);
        Assert.Contains("public int PreExistingError() => 4242;", retried, StringComparison.Ordinal);
        Assert.Contains("public int Healthy() => 11;", retried, StringComparison.Ordinal);
        Assert.DoesNotContain("public int PreExistingError() => 2;", retried, StringComparison.Ordinal);

        Assert.Contains("ERROR InvalidArgument", outOfRange, StringComparison.Ordinal);
        Assert.Contains("fix[0] names index 7, and the held batch carries 2 declaration(s)", outOfRange, StringComparison.Ordinal);
        Assert.Contains("name an index between 0 and 1", outOfRange, StringComparison.Ordinal);

        Assert.Contains("fix[0] is not '<index>=<declaration>'", malformed, StringComparison.Ordinal);

        Assert.Contains("ERROR InvalidArgument", detached, StringComparison.Ordinal);
        Assert.Contains("fix was passed without retryWith", detached, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABatchMixingANestedTypeWithItsSiblings_AppendsToTheirDeclaringType()
    {
        var applied = await CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "T:Fixture.Broken.Outer.Nested", "M:Fixture.Broken.Outer.Sibling" },
            ["declarations"] = new[]
            {
                "public enum Nested\n    {\n        First,\n        Second,\n    }",
                "public int Sibling() => Helper();",
            },
            ["add"] = new[] { "private int Helper() => 2;" },
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("do not share one", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
        Assert.Contains("private int Helper() => 2;", applied, StringComparison.Ordinal);
        Assert.Contains("Second,", applied, StringComparison.Ordinal);
    }
}
