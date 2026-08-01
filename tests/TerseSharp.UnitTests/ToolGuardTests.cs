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
    public void Inspect_ForAShellTextToolOnDotNetSource_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("dotnet build src/App/App.csproj")]
    [InlineData("git add src/App/OrderService.cs")]
    [InlineData("grep -rn TODO docs/")]
    public void Inspect_ForAShellCommandThatIsNotATextRead_Allows(string command) =>
        Assert.False(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("site/theme.css")]
    [InlineData("data/export.csv")]
    [InlineData("Pages/Index.cshtml")]
    [InlineData("scripts/build.csx")]
    [InlineData("notes-about-cs.md")]
    public void Inspect_ForAnExtensionThatMerelyStartsLikeCSharp_Allows(string path) =>
        Assert.False(ToolGuard.Inspect("Read", new JsonObject { ["file_path"] = path }).Denied);

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
}
