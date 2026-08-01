using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceIndexesTests : IDisposable
{
    private const string Template = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="KEYStyle" TargetType="Button" BasedOn="{StaticResource KEY}" />
          <SolidColorBrush x:Key="KEY" x:Uid="KEYUid" Color="#102030" />
        </ResourceDictionary>
        """;

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-workspace-index-");
    private readonly List<IDisposable> owned = [];

    public void Dispose()
    {
        foreach (var disposable in owned)
            disposable.Dispose();

        directory.Delete(recursive: true);
    }

    [Fact]
    public void Xaml_TwiceAtTheSameGeneration_ReturnsTheSameGraph()
    {
        WriteMarkup("Shell", "Accent");

        var harness = Open();

        Assert.Same(harness.Indexes.Xaml(), harness.Indexes.Xaml());
        Assert.Contains("xaml(hit=1 miss=1 files=1)", harness.Indexes.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_UnderParallelCallers_BuildsExactlyOnce()
    {
        WriteMarkup("Shell", "Accent");

        var harness = Open();
        var graphs = new XamlResourceGraph[32];

        Parallel.For(0, graphs.Length, index => graphs[index] = harness.Indexes.Xaml());

        Assert.All(graphs, graph => Assert.Same(graphs[0], graph));
        Assert.Contains("xaml(hit=31 miss=1 files=1)", harness.Indexes.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_WhenTheWatcherIsOff_ReparsesOnlyTheFileThatChanged()
    {
        const int files = ParsedDocumentCache.MaxDocuments + 72;

        WriteViews(files);

        var harness = Open();
        var before = harness.Indexes.Xaml();

        Assert.Equal(files, harness.Indexes.Documents.Parses);

        harness.Sync.Off();
        WriteMarkup("View2", "Rewritten");

        var after = harness.Indexes.Xaml();

        Assert.Equal(files + 1, harness.Indexes.Documents.Parses);
        Assert.True(after.Declares("Rewritten"));
        Assert.False(after.Declares("Key2"));
        Assert.Same(Record(before, "View1"), Record(after, "View1"));
        Assert.NotSame(Record(before, "View2"), Record(after, "View2"));
    }

    [Fact]
    public void Xaml_RebuiltIncrementally_MatchesAFromScratchBuild()
    {
        WriteViews(6);

        var cache = new ParsedDocumentCache();
        var previous = XamlResourceGraph.Build(directory.FullName, cache, null);

        WriteMarkup("View1", "Changed");
        WriteMarkup("View9", "Added");
        File.Delete(Path.Combine(directory.FullName, "View3.xaml"));
        File.WriteAllText(Path.Combine(directory.FullName, "Broken.xaml"), "<Root>");

        var incremental = XamlResourceGraph.Build(directory.FullName, cache, previous);
        var scratch = XamlResourceGraph.Build(directory.FullName, new ParsedDocumentCache(), null);

        Assert.Equal(Shape(scratch), Shape(incremental));
        Assert.Equal(1, incremental.SkippedCount);
        Assert.Equal(scratch.SkippedCount, incremental.SkippedCount);
        Assert.Equal(Styles(scratch), Styles(incremental));
    }

    [Fact]
    public void Xaml_OverALargeTree_AnswersLaterCallsWithNoFurtherFileRead()
    {
        const int files = 500;

        WriteViews(files);

        var harness = Open();
        var graph = harness.Indexes.Xaml();

        Assert.Equal(files, graph.FileCount);
        Assert.Equal(files, harness.Indexes.Documents.Parses);
        Assert.True(harness.Indexes.Documents.Count <= ParsedDocumentCache.MaxDocuments);

        for (var call = 0; call < 1000; call++)
            Assert.Single(harness.Indexes.Xaml().Of("Key7"));

        Assert.Equal(files, harness.Indexes.Documents.Parses);
        Assert.Contains("xaml(hit=1000 miss=1 files=500)", harness.Indexes.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resx_TwiceAtTheSameGeneration_ReturnsTheSameIndex()
    {
        File.WriteAllText(
            Path.Combine(directory.FullName, "Strings.resx"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n  <data name=\"Alpha\"><value>First</value></data>\n</root>\n");

        var harness = Open();

        Assert.Same(harness.Indexes.Resx(), harness.Indexes.Resx());
        Assert.Contains("resx(hit=1 miss=1 families=1)", harness.Indexes.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_BeforeAnythingIsBuilt_ReportsNoSizes() =>
        Assert.Contains(
            "index=xaml(hit=0 miss=0 files=-) resx(hit=0 miss=0 families=-) code(hit=0 miss=0 calls=-) documents=0/128 parses=0",
            Open().Indexes.Describe(),
            StringComparison.Ordinal);

    [Fact]
    public async Task Xaml_SurvivesACodeChangeAndRebuildsAfterAMarkupChange()
    {
        var token = TestContext.Current.CancellationToken;
        using var workspace = await TemporaryWorkspace.OpenAsync(token);

        await workspace.MaterialiseAsync(token);
        workspace.Sync.Watching();

        var first = workspace.Workspace.Indexes.Xaml();

        Assert.Same(first, workspace.Workspace.Indexes.Xaml());

        var beforeCode = workspace.Sync.Generations.Code;

        await ChangedAsync(workspace, workspace.Files.OrderServicePath, token);

        Assert.NotEqual(beforeCode, workspace.Sync.Generations.Code);
        Assert.Same(first, workspace.Workspace.Indexes.Xaml());

        var beforeXaml = workspace.Sync.Generations.Xaml;

        await ChangedAsync(workspace, Path.Combine(workspace.Files.ProjectDirectory, "Views", "ShellView.xaml"), token);

        Assert.NotEqual(beforeXaml, workspace.Sync.Generations.Xaml);
        Assert.NotSame(first, workspace.Workspace.Indexes.Xaml());
    }

    private static async Task ChangedAsync(TemporaryWorkspace workspace, string path, CancellationToken cancellationToken)
    {
        await File.AppendAllTextAsync(path, Environment.NewLine, cancellationToken);

        workspace.Sync.Notice(path);

        await workspace.SyncAsync(null, cancellationToken);
    }

    private static IReadOnlyList<string> Shape(XamlResourceGraph graph) =>
    [
        .. graph.Files.Select(file => string.Join(
            '|',
            file.Relative,
            file.Failure ?? "-",
            file.Dialect,
            string.Join(',', file.Elements.Select(Describe)),
            string.Join(',', file.References.Select(Describe)),
            string.Join(',', file.Uids.Select(Describe)))),
    ];

    private static IReadOnlyList<string> Styles(XamlResourceGraph graph) => [.. graph.Styles.Select(Describe)];

    private static string Describe(XamlElementFact element) => string.Create(
        CultureInfo.InvariantCulture,
        $"{element.Line}:{element.TypeName}:{element.Key ?? "-"}:{element.Name ?? "-"}");

    private static string Describe(XamlResourceReference reference) =>
        string.Create(CultureInfo.InvariantCulture, $"{reference.Line}:{reference.Key}");

    private static string Describe(XamlUidSite uid) =>
        string.Create(CultureInfo.InvariantCulture, $"{uid.File}:{uid.Line}:{uid.Uid}:{uid.TypeName}");

    private static string Describe(XamlStyle style) => string.Create(
        CultureInfo.InvariantCulture,
        $"{style.File}:{style.Line}:{style.Key ?? "-"}:{style.TargetType}:{style.BasedOn ?? "-"}:{style.Scope}");

    private void WriteViews(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);

            WriteMarkup("View" + suffix, "Key" + suffix);
        }
    }

    private void WriteMarkup(string name, string key) => File.WriteAllText(
        Path.Combine(directory.FullName, name + ".xaml"),
        Template.Replace("KEY", key, StringComparison.Ordinal));

    private Harness Open()
    {
        var sync = new WorkspaceSync(directory.FullName, default);

        sync.Watching();
        owned.Add(sync);

        var indexes = new WorkspaceIndexes(directory.FullName, sync);

        owned.Add(indexes);

        return new Harness(sync, indexes);
    }

    private static XamlFileRecord Record(XamlResourceGraph graph, string name) =>
        graph.Files.Single(file => string.Equals(file.Relative, name + ".xaml", StringComparison.Ordinal));

    private sealed record Harness(WorkspaceSync Sync, WorkspaceIndexes Indexes);
}
