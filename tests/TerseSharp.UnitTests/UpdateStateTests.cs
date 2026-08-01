using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class UpdateStateTests
{
    [Fact]
    public void Render_AndTryParse_RoundTripTheMomentAndTheVersion()
    {
        var moment = new DateTimeOffset(2026, 8, 1, 16, 24, 43, TimeSpan.FromHours(3));
        var state = new UpdateState(moment, new ReleaseVersion(0, 15, 2, false));

        Assert.True(UpdateState.TryParse(state.Render(), out var parsed));
        Assert.Equal(moment.ToUniversalTime(), parsed.CheckedUtc);
        Assert.Equal(new ReleaseVersion(0, 15, 2, false), parsed.Latest);
    }

    [Fact]
    public void Render_WritesTheMomentInUtcRoundTripFormat()
    {
        var state = new UpdateState(new DateTimeOffset(2026, 8, 1, 16, 24, 43, TimeSpan.Zero), new ReleaseVersion(1, 2, 3, false));

        Assert.Equal("1 2026-08-01T16:24:43.0000000+00:00 1.2.3", state.Render());
    }

    [Fact]
    public void TryParse_KeepsTheMomentWhenTheLatestVersionIsUnknown()
    {
        var state = new UpdateState(DateTimeOffset.UtcNow, null);

        Assert.True(UpdateState.TryParse(state.Render(), out var parsed));
        Assert.Null(parsed.Latest);
        Assert.Equal(state.CheckedUtc.ToUniversalTime(), parsed.CheckedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-08-01T16:24:43.0000000+00:00 1.2.3")]
    [InlineData("1 not-a-moment 1.2.3")]
    [InlineData("1 2026-08-01T16:24:43.0000000+00:00")]
    public void TryParse_RefusesALineItCannotRead(string text) =>
        Assert.False(UpdateState.TryParse(text, out _));

    [Fact]
    public void TryParse_KeepsTheMomentWhenTheRecordedVersionIsJunk()
    {
        Assert.True(UpdateState.TryParse("1 2026-08-01T16:24:43.0000000+00:00 main", out var parsed));
        Assert.Null(parsed.Latest);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-01T16:24:43.0000000+00:00", CultureInfo.InvariantCulture),
            parsed.CheckedUtc);
    }
}
