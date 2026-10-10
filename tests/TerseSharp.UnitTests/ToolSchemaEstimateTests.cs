using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Protocol;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[CollectionDefinition(nameof(AdvertisedCostState), DisableParallelization = true)]
public sealed class AdvertisedCostState;

[Collection(nameof(AdvertisedCostState))]
public sealed class ToolSchemaEstimateTests
{
    private const string Cap = "1024-token cap";

    private static string Source(string description) => $$"""
        using System;
        using System.Threading;

        public sealed class McpServerToolAttribute : Attribute { public string Name { get; set; } }

        public sealed class DescriptionAttribute : Attribute { public DescriptionAttribute(string text) { } }

        public sealed class FakeTools
        {
            private const string Text = "{{description}}";

            [McpServerTool(Name = "fake_tool")]
            [Description(Text)]
            public string Fake([Description("xy")] string workspace = null, CancellationToken cancellationToken = default) => string.Empty;

            public string NotATool(string ignored) => ignored;
        }
        """;

    private static Solution SolutionOf(AdhocWorkspace workspace, string source)
    {
        var project = workspace.AddProject("fake", LanguageNames.CSharp)
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        return project.AddDocument("FakeTools.cs", SourceText.From(source)).Project.Solution;
    }

    [Fact]
    public async Task DeclaredAsync_CountsTheNameAndEveryConstantDescription_AndSkipsTheCancellationToken()
    {
        using var workspace = new AdhocWorkspace();

        var declared = await ToolSchemaSource.DeclaredAsync(SolutionOf(workspace, Source("abc")), TestContext.Current.CancellationToken);

        var tool = Assert.Single(declared);
        Assert.Equal("fake_tool", tool.Name);
        Assert.Equal("fake_tool".Length + "abc".Length + "workspace".Length + "xy".Length, tool.Basis);
        Assert.Equal(1, tool.Parameters);
    }

