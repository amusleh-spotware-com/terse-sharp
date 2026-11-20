namespace TerseSharp.E2ETests;

[Collection(nameof(PolicySolutionCollection))]
public sealed class PolicyE2ETests : IAsyncLifetime
{
    private static readonly string PolicyRoot =
        Path.Combine(TerseServerFixture.RepositoryRoot, "fixtures", "PolicySolution");

    private static readonly string LedgerPath =
        Path.Combine(PolicyRoot, "src", "Fixture.Policy", "Ledger.cs");

    private TerseServerProcess server = null!;

    public async ValueTask InitializeAsync() =>
        server = await TerseServerProcess.StartAsync(
            PolicyRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--tools", "all", "--workspace", Path.Combine(PolicyRoot, "PolicySolution.slnx")],
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => server.StopAsync();

    [Fact]
    public async Task AddMember_WithADeclarationThatViolatesTheProjectPolicy_IsRolledBackAndNamesTheRuleAndTheFix()
    {
        var response = await AddAsync("public int Go() => 1;", allowPolicy: false);

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE105", response, StringComparison.Ordinal);
        Assert.Contains("Ledger.Go", response, StringComparison.Ordinal);
        Assert.Contains("'Go' is 2 characters", response, StringComparison.Ordinal);
        Assert.Contains("fix: ", response, StringComparison.Ordinal);
        Assert.Contains("allowPolicy=true", response, StringComparison.Ordinal);
        Assert.Contains("retryWith=", response, StringComparison.Ordinal);
        Assert.Equal(await OriginalAsync(), await CurrentAsync());
    }

    [Fact]
    public async Task AddMember_WithAllowPolicy_LandsTheEditAndNamesEveryRuleItBypassed()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync("public int Go() => 1;", allowPolicy: true);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", response, StringComparison.Ordinal);
            Assert.Contains("TERSE105", response, StringComparison.Ordinal);
            Assert.Contains("Go", await CurrentAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task AddMember_WithARuleTheProjectSetToWarn_LandsTheEditAndReportsTheWarning()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync(Long(), allowPolicy: false);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy  TERSE101", response, StringComparison.Ordinal);
            Assert.Contains("11 statements", response, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task AddMember_WithAConformingDeclaration_AppliesWithNoPolicyLineAtAll()
    {
        var before = await OriginalAsync();

        try
        {
            var response = await AddAsync("public int Balance() => 0;", allowPolicy: false);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.DoesNotContain("WARNING policy", response, StringComparison.Ordinal);
            Assert.DoesNotContain("PolicyViolation", response, StringComparison.Ordinal);
            Assert.DoesNotContain("TERSE1", response, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task AddMember_WithAMethodThatDropsTheFileBelowTheCodeHealthThreshold_IsRolledBackNamingTerse114()
    {
        var branches = string.Join('\n', Enumerable.Range(1, 9).Select(index => string.Create(CultureInfo.InvariantCulture, $"if (amount == {index}) total += {index};")));
        var statements = string.Join('\n', Enumerable.Range(0, 70).Select(index => string.Create(CultureInfo.InvariantCulture, $"total += amount * {index};")));

        var response = await AddAsync("public int Classify(int amount)\n{\nvar total = 0;\n" + branches + "\n" + statements + "\nreturn total;\n}", allowPolicy: false);

        Assert.Contains("ERROR PolicyViolation", response, StringComparison.Ordinal);
        Assert.Contains("TERSE114", response, StringComparison.Ordinal);
        Assert.Contains("code health 7.5 (Brain Method Ledger.Classify", response, StringComparison.Ordinal);
        Assert.Equal(await OriginalAsync(), await CurrentAsync());
    }

    private static string Long() =>
        "public int Long()\n{\n" + string.Concat(Enumerable.Range(0, 10).Select(index => "    var a" + index.ToString(CultureInfo.InvariantCulture) + " = " + index.ToString(CultureInfo.InvariantCulture) + ";\n")) + "    return 0;\n}";

    private Task<string> AddAsync(string declaration, bool allowPolicy) =>
        server.CallAsync(
            "add_member",
            new()
            {
                ["typeSymbolId"] = "Ledger",
                ["declaration"] = declaration,
                ["allowPolicy"] = allowPolicy,
            },
            TestContext.Current.CancellationToken);

    private static Task<string> CurrentAsync() =>
        File.ReadAllTextAsync(LedgerPath, TestContext.Current.CancellationToken);

    private static Task<string> OriginalAsync() => CurrentAsync();

    private static async Task RestoreAsync(string content)
    {
        var written = File.GetLastWriteTimeUtc(LedgerPath);

        await File.WriteAllTextAsync(LedgerPath, content, TestContext.Current.CancellationToken);

        File.SetLastWriteTimeUtc(LedgerPath, written);
    }

    [Fact]
    public async Task AddMember_ReplayingAPolicyRejectionWithItsToken_LandsTheHeldDeclarationWithoutResendingIt()
    {
        var before = await OriginalAsync();

        try
        {
            var rejected = await AddAsync("public int Go() => 1;", allowPolicy: false);
            var token = Token(rejected);

            var response = await server.CallAsync(
                "add_member",
                new() { ["retryWith"] = token, ["allowPolicy"] = true },
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", response, StringComparison.Ordinal);
            Assert.Contains("Go", await CurrentAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    private static string Token(string rejected)
    {
        var marker = rejected.IndexOf("retryWith=", StringComparison.Ordinal);

        Assert.True(marker >= 0, "the rejection carried no retryWith token");

        var start = marker + "retryWith=".Length;
        var length = rejected.AsSpan(start).IndexOfAny(' ', '\r', '\n');

        return length < 0 ? rejected[start..] : rejected.Substring(start, length);
    }

    [Fact]
    public async Task WriteText_WithContentTheProjectPolicyRejects_IsRolledBackUnlessAllowPolicyIsPassed()
    {
        const string probe = "src/Fixture.Policy/PolicyWriteProbe.cs";
        const string content = "namespace Fixture.Policy;\n\npublic sealed class PolicyWriteProbe\n{\n    public int Go() => 1;\n}\n";

        try
        {
            var rejected = await WrittenAsync(probe, content, allowPolicy: false);
            var applied = await WrittenAsync(probe, content, allowPolicy: true);

            Assert.Contains("ERROR", rejected, StringComparison.Ordinal);
            Assert.Contains("TERSE105", rejected, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains("WARNING policy overridden", applied, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync(
                "write_text",
                new() { ["path"] = probe, ["delete"] = true, ["force"] = true },
                TestContext.Current.CancellationToken);
        }
    }

    private Task<string> WrittenAsync(string path, string content, bool allowPolicy) =>
        server.CallAsync(
            "write_text",
            new()
            {
                ["path"] = path,
                ["content"] = content,
                ["force"] = true,
                ["allowPolicy"] = allowPolicy,
            },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task WriteText_WithAMethodScenarioOutcomeTestName_PassesNamingOnlyWhileItCarriesATestAttribute()
    {
        const string probe = "src/Fixture.Policy/PolicyTestNameProbe.cs";
        const string untested = "namespace Fixture.Policy;\n\npublic sealed class FactAttribute : System.Attribute\n{\n}\n\npublic sealed class LedgerProof\n{\n    public void Post_WithNoEntries_Balances() { }\n}\n";
        const string tested = "namespace Fixture.Policy;\n\npublic sealed class FactAttribute : System.Attribute\n{\n}\n\npublic sealed class LedgerProof\n{\n    [Fact]\n    public void Post_WithNoEntries_Balances() { }\n}\n";

        try
        {
            var rejected = await WrittenAsync(probe, untested, allowPolicy: false);
            var applied = await WrittenAsync(probe, tested, allowPolicy: false);

            Assert.Contains("TERSE107", rejected, StringComparison.Ordinal);
            Assert.Contains("method name 'Post_WithNoEntries_Balances'", rejected, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("TERSE107", applied, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync(
                "write_text",
                new() { ["path"] = probe, ["delete"] = true, ["force"] = true },
                TestContext.Current.CancellationToken);
        }
    }

    private const string LooseProbe = "loose/LooseProbe.cs";
    private const string ConformingProbe = "namespace Fixture.Policy;\n\npublic sealed class LooseProbe\n{\n    public int Count()\n    {\n        return 1;\n    }\n}\n";
    private const string CommentedProbe = "namespace Fixture.Policy;\n\npublic sealed class LooseProbe\n{\n    public int Count()\n    {\n        // the running count\n        return 1;\n    }\n}\n";
    private static readonly string LooseDirectory = Path.Combine(PolicyRoot, "loose");
    private static readonly string LoosePath = Path.Combine(LooseDirectory, "LooseProbe.cs");

    [Fact]
    public async Task WriteText_WithForceOnACsFileNoProjectCompiles_RefusesContentThePolicyRejectsUnlessAllowPolicyIsPassed()
    {
        try
        {
            var rejected = await WrittenAsync(LooseProbe, CommentedProbe, allowPolicy: false);
            var writtenOnRejection = File.Exists(LoosePath);
            var applied = await WrittenAsync(LooseProbe, CommentedProbe, allowPolicy: true);

            Assert.Contains("ERROR PolicyViolation", rejected, StringComparison.Ordinal);
            Assert.Contains("TERSE112", rejected, StringComparison.Ordinal);
            Assert.False(writtenOnRejection);
            Assert.Contains("WARNING policy overridden  TERSE112", applied, StringComparison.Ordinal);
            Assert.Contains("// the running count", await File.ReadAllTextAsync(LoosePath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            DeleteLooseDirectory();
        }
    }

    [Fact]
    public async Task EditText_WithForceOnACsFileNoProjectCompiles_RefusesAnEditThatAddsARejectedComment()
    {
        try
        {
            Assert.DoesNotContain("ERROR", await WrittenAsync(LooseProbe, ConformingProbe, allowPolicy: false), StringComparison.Ordinal);
            var before = await File.ReadAllTextAsync(LoosePath, TestContext.Current.CancellationToken);

            var preview = await CommentedEditAsync(LooseProbe, new() { ["force"] = true, ["dryRun"] = true });
            var rejected = await CommentedEditAsync(LooseProbe, new() { ["force"] = true });

            Assert.Contains("WARNING would be rolled back by policy  TERSE112", preview, StringComparison.Ordinal);
            Assert.Contains("ERROR PolicyViolation", rejected, StringComparison.Ordinal);
            Assert.Contains("TERSE112", rejected, StringComparison.Ordinal);
            Assert.Equal(before, await File.ReadAllTextAsync(LoosePath, TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteLooseDirectory();
        }
    }

    [Fact]
    public async Task EditText_WithAllowErrorsOnACompiledFile_StillRefusesAnEditThePolicyRejects()
    {
        var before = await OriginalAsync();

        try
        {
            var rejected = await CommentedLedgerEditAsync(new() { ["allowErrors"] = true });
            var batched = await server.CallAsync(
                "edit_text",
                new() { ["allowErrors"] = true, ["edits"] = new[] { new Dictionary<string, object?> { ["path"] = "src/Fixture.Policy/Ledger.cs", ["oldText"] = "        var total = 0;", ["newText"] = "        // running sum\n        var total = 0;" } } },
                TestContext.Current.CancellationToken);

            Assert.Contains("ERROR PolicyViolation", rejected, StringComparison.Ordinal);
            Assert.Contains("TERSE112", rejected, StringComparison.Ordinal);
            Assert.Contains("ERROR PolicyViolation", batched, StringComparison.Ordinal);
            Assert.Equal(before, await CurrentAsync());
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task EditText_OnACompiledFile_RefusesAPolicyViolationUnlessAllowPolicyIsPassed()
    {
        var before = await OriginalAsync();

        try
        {
            var rejected = await CommentedLedgerEditAsync([]);
            var unchanged = await CurrentAsync();
            var applied = await CommentedLedgerEditAsync(new() { ["allowPolicy"] = true });

            Assert.Contains("ERROR PolicyViolation", rejected, StringComparison.Ordinal);
            Assert.Equal(before, unchanged);
            Assert.Contains("WARNING policy overridden  TERSE112", applied, StringComparison.Ordinal);
            Assert.Contains("// running sum", await CurrentAsync(), StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    private Task<string> CommentedEditAsync(string path, Dictionary<string, object?> flags) =>
        server.CallAsync(
            "edit_text",
            new(flags) { ["path"] = path, ["oldText"] = "        return 1;", ["newText"] = "        // the running count\n        return 1;" },
            TestContext.Current.CancellationToken);

    private Task<string> CommentedLedgerEditAsync(Dictionary<string, object?> flags) =>
        server.CallAsync(
            "edit_text",
            new(flags) { ["path"] = "src/Fixture.Policy/Ledger.cs", ["oldText"] = "        var total = 0;", ["newText"] = "        // running sum\n        var total = 0;" },
            TestContext.Current.CancellationToken);

    private static void DeleteLooseDirectory()
    {
        if (Directory.Exists(LooseDirectory))
            Directory.Delete(LooseDirectory, recursive: true);
    }

    [Fact]
    public async Task Analyze_WithAnIdTheProjectPolicyEnforces_DoesNotCallItNotEnabled()
    {
        var text = await server.CallAsync(
            "analyze",
            new() { ["path"] = "src/Fixture.Policy/Ledger.cs", ["ids"] = "TERSE107,CA9999" },
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("NOT_ENABLED TERSE107", text, StringComparison.Ordinal);
        Assert.Contains("NOT_ENABLED CA9999", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_OverwritingAFileWithAWarnedEdit_MarksTheFileLineNotTheWarning()
    {
        var before = await OriginalAsync();

        try
        {
            var content = before[..before.LastIndexOf('}')] + "    " + Long() + "\n}\n";
            var response = await server.CallAsync("write_text", new() { ["path"] = LedgerPath, ["content"] = content, ["force"] = true }, TestContext.Current.CancellationToken);
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            Assert.DoesNotContain("ERROR", response, StringComparison.Ordinal);
            Assert.Contains(lines, line => line.StartsWith("WARNING policy  TERSE101", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("overwrote existing  ", StringComparison.Ordinal) && line.Contains("Ledger.cs  changedLines=", StringComparison.Ordinal));
            Assert.DoesNotContain("overwrote existing  WARNING", response, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task Analyze_WithAWarningFloor_DropsTheInfoPolicyAndDeadCodeRecordsItListsAtInfo()
    {
        var before = await OriginalAsync();

        try
        {
            await AddAsync(Long(), allowPolicy: false);

            var info = await server.CallAsync("analyze", new() { ["path"] = "src/Fixture.Policy/Ledger.cs", ["minSeverity"] = "info" }, TestContext.Current.CancellationToken);
            var warning = await server.CallAsync("analyze", new() { ["path"] = "src/Fixture.Policy/Ledger.cs", ["minSeverity"] = "warning" }, TestContext.Current.CancellationToken);

            Assert.Contains("TERSE101 info Policy", info, StringComparison.Ordinal);
            Assert.DoesNotContain("TERSE101", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(" info ", warning, StringComparison.Ordinal);
        }
        finally
        {
            await RestoreAsync(before);
        }
    }

    [Fact]
    public async Task WriteAndEditBatches_OnACsFileNoProjectCompiles_RefuseAnEntryThatAddsARejectedComment()
    {
        try
        {
            var written = await server.CallAsync(
                "write_text",
                new() { ["files"] = new[] { new Dictionary<string, object?> { ["path"] = LooseProbe, ["content"] = CommentedProbe, ["force"] = true } } },
                TestContext.Current.CancellationToken);
            var writtenOnRejection = File.Exists(LoosePath);
            await WrittenAsync(LooseProbe, ConformingProbe, allowPolicy: false);
            var before = await File.ReadAllTextAsync(LoosePath, TestContext.Current.CancellationToken);
            var edited = await server.CallAsync(
                "edit_text",
                new() { ["edits"] = new[] { new Dictionary<string, object?> { ["path"] = LooseProbe, ["oldText"] = "        return 1;", ["newText"] = "        // the running count\n        return 1;", ["force"] = true } } },
                TestContext.Current.CancellationToken);

            Assert.Contains("ERROR PolicyViolation", written, StringComparison.Ordinal);
            Assert.False(writtenOnRejection);
            Assert.Contains("ERROR PolicyViolation", edited, StringComparison.Ordinal);
            Assert.Equal(before, await File.ReadAllTextAsync(LoosePath, TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteLooseDirectory();
        }
    }
}
