using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class PowerShellGuardTests
{
    [Fact]
    public void Inspect_ForAPowerShellGetContentOfACSharpFile_DeniesItNamingTheOutline()
    {
        var verdict = PowerShell("Get-Content src/App/OrderService.cs");

        Assert.True(verdict.Denied);
        Assert.Contains("get_file_outline", verdict.Routing ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_ForAPowerShellGetContentOfANonCodeFileInADotNetTree_DeniesItNamingReadText()
    {
        var tree = await TreeAsync();

        try
        {
            var verdict = PowerShell("Get-Content README.md -Tail 5", tree.FullName);

            Assert.True(verdict.Denied, verdict.Reason);
            Assert.Contains("read_text", verdict.Routing ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            tree.Delete(true);
        }
    }

    [Fact]
    public async Task Inspect_ForAPowerShellGetContentOfAFileOutsideTheTree_AllowsIt()
    {
        var tree = await TreeAsync();
        var outside = Directory.CreateTempSubdirectory("terse-ps-outside");

        try
        {
            var verdict = PowerShell("Get-Content " + Path.Combine(outside.FullName, "scan.log") + " -Tail 5", tree.FullName);

            Assert.False(verdict.Denied, verdict.Reason);
        }
        finally
        {
            tree.Delete(true);
            outside.Delete(true);
        }
    }

    [Fact]
    public async Task Inspect_ForAPowerShellListingInADotNetTree_DeniesItNamingFindFiles()
    {
        var tree = await TreeAsync();

        try
        {
            var verdict = PowerShell("Get-ChildItem src -Recurse", tree.FullName);

            Assert.True(verdict.Denied, verdict.Reason);
            Assert.Contains("find_files", verdict.Routing ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            tree.Delete(true);
        }
    }

    [Theory]
    [InlineData("Start-Sleep -Seconds 30")]
    [InlineData("Start-Sleep 5; Get-Process")]
    public void Inspect_ForABarePowerShellStartSleep_DeniesIt(string command)
    {
        var verdict = PowerShell(command);

        Assert.True(verdict.Denied, command);
        Assert.Contains("is not how you wait", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("while (!$p.HasExited) { Start-Sleep -Milliseconds 500 }")]
    [InlineData("do { Start-Sleep -Seconds 1 } until ($p.HasExited)")]
    [InlineData("Wait-Process -Id 4242")]
    public void Inspect_ForAPowerShellWaitThatWatchesTheProcess_AllowsIt(string command) =>
        Assert.False(PowerShell(command).Denied, command);

    [Theory]
    [InlineData("Get-Content src/App/OrderService.cs | ForEach-Object { \"`t$_\" }")]
    [InlineData("@'\nGet-Content src/App/OrderService.cs\n'@ | Set-Content notes.txt")]
    [InlineData("<# Get-Content src/App/OrderService.cs #> Get-Date")]
    public void Inspect_ForPowerShellTheGuardCannotParseSoundly_AllowsIt(string command) =>
        Assert.False(PowerShell(command).Denied, command);

    [Theory]
    [InlineData("Get-Process | Format-Table")]
    [InlineData("npm install")]
    public void Inspect_ForAPowerShellCommandNothingReplaces_AllowsIt(string command) =>
        Assert.False(PowerShell(command).Denied, command);

    [Fact]
    public void Inspect_ForAPowerShellCallWithNoCommand_AllowsIt() =>
        Assert.False(ToolGuard.Inspect("PowerShell", []).Denied);

    [Fact]
    public void Inspect_ForAPowerShellBatchWithAReplacedPart_DeniesTheWholeCallWithoutARewrite()
    {
        var verdict = PowerShell("Get-Content src/App/OrderService.cs; npm install");

        Assert.True(verdict.Denied);
        Assert.Null(verdict.Rewrite);
        Assert.Contains("NOTHING in this PowerShell batch ran", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains("npm install", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Guarded_RegistersThePreToolUseGuardOnThePowerShellTool()
    {
        var entries = ClientRegistrar.Guarded([], folderRules: false)["hooks"]!["PreToolUse"]!.AsArray();

        Assert.Contains(entries, entry => entry?["matcher"]?.GetValue<string>() is "Read|Write|Edit|MultiEdit|NotebookEdit|Grep|Glob|Bash|PowerShell");
    }

    private static GuardVerdict PowerShell(string command, string? cwd = null) =>
        ToolGuard.Inspect("PowerShell", new JsonObject { ["command"] = command }, cwd);

    private static async Task<DirectoryInfo> TreeAsync()
    {
        var tree = Directory.CreateTempSubdirectory("terse-ps-tree");

        await File.WriteAllTextAsync(Path.Combine(tree.FullName, "App.csproj"), "<Project />", TestContext.Current.CancellationToken);

        return tree;
    }
}
