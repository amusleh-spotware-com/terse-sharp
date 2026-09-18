using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(EditPulseCollection))]
public sealed class EditPulseTests
{
    [Theory]
    [InlineData("release-notes.md", false)]
    [InlineData("RELEASE-NOTES.MD", false)]
    [InlineData("docs/plan.markdown", false)]
    [InlineData("src/OrderService.cs", true)]
    [InlineData("src/Fixture.Trading.csproj", true)]
    [InlineData("src/Strings.resx", true)]
    [InlineData("src/BannedSymbols.txt", true)]
    [InlineData("src/Home.razor", true)]
    public void ChangesABuild_TreatsOnlyAMarkdownWorkingNoteAsImmaterial(string path, bool counted) =>
        Assert.Equal(counted, EditPulse.ChangesABuild(path));

    [Fact]
    public void Bump_ForAMarkdownWorkingNote_MovesTheWriteCounterButNotTheOneStaleReads()
    {
        var changed = EditPulse.Changed;
        var material = EditPulse.Material;

        EditPulse.Bump("IMPROVEMENTS.md");

        Assert.Equal(changed + 1, EditPulse.Changed);
        Assert.Equal(material, EditPulse.Material);
    }

    [Fact]
    public void Bump_ForADocumentABuildReads_MovesBothCounters()
    {
        var changed = EditPulse.Changed;
        var material = EditPulse.Material;

        EditPulse.Bump("src/OrderService.cs");

        Assert.Equal(changed + 1, EditPulse.Changed);
        Assert.Equal(material + 1, EditPulse.Material);
    }

    [Fact]
    public void Since_NamesTheDocumentsWrittenAfterTheWatermark_SoTheStaleLineIsActionable()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-stale-root");
        var before = EditPulse.Material;

        EditPulse.Bump(Path.Combine(root, "src", "OrderService.cs"));

        var note = TerseSharp.Server.StaleRun.Note(before, EditPulse.Material, [root]);

        Assert.NotNull(note);
        Assert.Contains("OrderService.cs)", note, StringComparison.Ordinal);
        Assert.DoesNotContain(root, note, StringComparison.Ordinal);
    }
}

[CollectionDefinition(nameof(EditPulseCollection), DisableParallelization = true)]
public sealed class EditPulseCollection;
