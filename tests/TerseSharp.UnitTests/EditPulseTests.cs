using TerseSharp.Core;
using TerseSharp.Server;

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

    [Fact]
    public void StaleNote_WhenEveryDocumentWrittenSinceTheRunStartedLiesOutsideItsRoots_IsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-stale-root");
        var before = EditPulse.Material;

        EditPulse.Bump(Path.Combine(Path.GetTempPath(), "terse-stale-elsewhere", "Other.cs"));
        EditPulse.Bump(Path.Combine(root, ".claude", "worktrees", "agent-x", "src", "Sibling.cs"));

        Assert.Null(StaleRun.Note(before, EditPulse.Material, [root]));
    }

    [Fact]
    public void StaleNote_WhenADocumentInsideTheRunsRootChangesBesideOneOutside_CountsAndNamesOnlyTheInsideOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-stale-root");
        var before = EditPulse.Material;

        EditPulse.Bump(Path.Combine(Path.GetTempPath(), "terse-stale-elsewhere", "Other.cs"));
        EditPulse.Bump(Path.Combine(root, "src", "Inside.cs"));

        var note = StaleRun.Note(before, EditPulse.Material, [root]);

        Assert.NotNull(note);
        Assert.StartsWith("STALE 1 document(s) changed after this run started (src", note, StringComparison.Ordinal);
        Assert.DoesNotContain("Other.cs", note, StringComparison.Ordinal);
    }
}

[CollectionDefinition(nameof(EditPulseCollection), DisableParallelization = true)]
public sealed class EditPulseCollection;
