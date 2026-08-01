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
    [InlineData("git status --short")]
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
    public void Inspect_ForADotnetCommandTerseSharpReplaces_Denies(string command) =>
        Assert.True(ToolGuard.Inspect("Bash", new JsonObject { ["command"] = command }).Denied);

    [Theory]
    [InlineData("dotnet restore")]
    [InlineData("dotnet pack src/App -c Release")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet run --project src/App")]
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
}
