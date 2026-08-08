using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolGuardTests
{
    [Theory]
    [InlineData("Read", "file_path", "src/App/OrderService.cs")]
    [InlineData("Edit", "file_path", "src/App/Views/Shell.xaml")]
    [InlineData("Write", "file_path", "src/App/App.csproj")]
    [InlineData("Read", "file_path", "src/App/Views/Main.axaml")]
    [InlineData("Glob", "pattern", "**/*.cs")]
    public void Inspect_ForABuiltInOnDotNetSource_Denies(string tool, string key, string value) =>
        Assert.True(ToolGuard.Inspect(tool, new JsonObject { [key] = value }).Denied);

    [Theory]
    [InlineData("Read", "file_path", "README.md")]
    [InlineData("Write", "file_path", "notes/plan.txt")]
    [InlineData("Glob", "pattern", "**/*.json")]
    public void Inspect_ForABuiltInOnAnythingElse_Allows(string tool, string key, string value) =>
        Assert.False(ToolGuard.Inspect(tool, new JsonObject { [key] = value }).Denied);

    [Fact]
    public void Inspect_ForAGrepScopedToCSharp_Denies() =>
        Assert.True(ToolGuard.Inspect("Grep", new JsonObject { ["glob"] = "*.cs" }).Denied);

    [Fact]
    public void Inspect_ForAGrepWithNoDotNetScope_Allows() =>
        Assert.False(ToolGuard.Inspect("Grep", new JsonObject { ["pattern"] = "TODO" }).Denied);

    [Theory]
    [InlineData("grep -rn Submit src/App/OrderService.cs")]
    [InlineData("cat src/App/App.csproj")]
    [InlineData("rg Submit --glob *.cs")]
    [InlineData("dotnet build src/App/App.csproj")]
    [InlineData("find . -name \"*.cs\"")]
    [InlineData("fd --extension cs src/App/OrderService.cs")]
    [InlineData("ls src/App/*.cs")]
    [InlineData("dir src\\App\\App.csproj")]
    [InlineData("tree src/App/Views/Shell.xaml")]
    [InlineData("wc -l src/App/OrderService.cs")]
    [InlineData("nl src/App/OrderService.cs")]
    public void Inspect_ForAShellTextToolOnDotNetSource_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("dotnet pack src/App/App.csproj")]
    [InlineData("dotnet restore src/App/App.csproj")]
    [InlineData("git add src/App/OrderService.cs")]
    [InlineData("grep -rn TODO docs/")]
    [InlineData("ls src/App")]
    [InlineData("find . -name \"*.md\"")]
    public void Inspect_ForAShellCommandThatIsNotATextRead_Allows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("dotnet format analyzers TerseSharp.slnx --verify-no-changes")]
    [InlineData("dotnet format style --severity info")]
    [InlineData("dotnet clean")]
    [InlineData("cd src && dotnet clean App.csproj")]
    [InlineData("dotnet watch test")]
    [InlineData("dotnet watch --project src/App build")]
    public void Inspect_ForADotnetCommandTerseSharpReplaces_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("dotnet restore")]
    [InlineData("dotnet pack src/App -c Release")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet run --project src/App")]
    [InlineData("dotnet watch run")]
    [InlineData("dotnet watch run --launch-profile test")]
    [InlineData("dotnet watch run -- test")]
    [InlineData("dotnet build-server shutdown")]
    [InlineData("dotnet tool update -g TerseSharp")]
    public void Inspect_ForADotnetCommandNoToolReplaces_Allows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Fact]
    public void Inspect_ForDotnetFormat_NamesTheCleanupReplacement() =>
        Assert.Contains(
            "cleanup verify=true",
            ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "dotnet format --verify-no-changes" }).Reason,
            StringComparison.Ordinal);

    [Fact]
    public void Inspect_ForDotnetClean_NamesTheCleanTool() =>
        Assert.Contains(
            "use clean",
            ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "dotnet clean" }).Reason,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("site/theme.css")]
    [InlineData("data/export.csv")]
    [InlineData("wwwroot/app.js")]
    [InlineData("scripts/build.csx")]
    [InlineData("notes-about-cs.md")]
    public void Inspect_ForAnExtensionThatMerelyStartsLikeCSharp_Allows(string path) =>
        Assert.False(ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = path }).Denied);

    [Fact]
    public void Inspect_ForARazorFile_DeniesAndNamesTheRazorTools()
    {
        foreach (var path in new[] { "Components/Card.razor", "Pages/Index.cshtml", "Components/Card.razor.css", "Components/Card.razor.js" })
        {
            var verdict = ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = path });

            Assert.True(verdict.Denied);
            Assert.Contains("razor_outline", verdict.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Inspect_ForAnEditOfARazorFile_NamesTheRazorEditTools()
    {
        var verdict = ToolGuard.Inspect("Edit", new JsonObject { ["file_path"] = "Components/Card.razor" });

        Assert.True(verdict.Denied);
        Assert.Contains("razor_set_attribute", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ForAGrepTypedToRazor_Denies() =>
        Assert.True(ToolGuard.Inspect("Grep", new JsonObject { ["type"] = "cshtml" }).Denied);

    [Fact]
    public void Inspect_ForAGrepTypedToCSharp_Denies() =>
        Assert.True(ToolGuard.Inspect("Grep", new JsonObject { ["type"] = "cs" }).Denied);

    [Theory]
    [InlineData("cd src && cat Foo.cs")]
    [InlineData("ls | grep Foo.cs")]
    [InlineData("echo hi; cat App.csproj")]
    public void Inspect_ForATextReadLaterInACompoundCommand_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Fact]
    public async Task RunAsync_WhenToolNameIsNotAString_AllowsRatherThanCrashing()
    {
        var output = new StringWriter();

        await ToolGuard.RunAsync(
            new StringReader("{\"tool_name\":42,\"tool_input\":{}}"),
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal("{}", output.ToString().Trim());
    }

    [Fact]
    public void Inspect_ForAToolTheGuardDoesNotCover_Allows() =>
        Assert.False(ToolGuard.Inspect("WebFetch", new JsonObject { ["url"] = "https://example.com/a.cs" }).Denied);

    [Fact]
    public void Render_ForADenial_NamesTheReplacementTools()
    {
        var text = ToolGuard.Render(ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = "A.cs" }));

        Assert.Contains("\"permissionDecision\":\"deny\"", text, StringComparison.Ordinal);
        Assert.Contains("get_file_outline", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForAnAllowedCall_IsAnEmptyObject() =>
        Assert.Equal("{}", ToolGuard.Render(ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = "a.md" })));

    [Fact]
    public async Task RunAsync_ForMalformedInput_AllowsRatherThanCrashing()
    {
        var output = new StringWriter();

        await ToolGuard.RunAsync(new StringReader("not json"), output, TestContext.Current.CancellationToken);

        Assert.Equal("{}", output.ToString().Trim());
    }

    [Theory]
    [InlineData("Read", "file_path", "src/App/Strings.resx")]
    [InlineData("Edit", "file_path", "src/App/Strings.fr.resx")]
    [InlineData("Read", "file_path", "src/App/Views/Resources.resw")]
    public void Inspect_ForABuiltInOnAResourceFile_Denies(string tool, string key, string value) =>
        Assert.True(ToolGuard.Inspect(tool, new JsonObject { [key] = value }).Denied);

    [Fact]
    public void Render_ForAResourceDenial_NamesTheResxTools()
    {
        var verdict = ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = "src/App/Strings.resx" });

        Assert.Contains("resx_get", verdict.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("get_file_outline", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForAResourceEdit_NamesTheResxWriters()
    {
        var verdict = ToolGuard.Inspect("Edit", new JsonObject { ["file_path"] = "src/App/Strings.resx" });

        Assert.Contains("resx_set", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Edit")]
    public void Render_ForASourceFileThatDoesNotExist_NamesTheToolThatCanCreateIt(string tool)
    {
        var verdict = ToolGuard.Inspect(tool, new JsonObject { ["file_path"] = MissingSourcePath() });

        Assert.Contains("write_text(path, content, force=true)", verdict.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("replace_symbol_body", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForASourceFileThatExists_NamesTheCompileGatedEditors()
    {
        var verdict = ToolGuard.Inspect("Edit", new JsonObject { ["file_path"] = Fixtures.OrderServicePath });

        Assert.Contains("replace_symbol_body", verdict.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("write_text(path, content, force=true)", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Edit")]
    public void Render_ForASourceWriteDenial_SaysExternalChangesArePickedUpAutomatically(string tool)
    {
        var verdict = ToolGuard.Inspect(tool, new JsonObject { ["file_path"] = "src/App/OrderService.cs" });

        Assert.Contains("picked up automatically", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Read", "file_path", "src/App/OrderService.cs")]
    [InlineData("Grep", "glob", "*.cs")]
    [InlineData("Read", "file_path", "src/App/Views/Shell.xaml")]
    public void Render_ForADenialThatWritesNothing_OmitsTheFreshnessClause(string tool, string key, string value)
    {
        var verdict = ToolGuard.Inspect(tool, new JsonObject { [key] = value });

        Assert.DoesNotContain("picked up automatically", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForARelativeSourcePath_NeverRecommendsAnUnconditionalOverwrite()
    {
        var verdict = ToolGuard.Inspect("Edit", new JsonObject { ["file_path"] = "src/App/OrderService.cs" });

        Assert.Contains("replace_symbol_body", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("when the file does not exist yet", verdict.Reason, StringComparison.Ordinal);
    }

    private static string MissingSourcePath() =>
        Path.Combine(Path.GetTempPath(), "terse-guard-" + Guid.NewGuid().ToString("N") + ".cs");

    [Fact]
    public void Inspect_ForAGlobOverXaml_NamesTheXamlToolsBeforeFindFiles()
    {
        var verdict = ToolGuard.Inspect("Glob", new JsonObject { ["pattern"] = "**/*.xaml" });

        Assert.True(verdict.Denied);
        Assert.Contains("xaml_find", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("xaml_styles", verdict.Reason, StringComparison.Ordinal);
        Assert.True(
            verdict.Reason.IndexOf("xaml_resolve", StringComparison.Ordinal)
                < verdict.Reason.IndexOf("find_files", StringComparison.Ordinal),
            verdict.Reason);
    }

    [Fact]
    public void Inspect_ForAShellWalkOverXaml_NamesTheXamlTools()
    {
        var verdict = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "ls src/App/Views/*.xaml" });

        Assert.True(verdict.Denied);
        Assert.Contains("xaml_find", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Glob", "pattern", "**/*.resx")]
    [InlineData("Grep", "glob", "*.resw")]
    public void Inspect_ForAGlobOrGrepOverResources_NamesTheResxQueryTools(string tool, string key, string value)
    {
        var verdict = ToolGuard.Inspect(tool, new JsonObject { [key] = value });

        Assert.True(verdict.Denied);
        Assert.Contains("resx_find", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("resx_validate", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Get-ChildItem -Recurse -Filter *.cs")]
    [InlineData("gci src/App/Views/*.xaml")]
    [InlineData("Select-String Submit src/App/OrderService.cs")]
    [InlineData("sls TODO src/App/App.csproj")]
    [InlineData("Get-Content src/App/OrderService.cs")]
    [InlineData("gc src/App/Strings.resx")]
    public void Inspect_ForAPowerShellTextReadOnDotNetSource_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);
    [Fact]
    public void Inspect_ForAGrepTypedToXaml_NamesTheXamlToolsRatherThanTheCSharpOnes()
    {
        var verdict = ToolGuard.Inspect("Grep", new JsonObject { ["type"] = "xaml" });

        Assert.True(verdict.Denied);
        Assert.Contains("xaml_find", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("find_usages", verdict.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("search_symbols", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git status --porcelain")]
    [InlineData("git status --short")]
    [InlineData("git diff")]
    [InlineData("git diff --cached")]
    [InlineData("git diff main...HEAD --stat")]
    [InlineData("git -C src/App status")]
    [InlineData("git -c core.pager=cat status")]
    [InlineData("git --no-pager diff")]
    [InlineData("git.exe status")]
    [InlineData("cd src && git diff")]
    public void Inspect_ForAGitCommandTerseSharpReplaces_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("git log -p")]
    [InlineData("git blame src/App/OrderService.cs")]
    [InlineData("git show HEAD:src/App/OrderService.cs")]
    [InlineData("git difftool")]
    [InlineData("git -c core.pager=cat log")]
    [InlineData("git stash show -p")]
    [InlineData("git commit -m \"diff status\"")]
    [InlineData("git push origin main")]
    [InlineData("git stash")]
    public void Inspect_ForAGitCommandNoToolReplaces_Allows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Fact]
    public void Inspect_ForGitStatus_NamesChangedFiles() =>
        Assert.Contains(
            "changed_files",
            ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git status --porcelain" }).Reason,
            StringComparison.Ordinal);

    [Fact]
    public void Inspect_ForGitDiff_NamesDiffSymbolsBeforeDiffText() =>
        Assert.Contains(
            "diff_symbols, then diff_text",
            ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git diff main" }).Reason,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("git status")]
    [InlineData("git diff")]
    [InlineData("dotnet build")]
    [InlineData("dotnet test")]
    public void Inspect_ForAReplacedCommand_TellsTheAgentNotToRunItInBashAgain(string command) =>
        Assert.Contains(
            "do not run this in Bash again",
            ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Reason,
            StringComparison.Ordinal);

    [Theory]
    [InlineData("git status --porcelain")]
    [InlineData("git diff main")]
    public async Task Inspect_ForAGitCommandOutsideADotNetTree_Allows(string command)
    {
        var directory = Directory.CreateTempSubdirectory("terse-guard-plain");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "index.ts"), "export {};", TestContext.Current.CancellationToken);

            Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }, directory.FullName).Denied);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Inspect_ForAGitCommandUnderADotNetProject_Denies()
    {
        var directory = Directory.CreateTempSubdirectory("terse-guard-solution");
        var nested = directory.CreateSubdirectory("src").CreateSubdirectory("App");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "App.csproj"), "<Project />", TestContext.Current.CancellationToken);

            Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git status" }, nested.FullName).Denied);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("\"git\" diff")]
    [InlineData("'git' status")]
    [InlineData("GIT_PAGER=cat git diff")]
    [InlineData("GIT_PAGER=cat GIT_CONFIG_NOSYSTEM=1 git status")]
    [InlineData("(git status)")]
    [InlineData("( git diff )")]
    [InlineData("\"dotnet\" build")]
    [InlineData("DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test")]
    [InlineData("(dotnet build)")]
    [InlineData("\"grep\" -r Order src/App/OrderService.cs")]
    [InlineData("LC_ALL=C grep -r Order src/App/OrderService.cs")]
    public void Inspect_ForAReplacedCommandBehindAQuoteEnvPrefixOrSubShell_StillDenies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied, command);


    [Theory]
    [InlineData("GIT_PAGER=cat git log -p")]
    [InlineData("\"git\" commit -m \"diff status\"")]
    [InlineData("(git push origin main)")]
    [InlineData("DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet restore")]
    [InlineData("\"dotnet\" pack src/App")]
    public void Inspect_ForAnUnreplacedCommandBehindAQuoteEnvPrefixOrSubShell_StillAllows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied, command);

    [Fact]
    public void LockHolders_ForThisProcess_NamesItAsTheServerRatherThanAnUnknownPid()
    {
        var pid = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var described = LockHolders.Describe("MSB3027: Could not copy \"terse.dll\". The file is locked by: \"terse (" + pid + ")\"");

        Assert.Contains("holder pid=" + pid, described, StringComparison.Ordinal);
        Assert.Contains("this terse server", described, StringComparison.Ordinal);
        Assert.Contains("startedUtc=", described, StringComparison.Ordinal);
    }

    [Fact]
    public void LockHolders_ForAPidThatIsGone_SaysTheLockIsReleasedInsteadOfGuessing()
    {
        var described = LockHolders.Describe("The file is locked by: \"testhost (2147483646)\"");

        Assert.Contains("holder pid=2147483646", described, StringComparison.Ordinal);
        Assert.Contains("already gone", described, StringComparison.Ordinal);
    }

    [Fact]
    public void LockHolders_WithNoPidInTheOutput_AddsNothing() =>
        Assert.Equal(string.Empty, LockHolders.Describe("MSB3021: Unable to copy file, access is denied."));

    [Theory]
    [InlineData("src/Fixture.Trading/OrderService.cs(12,5): warning CA1822: mark as static")]
    [InlineData("Microsoft.Build.Tasks.Core (17.0) could not be resolved")]
    [InlineData("Restore (1) succeeded in 2.3s")]
    public void LockHolders_ForTextThatMerelyLooksLikeAPid_AddsNothing(string output) =>
            Assert.Equal(string.Empty, LockHolders.Describe(output));

    [Theory]
    [InlineData("$(git status)")]
    [InlineData("X=$(git status --porcelain)")]
    [InlineData("FILES=$(git diff --name-only)")]
    [InlineData("$(dotnet build)")]
    [InlineData("`git diff`")]
    public void Inspect_ForAReplacedCommandInsideACommandSubstitution_StillDenies(string command) =>
            Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied, command);


    [Theory]
    [InlineData("$(git log -1 --format=%H)")]
    [InlineData("SHA=$(git rev-parse HEAD)")]
    [InlineData("$(dotnet restore)")]
    public void Inspect_ForAnUnreplacedCommandInsideACommandSubstitution_StillAllows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied, command);

    [Fact]
    public void StillLocked_WhenTheBuildNamedNoHolder_DoesNotPromiseAListBelow()
    {
        var note = Server.Tools.BuildTools.StillLocked("build", "MSB3021: Unable to copy file, access is denied.");

        Assert.DoesNotContain("below before stopping it", note, StringComparison.Ordinal);
        Assert.Contains("named no holding process", note, StringComparison.Ordinal);
        Assert.DoesNotContain("holder pid=", note, StringComparison.Ordinal);
    }

    [Fact]
    public void StillLocked_WhenTheBuildNamedAHolder_PointsAtTheListItAppends()
    {
        var pid = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var note = Server.Tools.BuildTools.StillLocked("run_tests", "The file is locked by: \"terse (" + pid + ")\"");

        Assert.Contains("Resolve each holder below before stopping it.", note, StringComparison.Ordinal);
        Assert.Contains("holder pid=" + pid, note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git ls-files")]
    [InlineData("git -C src ls-files")]
    [InlineData("git ls-files fixtures")]
    public void Inspect_ForABareGitLsFiles_NamesFindFilesTracked(string command)
    {
        var verdict = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command });

        Assert.True(verdict.Denied, command);
        Assert.Contains("find_files tracked=true", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git ls-files --others --exclude-standard")]
    [InlineData("git ls-files --deleted")]
    [InlineData("git ls-files -z")]
    [InlineData("git ls-remote")]
    public void Inspect_ForAGitListingNothingReplaces_Allows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied, command);

    [Fact]
    public async Task Inspect_ForAGitCommandDirectedAtADirectoryWithNoSolution_Allows()
    {
        var (dotnet, plain) = await TreesAsync();

        var directed = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git -C " + plain + " ls-files" }, dotnet);
        var relative = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git -C ../notes status" }, dotnet);
        var operand = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git status ../notes" }, dotnet);

        Assert.False(directed.Denied, directed.Reason);
        Assert.False(relative.Denied, relative.Reason);
        Assert.False(operand.Denied, operand.Reason);
    }

    [Fact]
    public async Task Inspect_ForAGitCommandDirectedAtTheDotNetTreeItself_StillDenies()
    {
        var (dotnet, _) = await TreesAsync();

        var here = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git status" }, dotnet);
        var inside = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "git -C . diff" }, dotnet);

        Assert.True(here.Denied, here.Reason);
        Assert.True(inside.Denied, inside.Reason);
    }

    private static async Task<(string DotNet, string Plain)> TreesAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-guard-" + Guid.NewGuid().ToString("N"));
        var dotnet = Path.Combine(root, "solution");
        var plain = Path.Combine(root, "notes");

        Directory.CreateDirectory(dotnet);
        Directory.CreateDirectory(plain);

        await File.WriteAllTextAsync(Path.Combine(dotnet, "App.csproj"), "<Project />", TestContext.Current.CancellationToken);

        return (dotnet, plain);
    }

    [Theory]
    [InlineData("dotnet format analyzers TerseSharp.slnx --verify-no-changes --severity info", "cleanup verify=true fix=analyzers")]
    [InlineData("dotnet format style TerseSharp.slnx --verify-no-changes --severity info", "cleanup verify=true fix=style")]
    [InlineData("dotnet format analyzers", "cleanup fix=analyzers")]
    [InlineData("dotnet format style", "cleanup fix=style")]
    public void Inspect_ForDotnetFormatWithASubcommand_NamesTheExactCleanupThatVerifiesTheSameThing(string command, string replacement)
    {
        var verdict = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command });

        Assert.True(verdict.Denied, command);
        Assert.Contains(replacement, verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_ForDotnetFormatWithoutASubcommand_StillNamesTheWhitespaceAndCleanupPair()
    {
        var verdict = ToolGuard.Inspect("Bash", new JsonObject { ["command"] = "dotnet format TerseSharp.slnx --verify-no-changes" });

        Assert.True(verdict.Denied);
        Assert.Contains("format verify=true", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("cleanup verify=true", verdict.Reason, StringComparison.Ordinal);
    }
}
