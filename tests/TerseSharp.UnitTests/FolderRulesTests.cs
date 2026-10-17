using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class FolderRulesTests : IDisposable
{
    private readonly DirectoryInfo sandbox = Directory.CreateTempSubdirectory("terse-folder-rules-");

    public FolderRulesTests()
    {
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Directory.CreateDirectory(Path.Combine(Repository, "UnitTests", "Foo"));
        Directory.CreateDirectory(Home);
        File.WriteAllText(Path.Combine(Repository, "CLAUDE.md"), "ROOT RULES");
        File.WriteAllText(Path.Combine(Repository, "UnitTests", "CLAUDE.md"), "UNIT RULES");
        File.WriteAllText(FooRules, "FOO RULES");
        File.WriteAllText(Path.Combine(Repository, "UnitTests", "Foo", "BarTests.cs"), "class BarTests;");
    }

    private string Repository => Path.Combine(sandbox.FullName, "repo");

    private string Home => Path.Combine(sandbox.FullName, "home");

    private string FooRules => Path.Combine(Repository, "UnitTests", "Foo", "CLAUDE.md");

    [Fact]
    public async Task PostToolUse_ForATerseReadBelowNestedRules_InjectsEachFolderOnceOutermostFirst()
    {
        var first = Context(await HookAsync(Read()));
        var second = await HookAsync(Read());

        Assert.InRange(first.IndexOf("UNIT RULES", StringComparison.Ordinal), 0, first.IndexOf("FOO RULES", StringComparison.Ordinal) - 1);
        Assert.DoesNotContain("ROOT RULES", first, StringComparison.Ordinal);
        Assert.Null(second);
    }

    [Fact]
    public async Task PostToolUse_ForAPathOnlyTheResponseNames_InjectsItsFolderRules()
    {
        var call = Call("PostToolUse", "mcp__terse-sharp__get_symbol_source", new() { ["symbolId"] = "BarTests" });
        call["tool_response"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "1 symbols\nUnitTests\\Foo\\BarTests.cs:1-1\nclass BarTests;" });

        Assert.Contains("FOO RULES", Context(await HookAsync(call)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("mcp__other-server__read_text")]
    public async Task PostToolUse_ForAToolThatIsNotTerse_InjectsNothing(string tool) =>
        Assert.Null(await HookAsync(Call("PostToolUse", tool, new() { ["path"] = "UnitTests/Foo/BarTests.cs" })));

    [Fact]
    public async Task PreToolUse_ForATerseWriteBelowUnseenRules_DeniesOnceWithTheRules()
    {
        var first = await HookAsync(Write());
        var second = await HookAsync(Write());

        Assert.Equal("deny", first!["permissionDecision"]!.GetValue<string>());
        Assert.Contains("FOO RULES", first["permissionDecisionReason"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(second);
    }

    [Fact]
    public async Task PreToolUse_ForATerseRead_NeverDenies() =>
        Assert.Null(await HookAsync(Call("PreToolUse", "mcp__terse-sharp__get_file_outline", new() { ["path"] = "UnitTests/Foo/BarTests.cs" })));

    [Fact]
    public async Task PreToolUse_ForAWriteBelowRulesAReadAlreadyShowed_AllowsIt()
    {
        await HookAsync(Read());

        Assert.Null(await HookAsync(Write()));
    }

    [Fact]
    public async Task SessionStart_AfterACompaction_ForgetsWhatTheSessionWasShown()
    {
        await HookAsync(Read());
        Assert.Null(await HookAsync(Read()));
        await HookAsync(new JsonObject { ["hook_event_name"] = "SessionStart", ["source"] = "compact" });

        Assert.Contains("FOO RULES", Context(await HookAsync(Read())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForASessionStartedBelowTheRepositoryRoot_SkipsTheRulesClaudeCodeAlreadyLoaded()
    {
        var read = Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "Foo/BarTests.cs" });
        read["cwd"] = Path.Combine(Repository, "UnitTests");

        var context = Context(await HookAsync(read));

        Assert.Contains("FOO RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIT RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForANamedSiblingWorktree_InjectsItsRulesRatherThanTheSessionDirectorys()
    {
        var worktree = Path.Combine(sandbox.FullName, "repo-wt");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        Directory.CreateDirectory(Path.Combine(worktree, "UnitTests", "Foo"));
        await File.WriteAllTextAsync(Path.Combine(worktree, "UnitTests", "CLAUDE.md"), "WORKTREE RULES", TestContext.Current.CancellationToken);

        var context = Context(await HookAsync(Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "UnitTests/Foo/BarTests.cs", ["workspace"] = "repo-wt" })));

        Assert.Contains("WORKTREE RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIT RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForRulesOverTheHookBudget_NamesTheFileToReadInsteadOfInliningIt()
    {
        var oversized = new string('x', FolderRules.Budget + 1);
        await File.WriteAllTextAsync(FooRules, oversized, TestContext.Current.CancellationToken);

        var context = Context(await HookAsync(Read()));

        Assert.Contains("UNIT RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain(oversized, context, StringComparison.Ordinal);
        Assert.Contains("read_text path=\"" + FooRules + "\" verbose=true", context, StringComparison.Ordinal);
        Assert.InRange(context.Length, 1, FolderRules.ContextLimit);
    }

    [Fact]
    public async Task RunAsync_WhenTheLedgerCannotBeWritten_StillInjectsTheRules()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, ".terse"), "a file where the ledger directory belongs", TestContext.Current.CancellationToken);

        Assert.Contains("FOO RULES", Context(await HookAsync(Read())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreToolUse_WhenTheLedgerCannotBeWritten_FailsOpenInsteadOfDenyingEveryRetry()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, ".terse"), "a file where the ledger directory belongs", TestContext.Current.CancellationToken);

        Assert.Null(await HookAsync(Write()));
        Assert.Null(await HookAsync(Write()));
    }

    [Fact]
    public async Task PostToolUse_ForParallelCallsBelowTheSameRules_InjectsThemExactlyOnce()
    {
        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => HookAsync(Read())));

        Assert.Single(answers, answer => answer is not null);
    }

    [Fact]
    public void Claim_AfterAParallelCallWonTheOutermostRule_ClaimsNothingSoNoAnswerCarriesHalfTheRules()
    {
        var outer = Path.Combine(Repository, "UnitTests", "CLAUDE.md");
        var inner = Path.Combine(Repository, "UnitTests", "Foo", "CLAUDE.md");

        var won = new FolderRuleLedger(Home, "split").Claim([outer], showUnrecorded: true);
        var lost = new FolderRuleLedger(Home, "split").Claim([outer, inner], showUnrecorded: true);
        var later = new FolderRuleLedger(Home, "split").Claim([inner], showUnrecorded: true);

        Assert.Equal([outer], won);
        Assert.Empty(lost);
        Assert.Equal([inner], later);
    }

    [Fact]
    public async Task PreToolUse_ForParallelWritesBelowUnseenRules_DeniesEveryOneOfThem()
    {
        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => HookAsync(Write())));

        Assert.All(answers, answer => Assert.Equal("deny", answer?["permissionDecision"]?.GetValue<string>()));
        Assert.Null(await HookAsync(Write()));
    }

    [Fact]
    public async Task PostToolUse_ForASolutionInASubfolderOfTheSession_ResolvesPathsAgainstTheSolutionRoot()
    {
        Directory.CreateDirectory(Path.Combine(Repository, "src", "Tests", "Unit"));
        await File.WriteAllTextAsync(Path.Combine(Repository, "src", "App.slnx"), "<Solution />", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repository, "src", "Tests", "CLAUDE.md"), "SRC TEST RULES", TestContext.Current.CancellationToken);

        var context = Context(await HookAsync(Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "Tests/Unit/OrderTests.cs" })));

        Assert.Contains("SRC TEST RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForAnAgentWorktreeNamedAsTheWorkspace_InjectsThatWorktreesRules()
    {
        var worktree = Path.Combine(Repository, ".claude", "worktrees", "agent-x");
        Directory.CreateDirectory(Path.Combine(worktree, "UnitTests", "Foo"));
        await File.WriteAllTextAsync(Path.Combine(worktree, ".git"), "gitdir: elsewhere", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(worktree, "UnitTests", "CLAUDE.md"), "AGENT RULES", TestContext.Current.CancellationToken);

        var context = Context(await HookAsync(Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "UnitTests/Foo/BarTests.cs", ["workspace"] = "agent-x" })));

        Assert.Contains("AGENT RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIT RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForAWorkspaceNamedLikeTheSessionsOwnSolution_IgnoresTheSiblingCheckoutOfThatName()
    {
        var mainCheckout = Path.Combine(sandbox.FullName, "App");
        Directory.CreateDirectory(Path.Combine(mainCheckout, ".git"));
        Directory.CreateDirectory(Path.Combine(mainCheckout, "UnitTests", "Foo"));
        await File.WriteAllTextAsync(Path.Combine(mainCheckout, "UnitTests", "CLAUDE.md"), "MAIN CHECKOUT RULES", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repository, "App.slnx"), "<Solution />", TestContext.Current.CancellationToken);

        var context = Context(await HookAsync(Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "UnitTests/Foo/BarTests.cs", ["workspace"] = "App" })));

        Assert.Contains("UNIT RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain("MAIN CHECKOUT RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_WhenAStaleSessionCannotBePruned_StillRecordsWhatThisSessionWasShown()
    {
        var stale = Directory.CreateDirectory(Path.Combine(Home, ".terse", "folder-rules", "stale-session"));
        await using var held = new FileStream(Path.Combine(stale.FullName, "held"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stale.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-30);

        await HookAsync(Read());

        Assert.Null(await HookAsync(Read()));
    }

    [Fact]
    public async Task PostToolUse_ForAFileBesideTheSessionDirectory_SkipsTheRulesOfTheFoldersAboveTheSession()
    {
        Directory.CreateDirectory(Path.Combine(Repository, "UnitTests", "Other"));
        await File.WriteAllTextAsync(Path.Combine(Repository, "UnitTests", "Other", "CLAUDE.md"), "OTHER RULES", TestContext.Current.CancellationToken);
        var read = Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = Path.Combine(Repository, "UnitTests", "Other", "X.cs") });
        read["cwd"] = Path.Combine(Repository, "UnitTests", "Foo");

        var context = Context(await HookAsync(read));

        Assert.Contains("OTHER RULES", context, StringComparison.Ordinal);
        Assert.DoesNotContain("UNIT RULES", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForAnotherRepositoryNamedOnlyInTheResponse_InjectsNothing()
    {
        var foreign = Path.Combine(sandbox.FullName, "foreign");
        Directory.CreateDirectory(Path.Combine(foreign, ".git"));
        Directory.CreateDirectory(Path.Combine(foreign, "sub"));
        await File.WriteAllTextAsync(Path.Combine(foreign, "sub", "CLAUDE.md"), "FOREIGN RULES", TestContext.Current.CancellationToken);
        var call = Call("PostToolUse", "mcp__terse-sharp__search_text", new() { ["query"] = "x" });
        call["tool_response"] = "1 match\n" + Path.Combine(foreign, "sub", "x.cs") + ":1  x";

        Assert.Null(await HookAsync(call));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"hook_event_name":"PostToolUse","tool_name":"mcp__terse-sharp__read_text"}""")]
    public async Task RunAsync_ForAPayloadItCannotRead_WritesNothingAndExitsZero(string payload)
    {
        using var output = new StringWriter();

        Assert.Equal(0, await FolderRules.RunAsync(new StringReader(payload), output, Home, TestContext.Current.CancellationToken));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void Writers_AreTheEditToolsThatNameAPath_AndNoPathlessSymbolReadBuildOrFormatTool()
    {
        string[] writers = ["write_text", "edit_text", "add_member", "replace_symbol", "replace_symbol_body", "resx_set", "xaml_set_property", "razor_set_attribute"];
        string[] others = ["read_text", "get_file_outline", "build", "run_tests", "delete_symbol", "rename_symbol", "format", "cleanup", "gate"];

        Assert.All(writers, tool => Assert.Contains(tool, FolderRules.Writers));
        Assert.All(others, tool => Assert.DoesNotContain(tool, FolderRules.Writers));
    }

    public void Dispose() => sandbox.Delete(recursive: true);

    private static JsonObject Read() => Call("PostToolUse", "mcp__terse-sharp__read_text", new() { ["path"] = "UnitTests/Foo/BarTests.cs" });

    private static JsonObject Write() =>
        Call("PreToolUse", "mcp__terse-sharp__write_text", new() { ["path"] = "UnitTests/Foo/NewTests.cs", ["content"] = "class NewTests;", ["force"] = true });

    private static JsonObject Call(string hookEvent, string tool, JsonObject input) => new()
    {
        ["hook_event_name"] = hookEvent,
        ["tool_name"] = tool,
        ["tool_input"] = input,
    };

    private static string Context(JsonObject? output) => output!["additionalContext"]!.GetValue<string>();

    private async Task<JsonObject?> HookAsync(JsonObject payload)
    {
        payload["session_id"] ??= "s1";
        payload["cwd"] ??= Repository;
        using var output = new StringWriter();

        Assert.Equal(0, await FolderRules.RunAsync(new StringReader(payload.ToJsonString()), output, Home, TestContext.Current.CancellationToken));

        var text = output.ToString().Trim();

        return text.Length is 0 ? null : JsonNode.Parse(text)!["hookSpecificOutput"]!.AsObject();
    }

    [Fact]
    public async Task PreToolUse_ForADryRunWriteBelowUnseenRules_AllowsItAndTheRealWriteIsStillDenied()
    {
        var dryRun = Write();
        dryRun["tool_input"]!["dryRun"] = true;

        Assert.Null(await HookAsync(dryRun));
        Assert.Equal("deny", (await HookAsync(Write()))!["permissionDecision"]!.GetValue<string>());
    }

    [Fact]
    public async Task PostToolUse_WhenOneStaleSessionIsLocked_StillPrunesTheOtherStaleSessions()
    {
        var root = Path.Combine(Home, ".terse", "folder-rules");
        var locked = Directory.CreateDirectory(Path.Combine(root, "a-locked-session"));
        var unlocked = Directory.CreateDirectory(Path.Combine(root, "z-stale-session"));
        await using var held = new FileStream(Path.Combine(locked.FullName, "held"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        locked.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-30);
        unlocked.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-30);

        await HookAsync(Read());

        Assert.False(Directory.Exists(unlocked.FullName));
    }

    [Theory]
    [InlineData("UnitTests/Foo")]
    [InlineData("UnitTests/Foo/")]
    public async Task PostToolUse_ForADirectoryPath_InjectsThatDirectorysOwnRulesOnce(string path)
    {
        var context = Context(await HookAsync(Call("PostToolUse", "mcp__terse-sharp__analyze", new() { ["path"] = path })));

        Assert.Contains("UNIT RULES", context, StringComparison.Ordinal);
        Assert.Contains("FOO RULES", context, StringComparison.Ordinal);
        Assert.Equal(context.IndexOf("FOO RULES", StringComparison.Ordinal), context.LastIndexOf("FOO RULES", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WhenTheAnswerCannotBeWritten_ReleasesItsRulesSoTheNextCallCarriesThem()
    {
        var read = Read();
        read["session_id"] = "s1";
        read["cwd"] = Repository;
        using var broken = new BrokenWriter();

        Assert.Equal(0, await FolderRules.RunAsync(new StringReader(read.ToJsonString()), broken, Home, TestContext.Current.CancellationToken));
        Assert.Contains("FOO RULES", Context(await HookAsync(Read())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostToolUse_ForASubagentOfASessionThatSawTheRules_InjectsThemIntoTheSubagent()
    {
        await HookAsync(Read());
        var subagent = Read();
        subagent["agent_id"] = "agent-1";

        Assert.Contains("FOO RULES", Context(await HookAsync(subagent)), StringComparison.Ordinal);
        Assert.Null(await HookAsync(Read()));
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) => Task.FromException(new IOException("The pipe is being closed."));
    }
}
