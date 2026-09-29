namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class FieldReportE2ETests(TerseServerFixture server)
{
    private const string TwoProjectSolution =
        "<Solution>\n  <Project Path=\"src/Fixture.Trading/Fixture.Trading.csproj\" />\n  <Project Path=\"tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj\" />\n</Solution>\n";

    private const string PresenterSource = """
        namespace Fixture.Trading;

        public sealed class Presenter
        {
            public Presenter() : this(1, "a")
            {
            }

            internal Presenter(int code, string name)
            {
                Code = code;
                Name = name;
            }

            public int Code { get; }

            public string Name { get; }
        }
        """;

    private const string PresenterId = "M:Fixture.Trading.Presenter.#ctor(System.Int32,System.String)";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangedFiles_ForAFileUntrackedOnDiskThatTheBaseHeld_SaysSoInsteadOfCallingItDeleted()
    {
        using var repository = await ScratchRepository.CreateAsync();

        await Task.WhenAll(repository.WriteAsync("kept.md", "kept\n"), repository.WriteAsync("gone.md", "gone\n"));
        await repository.CommitAllAsync("base");
        await repository.GitAsync("rm", "--cached", "--quiet", "kept.md");
        await repository.GitAsync("rm", "--quiet", "gone.md");
        await repository.GitAsync("commit", "--no-verify", "--quiet", "-m", "untrack");

        var text = await server.CallAsync("changed_files", new() { ["root"] = repository.Root, ["baseRef"] = "HEAD~1", ["untracked"] = false });
        var lines = text.Split('\n');

        Assert.Single(lines, line => line.StartsWith("kept.md  ", StringComparison.Ordinal) && line.EndsWith("  D (untracked on disk)", StringComparison.Ordinal));
        Assert.Single(lines, line => line.StartsWith("gone.md  ", StringComparison.Ordinal) && line.EndsWith("  D", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangedFiles_WithIgnoreWhitespace_DropsAFileWhoseOnlyChangeIsWhitespace()
    {
        using var repository = await WhitespaceRepositoryAsync();

        var plain = await server.CallAsync("changed_files", new() { ["root"] = repository.Root });
        var ignoring = await server.CallAsync("changed_files", new() { ["root"] = repository.Root, ["ignoreWhitespace"] = true });

        Assert.Contains("spaced.md", plain, StringComparison.Ordinal);
        Assert.Contains("real.md", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("spaced.md", ignoring, StringComparison.Ordinal);
        Assert.Contains("real.md", ignoring, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffText_WithIgnoreWhitespaceAndUnified_AnswersTheHunksGitDiffWIAndU0Give()
    {
        using var repository = await WhitespaceRepositoryAsync();

        var whitespace = await server.CallAsync("diff_text", new() { ["root"] = repository.Root, ["path"] = "spaced.md", ["ignoreWhitespace"] = true });
        var context = await server.CallAsync("diff_text", new() { ["root"] = repository.Root, ["path"] = "real.md" });
        var bare = await server.CallAsync("diff_text", new() { ["root"] = repository.Root, ["path"] = "real.md", ["unified"] = 0 });

        Assert.StartsWith("0 lines", whitespace, StringComparison.Ordinal);
        Assert.Contains("@@ -1,5 +1,5 @@", context, StringComparison.Ordinal);
        Assert.Contains("@@ -3 +3 @@", bare, StringComparison.Ordinal);
        Assert.DoesNotContain(" 2", bare.Split('\n'), StringComparer.Ordinal);
    }

    [Fact]
    public async Task DiffText_WithAnUnifiedCountOutOfRange_IsRefusedByName()
    {
        var text = await server.CallAsync("diff_text", new() { ["unified"] = -2 });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("unified=-2", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_WithABranchOnlyTheRemoteTrackingRefHolds_SuggestsTheOriginSpelling()
    {
        using var repository = await ScratchRepository.CreateAsync();

        await repository.WriteAsync("a.md", "a\n");
        await repository.CommitAllAsync("base");
        await repository.GitAsync("update-ref", "refs/remotes/origin/dev", "HEAD");

        var text = await server.CallAsync("history", new() { ["root"] = repository.Root, ["baseRef"] = "dev..HEAD" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("'dev' is not a local branch, tag or commit; only the remote-tracking 'origin/dev' exists", text, StringComparison.Ordinal);
        Assert.Contains("remedy: pass origin/dev in place of dev", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task History_WithARefThatNamesNothing_NamesTheMissingRefInsteadOfBlamingTheRepository()
    {
        var text = await server.CallAsync("history", new() { ["baseRef"] = "terse-no-such-ref..HEAD" });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("'terse-no-such-ref' names no branch, tag or commit in this repository", text, StringComparison.Ordinal);
        Assert.DoesNotContain("check that this workspace is a git repository", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_AFilesBatchOutsideEveryRootWithForce_WritesEveryEntryLikeASingleWrite()
    {
        var directory = Directory.CreateTempSubdirectory("terse-e2e-batch-");

        try
        {
            var first = Path.Combine(directory.FullName, "first.txt");
            var second = Path.Combine(directory.FullName, "second.md");
            var refused = await server.CallAsync("write_text", new() { ["files"] = Entries(first, second) });
            var written = await server.CallAsync("write_text", new() { ["files"] = Entries(first, second), ["force"] = true });

            Assert.StartsWith("ERROR", refused, StringComparison.Ordinal);
            Assert.DoesNotContain("ERROR", written, StringComparison.Ordinal);
            Assert.Equal(["one\n", "two\n"], await ContentsAsync(first, second));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReadText_OfACSharpRangeWithBlankLines_SaysItIsCondensedAndAVerboseReadDoesNot()
    {
        var condensed = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-17" });
        var verbose = await server.CallAsync("read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-17", ["verbose"] = true });

        Assert.Equal("condensed=true - blank lines dropped, a number shown only after a gap; verbose=true numbers every line", condensed.Split('\n')[1]);
        Assert.DoesNotContain("condensed=true", verbose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteTextAndHistory_WithTwoWorkspacesLoadedAndNoHint_AnswerInsteadOfAmbiguousWorkspace()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token, CommittedAsync);
        var outside = Path.Combine(Path.GetTempPath(), "terse-e2e-unbound-" + Guid.NewGuid().ToString("N") + ".txt");

        await solution.CallAsync("load_workspace", new() { ["path"] = Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx") });

        try
        {
            await solution.CallAsync("write_text", new() { ["path"] = outside, ["content"] = "x\n", ["force"] = true });
            var history = await solution.CallAsync("history", new() { ["maxResults"] = 1 });

            Assert.Equal(["x\n"], await ContentsAsync(outside));
            Assert.Contains("fixture copy", history, StringComparison.Ordinal);
            Assert.Contains("(" + Path.GetFileName(solution.Root) + ") - 2 are loaded and this one holds the server's working directory", history, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task EditText_InAWorkspaceLoadedFromACsproj_EditsAFileOfTheProjectItReferences()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token);
        var tests = Path.Combine(solution.Root, "tests", "Fixture.Trading.Tests", "Fixture.Trading.Tests.csproj");

        await solution.CallAsync("load_workspace", new() { ["path"] = tests });

        var edited = await solution.CallAsync("edit_text", new()
        {
            ["workspace"] = tests,
            ["path"] = solution.OrderServicePath,
            ["oldText"] = "public int Unused() => 7;",
            ["newText"] = "public int Unused() => 8;",
            ["force"] = true,
        });

        Assert.DoesNotContain("OutOfWorkspace", edited, StringComparison.Ordinal);
        Assert.Contains("public int Unused() => 8;", await File.ReadAllTextAsync(solution.OrderServicePath, Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_InAWorkspaceLoadedFromACsproj_SeesAnotherProcessRewriteAReferencedProjectsFile()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: true, Token, root => PresenterWithCallerAsync(root, "new Presenter(5)"));
        var tests = Path.Combine(solution.Root, "tests", "Fixture.Trading.Tests", "Fixture.Trading.Tests.csproj");
        var caller = Path.Combine(solution.Root, "tests", "Fixture.Trading.Tests", "PresenterCaller.cs");

        await solution.CallAsync("load_workspace", new() { ["path"] = tests });

        var before = await AnalyzeAsync(solution, tests, caller);

        await RewriteAsync(
            Path.Combine(solution.ProjectDirectory, "Presenter.cs"),
            PresenterSource.Replace("public int Code { get; }", "internal Presenter(int code) : this(code, \"b\")\n    {\n    }\n\n    public int Code { get; }", StringComparison.Ordinal));

        var after = await PollAsync(() => AnalyzeAsync(solution, tests, caller), text => !text.Contains("CS7036", StringComparison.Ordinal));

        Assert.Contains("CS7036", before, StringComparison.Ordinal);
        Assert.DoesNotContain("CS7036", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_OfAnInternalConstructorCalledFromATestFileCreatedAfterTheLoad_CountsTheTestCaller()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: true, Token, root => PresenterWithCallerAsync(root, null));

        await solution.CallAsync("load_workspace", new() { ["reload"] = true });

        var before = await solution.CallAsync("find_usages", new() { ["symbol"] = PresenterId });

        await File.WriteAllTextAsync(
            Path.Combine(solution.Root, "tests", "Fixture.Trading.Tests", "PresenterFactory.cs"),
            "using Fixture.Trading;\n\nnamespace Fixture.Trading.Tests;\n\ninternal static class PresenterFactory\n{\n    public static Presenter Create() => new Presenter(2, \"b\");\n}\n",
            Token);

        var after = await PollAsync(() => solution.CallAsync("find_usages", new() { ["symbol"] = PresenterId }), text => text.Contains("PresenterFactory.cs", StringComparison.Ordinal));

        Assert.StartsWith("1 usages in 1 files", before, StringComparison.Ordinal);
        Assert.StartsWith("2 usages in 2 files", after, StringComparison.Ordinal);
        Assert.Contains("PresenterFactory.cs", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindUsages_InAWorkspaceLoadedFromACsproj_SaysCallersOutsideTheLoadAreNotCounted()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token, root => PresenterWithCallerAsync(root, "new Presenter(2, \"b\")"));

        await solution.CallAsync("load_workspace", new() { ["path"] = solution.ProjectPath });

        var scoped = await solution.CallAsync("find_usages", new() { ["symbol"] = PresenterId, ["workspace"] = solution.ProjectPath });
        var whole = await solution.CallAsync("find_usages", new() { ["symbol"] = PresenterId, ["workspace"] = solution.SolutionPath });

        Assert.StartsWith("1 usages in 1 files", scoped, StringComparison.Ordinal);
        Assert.Contains("scope=project load - only the 1 project(s) Fixture.Trading.csproj pulls in were searched", scoped, StringComparison.Ordinal);
        Assert.StartsWith("2 usages in 2 files", whole, StringComparison.Ordinal);
        Assert.DoesNotContain("scope=project load", whole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveTypeToFile_ForTheOnlyTypeOfAFile_MovesTheFileWholeAndLeavesNoStub()
    {
        const string Source = "namespace Fixture.Trading;\n\npublic sealed class Renamed\n{\n    public int First() => 1;\n\n    public int Second() => 2;\n}\n";
        await using var solution = await TerseTempSolution.StartAsync(
            watch: false,
            Token,
            root => File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Misnamed.cs"), Source, Token));
        var project = await File.ReadAllBytesAsync(solution.ProjectPath, Token);

        var moved = await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.Renamed" });

        Assert.Contains("deleted  src", moved, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(solution.ProjectDirectory, "Misnamed.cs")));
        Assert.Equal(Source, (await File.ReadAllTextAsync(Path.Combine(solution.ProjectDirectory, "Renamed.cs"), Token)).ReplaceLineEndings("\n"));
        Assert.Equal(project, await File.ReadAllBytesAsync(solution.ProjectPath, Token));
    }

    [Fact]
    public async Task UndoLastChange_AfterASoleTypeMove_SaysItCannotRestoreTheFileInsteadOfClaimingItDid()
    {
        await using var solution = await TerseTempSolution.StartAsync(
            watch: false,
            Token,
            root => File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Misnamed.cs"), "namespace Fixture.Trading;\n\npublic sealed class Renamed;\n", Token));

        await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.Renamed" });

        var undone = await solution.CallAsync("undo_last_change", []);

        Assert.Contains("nothing to undo - 1 snapshot(s) were dropped after move_type_to_file moved src", undone, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(solution.ProjectDirectory, "Misnamed.cs")));
        Assert.True(File.Exists(Path.Combine(solution.ProjectDirectory, "Renamed.cs")));
    }

    [Fact]
    public async Task ChangedFiles_OverARangeWhoseDeletedFileIsTrackedAgainNow_KeepsThePlainD()
    {
        using var repository = await ScratchRepository.CreateAsync();

        await repository.WriteAsync("a.md", "a\n");
        await repository.CommitAllAsync("add");
        await repository.GitAsync("rm", "--quiet", "a.md");
        await repository.GitAsync("commit", "--no-verify", "--quiet", "-m", "delete");
        await repository.WriteAsync("a.md", "a\n");
        await repository.CommitAllAsync("add again");

        var range = await server.CallAsync("changed_files", new() { ["root"] = repository.Root, ["baseRef"] = "HEAD~2..HEAD~1" });

        Assert.Single(range.Split('\n'), line => line.StartsWith("a.md  ", StringComparison.Ordinal) && line.EndsWith("  D", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangedFiles_ForACaseOnlyRename_KeepsThePlainDOfTheOldSpelling()
    {
        using var repository = await ScratchRepository.CreateAsync();

        await repository.WriteAsync("Case.md", "c\n");
        await repository.CommitAllAsync("case");
        await repository.GitAsync("mv", "Case.md", "case.md");

        var renamed = await server.CallAsync("changed_files", new() { ["root"] = repository.Root, ["untracked"] = false });

        Assert.Single(renamed.Split('\n'), line => line.StartsWith("Case.md  ", StringComparison.Ordinal) && line.EndsWith("  D", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Analyze_AfterATerseJsonIsRewrittenInPlace_RunsAgainInsteadOfReplayingUnchanged()
    {
        await using var solution = await TerseTempSolution.StartAsync(
            watch: true,
            Token,
            root => File.WriteAllTextAsync(Path.Combine(root, ".terse.json"), "{ \"build\": { \"configuration\": \"Debug\" } }\n", Token));
        var arguments = new Dictionary<string, object?> { ["path"] = "src/Fixture.Trading/OrderService.cs", ["includeDeadCode"] = false };

        await solution.CallAsync("analyze", new(arguments));
        await PollAsync(() => solution.CallAsync("analyze", new(arguments)), text => text.Contains("UNCHANGED", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(solution.Root, ".terse.json"), "{ \"build\": { \"configuration\": \"Release\" } }\n", Token);

        var after = await PollAsync(() => solution.CallAsync("analyze", new(arguments)), text => !text.Contains("UNCHANGED", StringComparison.Ordinal));

        Assert.DoesNotContain("UNCHANGED", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveTypeToFile_ForOneOfTwoTypes_KeepsTheBlankLinesBetweenItsMembers()
    {
        const string Source = "namespace Fixture.Trading;\n\npublic sealed class Staying\n{\n    public int Kept() => 0;\n}\n\npublic sealed class Leaving\n{\n    public int First() => 1;\n\n    public int Second() => 2;\n}\n";
        await using var solution = await TerseTempSolution.StartAsync(
            watch: false,
            Token,
            root => File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Staying.cs"), Source, Token));

        await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.Leaving" });

        var created = (await File.ReadAllTextAsync(Path.Combine(solution.ProjectDirectory, "Leaving.cs"), Token)).ReplaceLineEndings("\n");
        var remaining = (await File.ReadAllTextAsync(Path.Combine(solution.ProjectDirectory, "Staying.cs"), Token)).ReplaceLineEndings("\n");

        Assert.Contains("namespace Fixture.Trading;\n\npublic sealed class Leaving", created, StringComparison.Ordinal);
        Assert.Contains("public int First() => 1;\n\n    public int Second() => 2;", created, StringComparison.Ordinal);
        Assert.DoesNotContain("Staying", created, StringComparison.Ordinal);
        Assert.Contains("public sealed class Staying", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaving", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_AfterACheckoutMovesHeadWithTheWatcherOff_ReloadsAndFindsTheTypeTheNewCommitAdds()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token, BranchedAsync);

        var before = await solution.CallAsync("search_symbols", new() { ["query"] = "CheckedOutType" });

        await GitToolsE2ETests.RunGitAsync(solution.Root, "checkout", "--quiet", "other");

        var after = await solution.CallAsync("search_symbols", new() { ["query"] = "CheckedOutType" });

        Assert.StartsWith("0 symbols", before, StringComparison.Ordinal);
        Assert.Contains("T:Fixture.Trading.CheckedOutType", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_WhileCompilationsAreCold_SaysHowLongTheyAreKept()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, Token);

        var loaded = await solution.CallAsync("load_workspace", new() { ["reload"] = true });

        Assert.Contains("kept until the server serves no call for 15m, then released - --idle-minutes=0 or TERSE_IDLE_MINUTES=0 keeps them", loaded, StringComparison.Ordinal);
    }

    private static object[] Entries(string first, string second) =>
    [
        new Dictionary<string, object?> { ["path"] = first, ["content"] = "one\n" },
        new Dictionary<string, object?> { ["path"] = second, ["content"] = "two\n" },
    ];

    private static async Task<string[]> ContentsAsync(params string[] paths) =>
        [.. (await Task.WhenAll(paths.Select(path => File.ReadAllTextAsync(path, Token)))).Select(text => text.ReplaceLineEndings("\n"))];

    private static Task<string> AnalyzeAsync(TerseTempSolution solution, string workspace, string path) =>
        solution.CallAsync("analyze", new() { ["workspace"] = workspace, ["path"] = path, ["includeDeadCode"] = false, ["minSeverity"] = "error" });

    private static async Task<string> PollAsync(Func<Task<string>> call, Func<string, bool> settled)
    {
        var answer = await call();

        for (var attempt = 0; attempt < 50 && !settled(answer); attempt++)
        {
            await Task.Delay(200, Token);
            answer = await call();
        }

        return answer;
    }

    private static async Task RewriteAsync(string path, string content)
    {
        var staged = path + ".terse-e2e";

        await File.WriteAllTextAsync(staged, content, Token);
        File.Move(staged, path, overwrite: true);
    }

    private static async Task PresenterWithCallerAsync(string root, string? call)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "FixtureSolution.slnx"), TwoProjectSolution, Token);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Presenter.cs"), PresenterSource, Token);
        await File.WriteAllTextAsync(
            Path.Combine(root, "src", "Fixture.Trading", "Visibility.cs"),
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Fixture.Trading.Tests\")]\n",
            Token);

        if (call is not null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tests", "Fixture.Trading.Tests", "PresenterCaller.cs"),
                "using Fixture.Trading;\n\nnamespace Fixture.Trading.Tests;\n\ninternal static class PresenterCaller\n{\n    public static Presenter Create() => " + call + ";\n}\n",
                Token);
        }
    }

    private static async Task CommittedAsync(string root)
    {
        Directory.Delete(Path.Combine(root, ".git"));
        await ScratchRepository.InitializeAsync(root);
        await ScratchRepository.CommitAllAsync(root, "fixture copy");
    }

    private static async Task BranchedAsync(string root)
    {
        await CommittedAsync(root);
        await GitToolsE2ETests.RunGitAsync(root, "checkout", "--quiet", "-b", "other");
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "CheckedOutType.cs"), "namespace Fixture.Trading;\n\npublic sealed class CheckedOutType;\n", Token);
        await ScratchRepository.CommitAllAsync(root, "adds a type");
        await GitToolsE2ETests.RunGitAsync(root, "checkout", "--quiet", "-");
    }

    private static async Task<ScratchRepository> WhitespaceRepositoryAsync()
    {
        var repository = await ScratchRepository.CreateAsync();

        await repository.WriteAsync("spaced.md", "a b\nc\n");
        await repository.WriteAsync("real.md", "1\n2\n3\n4\n5\n");
        await repository.CommitAllAsync("base");
        await repository.WriteAsync("spaced.md", "a  b\nc   \n");
        await repository.WriteAsync("real.md", "1\n2\nthree\n4\n5\n");

        return repository;
    }

    private sealed class ScratchRepository : IDisposable
    {
        private ScratchRepository(string root) => Root = root;

        public string Root { get; }

        public static async Task<ScratchRepository> CreateAsync()
        {
            var repository = new ScratchRepository(Directory.CreateTempSubdirectory("terse-e2e-git-").FullName);

            await InitializeAsync(repository.Root);

            return repository;
        }

        public static Task InitializeAsync(string root) => GitToolsE2ETests.RunGitAsync(root, "init", "--quiet", "--initial-branch=main");

        public static async Task CommitAllAsync(string root, string message)
        {
            await GitToolsE2ETests.RunGitAsync(root, "-c", "core.autocrlf=false", "add", "-A");
            await GitToolsE2ETests.RunGitAsync(root, Identity("commit", "--no-verify", "--quiet", "-m", message));
        }

        public Task WriteAsync(string name, string content) => File.WriteAllTextAsync(Path.Combine(Root, name), content, Token);

        public Task CommitAllAsync(string message) => CommitAllAsync(Root, message);

        public Task GitAsync(params string[] arguments) => GitToolsE2ETests.RunGitAsync(Root, Identity(arguments));

        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(Root, recursive: true);
        }

        private static string[] Identity(params string[] arguments) =>
            ["-c", "user.email=terse@example.com", "-c", "user.name=terse", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. arguments];
    }

    [Fact]
    public async Task UndoLastChange_AfterAnEditThenASoleTypeMove_RevertsNeitherAndSaysWhy()
    {
        await using var solution = await TerseTempSolution.StartAsync(
            watch: false,
            Token,
            root => File.WriteAllTextAsync(Path.Combine(root, "src", "Fixture.Trading", "Misnamed.cs"), "namespace Fixture.Trading;\n\npublic sealed class Renamed;\n", Token));

        await solution.CallAsync("replace_symbol_body", new() { ["symbolId"] = "OrderService.Unused", ["body"] = "=> 9" });
        await solution.CallAsync("move_type_to_file", new() { ["typeSymbolId"] = "T:Fixture.Trading.Renamed" });

        var undone = await solution.CallAsync("undo_last_change", []);

        Assert.Contains("nothing to undo - 2 snapshot(s) were dropped after move_type_to_file moved src", undone, StringComparison.Ordinal);
        Assert.Contains("Unused() => 9;", await File.ReadAllTextAsync(solution.OrderServicePath, Token), StringComparison.Ordinal);
    }
}
