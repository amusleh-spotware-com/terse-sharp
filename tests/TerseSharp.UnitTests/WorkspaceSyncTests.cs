using System.Globalization;
using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class WorkspaceSyncTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-sync-root");

    [Theory]
    [InlineData("src/App/Order.cs", ChangeKind.Code)]
    [InlineData("src/App/App.csproj", ChangeKind.Project)]
    [InlineData("Directory.Build.props", ChangeKind.Project)]
    [InlineData("Directory.Packages.props", ChangeKind.Project)]
    [InlineData("build/Custom.targets", ChangeKind.Project)]
    [InlineData("App.slnx", ChangeKind.Project)]
    [InlineData("App.sln", ChangeKind.Project)]
    [InlineData("App.slnf", ChangeKind.Project)]
    [InlineData("global.json", ChangeKind.Project)]
    [InlineData(".editorconfig", ChangeKind.Project)]
    [InlineData("Views/Main.xaml", ChangeKind.Xaml)]
    [InlineData("Views/Main.axaml", ChangeKind.Xaml)]
    [InlineData("Views/Main.paml", ChangeKind.Xaml)]
    [InlineData("Strings.resx", ChangeKind.Resx)]
    [InlineData("Strings.resw", ChangeKind.Resx)]
    [InlineData("Pages/Index.cshtml", ChangeKind.Razor)]
    [InlineData("Components/Home.razor", ChangeKind.Razor)]
    public void Classify_ForAKnownExtension_ReturnsItsKind(string path, ChangeKind expected) =>
        Assert.Equal(expected, WorkspaceSync.Classify(path));

    [Theory]
    [InlineData("notes.md")]
    [InlineData("assets/logo.png")]
    [InlineData("scripts/build.csx")]
    [InlineData("docs/diagram.svg")]
    public void Classify_ForAFileNoTerseSharpToolAnswersFor_ReturnsNull(string path) =>
        Assert.Null(WorkspaceSync.Classify(path));

    [Theory]
    [InlineData("obj/Debug/net10.0/Generated.cs")]
    [InlineData("bin/Release/App.csproj")]
    [InlineData(".git/index.lock")]
    [InlineData("artifacts/Order.cs")]
    [InlineData("TestResults/run.xaml")]
    [InlineData(".vs/App.csproj")]
    [InlineData(".idea/Order.cs")]
    [InlineData("node_modules/pack/App.csproj")]
    [InlineData("src/Order.cs.terse-1234.tmp")]
    [InlineData("src/.#Order.cs")]
    [InlineData("src/~$Order.cs")]
    [InlineData("src/Order.cs.orig")]
    [InlineData("src/Order.cs.swp")]
    public void Notice_ForAnIgnoredPath_EnqueuesNothing(string relative)
    {
        var sync = new WorkspaceSync(Root, default);

        sync.Notice(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(0, sync.PendingCount);
        Assert.False(sync.Reloading);
    }

    [Fact]
    public void Notice_ForTheSamePathManyTimes_EnqueuesItOnce()
    {
        var sync = new WorkspaceSync(Root, default);

        for (var index = 0; index < 100; index++)
            sync.Notice(Path.Combine(Root, "src", "Order.cs"));

        Assert.Equal(1, sync.PendingCount);
    }

    [Fact]
    public void Notice_BeyondThePendingCap_EscalatesToAFullReload()
    {
        var sync = new WorkspaceSync(Root, default);

        for (var index = 0; index <= WorkspaceSync.PendingCap; index++)
            sync.Notice(Path.Combine(Root, "src", Named(index)));

        Assert.True(sync.Reloading);
        Assert.Equal(WorkspaceSync.PendingCap, sync.PendingCount);
    }

    [Fact]
    public async Task SyncAsync_WithNothingPending_BumpsNoGeneration()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(0, 0, 0, 0), loaded.Sync.Generations);
    }

    [Fact]
    public async Task SyncAsync_ForATouchedFileWhoseContentIsUnchanged_BumpsNoGeneration()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(loaded.Files.OrderServicePath, DateTime.UtcNow.AddSeconds(1));
        loaded.Sync.Notice(loaded.Files.OrderServicePath);

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(0, 0, 0, 0), loaded.Sync.Generations);
        Assert.Equal(0, loaded.Sync.PendingCount);
    }

    [Fact]
    public async Task SyncAsync_ForAnExternalEdit_RefreshesTheDocumentAndBumpsCodeOnly()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);
        await AppendAsync(loaded.Files.OrderServicePath, "// ExternallyAdded\n");
        loaded.Sync.Notice(loaded.Files.OrderServicePath);

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(1, 0, 0, 0), loaded.Sync.Generations);
        Assert.Contains("ExternallyAdded", await TextOfAsync(loaded), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncAsync_ForAnExternalEdit_LeavesTheFileOnDiskByteIdentical()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);

        var written = new UTF8Encoding(false).GetBytes("namespace Fixture.Trading;\n\npublic sealed class Awkward\n{\n}\n");

        await File.WriteAllBytesAsync(AwkwardPath(loaded), written, TestContext.Current.CancellationToken);
        loaded.Sync.Notice(AwkwardPath(loaded));

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(written, await File.ReadAllBytesAsync(AwkwardPath(loaded), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncAsync_WithAPathHintAndNoNotice_StillCatchesTheChange()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);
        await AppendAsync(loaded.Files.OrderServicePath, "// CaughtByTheStampCheck\n");

        Assert.False(await loaded.SyncAsync(loaded.Files.OrderServicePath, TestContext.Current.CancellationToken));
        Assert.Equal(1, loaded.Sync.Generation(ChangeKind.Code));
        Assert.Contains("CaughtByTheStampCheck", await TextOfAsync(loaded), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncAsync_ForABurstAcrossKinds_BumpsEachKindExactlyOnce()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);
        await AppendAsync(loaded.Files.OrderServicePath, "// Burst\n");

        for (var index = 0; index < 50; index++)
            NoticeBurst(loaded);

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(1, 0, 1, 1), loaded.Sync.Generations);
        Assert.Equal(0, loaded.Sync.PendingCount);
    }

    [Fact]
    public async Task SyncAsync_ForANewSourceFileUnderAProject_AsksForAFullReload()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var added = Path.Combine(loaded.Files.ProjectDirectory, "AddedType.cs");

        await File.WriteAllTextAsync(added, "namespace Fixture.Trading;\n", TestContext.Current.CancellationToken);
        loaded.Sync.Notice(added);

        Assert.True(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncAsync_WithAPathHintForANewFileAndNoNotice_AsksForAFullReload()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var added = Path.Combine(loaded.Files.ProjectDirectory, "AddedType.cs");

        await File.WriteAllTextAsync(added, "namespace Fixture.Trading;\n", TestContext.Current.CancellationToken);

        Assert.True(await loaded.SyncAsync(added, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncAsync_ForASourceFileOutsideEveryProject_AsksForNoReload()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        var stray = Path.Combine(loaded.Files.Root, "scratch", "Stray.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        await File.WriteAllTextAsync(stray, "namespace Scratch;\n", TestContext.Current.CancellationToken);
        loaded.Sync.Notice(stray);

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(0, 0, 0, 0), loaded.Sync.Generations);
    }

    [Fact]
    public async Task SyncAsync_ForADeletedDocument_AsksForAFullReload()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        File.Delete(AwkwardPath(loaded));
        loaded.Sync.Notice(AwkwardPath(loaded));

        Assert.True(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncAsync_ForAProjectFileChange_AsksForAFullReload()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            loaded.Files.ProjectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <RootNamespace>Renamed</RootNamespace>\n  </PropertyGroup>\n</Project>\n",
            TestContext.Current.CancellationToken);
        loaded.Sync.Notice(loaded.Files.ProjectPath);

        Assert.True(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));

        await loaded.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceGenerations(1, 1, 0, 0), loaded.Sync.Generations);
    }

    [Fact]
    public async Task SyncAsync_ForAMarkupChange_BumpsXamlWithoutTouchingCode()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        loaded.Sync.Notice(Path.Combine(loaded.Files.ProjectDirectory, "Views", "OrderView.xaml"));

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(0, 0, 1, 0), loaded.Sync.Generations);
    }

    [Fact]
    public async Task SyncAsync_ForAResourceChange_BumpsResxWithoutTouchingCode()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        loaded.Sync.Notice(Path.Combine(loaded.Files.ProjectDirectory, "Strings.resx"));

        Assert.False(await loaded.SyncAsync(null, TestContext.Current.CancellationToken));
        Assert.Equal(new WorkspaceGenerations(0, 0, 0, 1), loaded.Sync.Generations);
    }

    [Fact]
    public async Task ReloadAsync_CarriesTheCountersForwardAndBumpsOnlyCodeAndProject()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        await loaded.MaterialiseAsync(TestContext.Current.CancellationToken);
        await AppendAsync(loaded.Files.OrderServicePath, "// BeforeTheReload\n");
        loaded.Sync.Notice(loaded.Files.OrderServicePath);
        await loaded.SyncAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceGenerations(1, 0, 0, 0), loaded.Sync.Generations);

        await loaded.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new WorkspaceGenerations(2, 1, 0, 0), loaded.Sync.Generations);
    }

    [Fact]
    public async Task WatchState_WhenTheWatcherIsDisabled_IsOff()
    {
        using var loaded = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WatchState.Off, loaded.Sync.State);
        Assert.Equal(0, loaded.Sync.Gaps);
    }

    private static void NoticeBurst(TemporaryWorkspace loaded)
    {
        loaded.Sync.Notice(loaded.Files.OrderServicePath);
        loaded.Sync.Notice(Path.Combine(loaded.Files.ProjectDirectory, "Views", "OrderView.xaml"));
        loaded.Sync.Notice(Path.Combine(loaded.Files.ProjectDirectory, "Strings.resx"));
    }

    private static string AwkwardPath(TemporaryWorkspace loaded) =>
        Path.Combine(loaded.Files.ProjectDirectory, "Awkward.cs");

    private static async Task<string> TextOfAsync(TemporaryWorkspace loaded)
    {
        var text = await loaded.Document("OrderService.cs").GetTextAsync(TestContext.Current.CancellationToken);

        return text.ToString();
    }

    private static async Task AppendAsync(string path, string addition)
    {
        var existing = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(path, existing + addition, TestContext.Current.CancellationToken);
    }

    private static string Named(int index) =>
        "Type" + index.ToString(CultureInfo.InvariantCulture) + ".cs";
}