    [Fact]
    public async Task Render_ForANewToolWithAHugeDescription_FlagsItOverTheCap()
    {
        using var workspace = new AdhocWorkspace();
        var declared = await ToolSchemaSource.DeclaredAsync(SolutionOf(workspace, Source(new string('x', 5000))), TestContext.Current.CancellationToken);

        var text = ToolSchemaEstimate.Render(declared, new Dictionary<string, InstalledSchema>(StringComparer.Ordinal), static _ => 0);

        Assert.StartsWith("source=1 tools declared in the working tree, schema estimated HEURISTIC: 1 differ from the running server, 1 over the " + Cap, text, StringComparison.Ordinal);
        Assert.Contains("\n  fake_tool new -> 1275 OVER", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForAToolWhoseDescriptionGrew_PricesItOnTheInstalledFrameAndFlagsTheCap()
    {
        var installed = new Dictionary<string, InstalledSchema>(StringComparer.Ordinal) { ["run_tests"] = new(4000, 32) };

        var text = ToolSchemaEstimate.Render([new DeclaredTool("run_tests", 4100, 3)], installed, static _ => 0);

        Assert.Equal(
            "source=1 tools declared in the working tree, schema estimated HEURISTIC: 1 differ from the running server, 1 over the " + Cap + "\n  run_tests 1008 -> 1033 OVER",
            text);
    }

    [Fact]
    public void Render_ForAnUnchangedToolUnderTheCap_ListsNothing()
    {
        var installed = new Dictionary<string, InstalledSchema>(StringComparer.Ordinal) { ["find_files"] = new(2000, 900) };

        var text = ToolSchemaEstimate.Render([new DeclaredTool("find_files", 2000, 4)], installed, static _ => 0);

        Assert.Equal("source=1 tools declared in the working tree, schema estimated HEURISTIC: 0 differ from the running server, 0 over the " + Cap, text);
    }

    [Fact]
    public void Render_CountsTheWorkedExampleTheServerAppends_TowardTheCap()
    {
        var installed = new Dictionary<string, InstalledSchema>(StringComparer.Ordinal) { ["search_regex"] = new(3000, 1000) };

        var text = ToolSchemaEstimate.Render([new DeclaredTool("search_regex", 3000, 6)], installed, static _ => 120);

        Assert.Contains("\n  search_regex 1030 -> 1030 OVER", text, StringComparison.Ordinal);
        Assert.Contains("0 differ from the running server, 1 over", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EstimatedAsync_ForSourceThatMatchesTheInstalledSchema_ReportsNoDifference_AndAnEditAsOne()
    {
        var installed = new Tool
        {
            Name = "fake_tool",
            Description = "abc  example: fake_tool x=1",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"workspace":{"description":"xy","type":"string"}}}""").RootElement,
        };
        AdvertisedCost.Observe([installed], [installed]);

        using var first = new AdhocWorkspace();
        using var second = new AdhocWorkspace();
        var same = await AdvertisedCost.EstimatedAsync(SolutionOf(first, Source("abc")), TestContext.Current.CancellationToken);
        var edited = await AdvertisedCost.EstimatedAsync(SolutionOf(second, Source("abcd")), TestContext.Current.CancellationToken);

        Assert.Equal("source=1 tools declared in the working tree, schema estimated HEURISTIC: 0 differ from the running server, 0 over the " + Cap, same);
        Assert.Contains("1 differ from the running server", edited, StringComparison.Ordinal);
        Assert.Contains("\n  fake_tool ", edited, StringComparison.Ordinal);
    }

    [Fact]
    public void Trailer_ForAToolWhoseDescriptionChanged_PricesItOnTheInstalledFrameAgainstTheCap_AndSkipsTheUnchangedOne()
    {
        var installed = new Dictionary<string, InstalledSchema>(StringComparer.Ordinal) { ["edit_text"] = new(4000, 420) };
        DeclaredTool[] original = [new("edit_text", 3900, 17), new("read_text", 2000, 12)];
        DeclaredTool[] edited = [new("edit_text", 4000, 17), new("read_text", 2000, 12)];

        var trailer = ToolSchemaEstimate.Trailer(edited, original, installed, static name => name == "edit_text" ? 36 : 0);

        Assert.Equal("  schema edit_text=1114 tokens (cap 1024, over=90)", trailer);
    }

    [Fact]
    public void Trailer_WhenNoToolChanged_IsEmpty()
    {
        DeclaredTool[] tools = [new("find_files", 2000, 4)];

        Assert.Equal(string.Empty, ToolSchemaEstimate.Trailer(tools, tools, new Dictionary<string, InstalledSchema>(StringComparer.Ordinal), static _ => 0));
    }

    [Fact]
    public async Task TrailerAsync_ForAnEditedDescriptionOfAnUninstalledTool_PricesItOnTheFrameHeuristicUnderTheCap()
    {
        ToolSchemaEstimate.Publish(static () => new Dictionary<string, InstalledSchema>(StringComparer.Ordinal), static _ => 0);
        using var workspace = new AdhocWorkspace();
        var document = SolutionOf(workspace, Source("abc")).Projects.Single().Documents.Single();

        var trailer = await ToolSchemaEstimate.TrailerAsync(document, document.WithText(SourceText.From(Source("abcd"))), TestContext.Current.CancellationToken);
        var unchanged = await ToolSchemaEstimate.TrailerAsync(document, document, TestContext.Current.CancellationToken);

        Assert.Equal("  schema fake_tool=26 tokens (cap 1024, left=998)", trailer);
        Assert.Equal(string.Empty, unchanged);
    }

    [Fact]
    public async Task Observe_PublishesTheInstalledFrameThatTheEditTrailerPricesAgainst()
    {
        var installed = new Tool
        {
            Name = "fake_tool",
            Description = "abc",
            InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"workspace":{"description":"xy","type":"string"}}}""").RootElement,
        };
        AdvertisedCost.Observe([installed], [installed]);
        using var workspace = new AdhocWorkspace();
        var document = SolutionOf(workspace, Source("abc")).Projects.Single().Documents.Single();

        var trailer = await ToolSchemaEstimate.TrailerAsync(document, document.WithText(SourceText.From(Source("abcd"))), TestContext.Current.CancellationToken);

        Assert.Equal("  schema fake_tool=24 tokens (cap 1024, left=1000)", trailer);
    }
}
