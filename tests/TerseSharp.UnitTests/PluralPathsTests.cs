using System.Globalization;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class PluralPathsTests
{
    private static readonly string[] Eleven = [.. Enumerable.Range(1, 11).Select(index => string.Create(CultureInfo.InvariantCulture, $"f{index:00}.cs"))];

    [Fact]
    public void Spill_WithElevenEntries_AnswersTheFirstTenAndKeepsTheEleventhVerbatim()
    {
        var spilled = PluralPaths.Spill(null, Eleven, "paths");

        Assert.True(spilled.IsOk);
        Assert.Equal(Eleven[..10], spilled.Value.Taken.ToArray());
        Assert.Equal("f11.cs", Assert.Single(spilled.Value.Overflow));
    }

    [Fact]
    public void Spill_WithABlankEntryPastTheTenth_IsRefusedRatherThanSpilled()
    {
        var spilled = PluralPaths.Spill(null, [.. Eleven[..10], ""], "paths");

        Assert.False(spilled.IsOk);
        Assert.Contains("'paths' carries a blank entry", spilled.Error!.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Combine_WithElevenEntries_StillRefusesForTheToolsThatDoNotSpill()
    {
        var combined = PluralPaths.Combine(null, Eleven, "queries");

        Assert.False(combined.IsOk);
        Assert.Contains("11 paths were requested", combined.Error!.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void Trailer_NamesTheOverflowAsAReadyCallCarryingTheArgumentsThatChangeItsMeaning() =>
        Assert.Equal(
            "\nPARTIAL 10 of 11 paths answered, 10 per call - the rest, with this call's other arguments:\nnext: read_text paths=[\"docs\\\\a \\\"b\\\".md\"] ref=\"HEAD\"",
            PluralPaths.Trailer("read_text", "paths", 10, ["docs\\a \"b\".md"], PluralPaths.Argument("ref", "HEAD")));

    [Fact]
    public void Trailer_WithNoOverflow_IsEmpty() =>
        Assert.Equal(string.Empty, PluralPaths.Trailer("analyze", "paths", 10, []));
}
