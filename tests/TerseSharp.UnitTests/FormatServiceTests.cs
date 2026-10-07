using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(FixtureSolutionCollection))]
public sealed class FormatServiceTests
{
    [Fact]
    public async Task Cleanup_OverTheWholeSolution_NeverRewritesGeneratedCode()
    {
        var text = await RunAsync(null, FixMode.Usings);

        Assert.DoesNotContain(".g.cs", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Format_WithAnEmptyPath_RefusesRatherThanTargetingEverything()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            new FixScope(string.Empty, ChangedOnly: false),
            new FixRequest(FixMode.None, [], DiagnosticSeverity.Info, Verify: false),
            new EditOptions("format", DryRun: false, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.DocumentNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Cleanup_WithAGlob_TargetsOnlyTheMatchingDocuments()
    {
        var text = await RunAsync("src/Fixture.Trading/Style*.cs", FixMode.Style);

        Assert.Contains("StyleSample.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithADirectory_TargetsEveryDocumentUnderIt()
    {
        var text = await RunAsync("src/Fixture.Trading/Views", FixMode.None);

        Assert.Equal("clean", text);
    }

    private static async Task<string> RunAsync(string? path, FixMode mode)
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            new FixScope(path, ChangedOnly: false),
            new FixRequest(mode, [], DiagnosticSeverity.Info, Verify: mode is FixMode.None),
            new EditOptions(mode is FixMode.None ? "format" : "cleanup", DryRun: true, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsOk, result.Error?.Message);

        return result.Value!;
    }
    [Fact]
    public async Task Format_WithChangedOnly_AndNothingTouchedSinceLoad_RefusesInsteadOfSweepingEverything()
    {
        using var registry = new WorkspaceRegistry();
        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);
        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            new FixScope(null, ChangedOnly: true),
            new FixRequest(FixMode.None, [], DiagnosticSeverity.Info, Verify: false),
            new EditOptions("format", DryRun: true, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsOk);
        Assert.Contains("no document under that scope was modified", result.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("path=", result.Error!.Remedy, StringComparison.Ordinal);
    }

    private const string Untidy = "class C\n{\n    int A(){return  1;}\n    int B(){return  2;}\n}\n";
    private const string TouchedDiff = """
        diff --git a/Touched.cs b/Touched.cs
        --- a/Touched.cs
        +++ b/Touched.cs
        @@ -4,1 +4,1 @@
        +    int B(){return  2;}
        """;

    [Fact]
    public async Task FormatOnly_WithTouchedLines_ReformatsOnlyTheLinesTheTreeChanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-touched-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        try
        {
            var formatted = await FormatService.FormatOnlyAsync(Untouched(root), TouchedLines.From(TouchedDiff, [], root), TestContext.Current.CancellationToken);

            Assert.Equal(
                "class C\n{\n    int A(){return  1;}\n    int B() { return 2; }\n}\n",
                (await formatted.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FormatOnly_WithTouchedLines_LeavesTheLineAfterTheHunkAndADeletionsNeighbourByteIdentical()
    {
        const string Neighboured = "class C\n{\n    int A(){return  1;}\n    int B(){return  2;}\n\tint D(){return  4;}\n}\n";
        const string DeletionAndEdit = """
            diff --git a/Touched.cs b/Touched.cs
            --- a/Touched.cs
            +++ b/Touched.cs
            @@ -3,1 +3,0 @@
            -    int Z() => 0;
            @@ -5,1 +4,1 @@
            +    int B(){return  2;}
            """;
        var root = Path.Combine(Path.GetTempPath(), "terse-touched-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        try
        {
            var formatted = await FormatService.FormatOnlyAsync(Untouched(root, Neighboured), TouchedLines.From(DeletionAndEdit, [], root), TestContext.Current.CancellationToken);

            Assert.Equal(
                "class C\n{\n    int A(){return  1;}\n    int B() { return 2; }\n\tint D(){return  4;}\n}\n",
                (await formatted.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FormatOnly_WithTouchedLines_FixesTheIndentationOfTheChangedLineItself()
    {
        const string Misindented = "class C\n{\n    int A(){return  1;}\n  int B(){return  2;}\n}\n";
        var root = Path.Combine(Path.GetTempPath(), "terse-touched-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        try
        {
            var formatted = await FormatService.FormatOnlyAsync(Untouched(root, Misindented), TouchedLines.From(TouchedDiff, [], root), TestContext.Current.CancellationToken);

            Assert.Equal(
                "class C\n{\n    int A(){return  1;}\n    int B() { return 2; }\n}\n",
                (await formatted.GetTextAsync(TestContext.Current.CancellationToken)).ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Document Untouched(string root, string text = Untidy)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Touched", LanguageNames.CSharp);
        var loader = TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create()));

        return workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project.Id), "Touched.cs", loader: loader, filePath: Path.Combine(root, "Touched.cs")));
    }
}
