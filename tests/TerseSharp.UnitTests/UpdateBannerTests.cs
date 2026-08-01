using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class UpdateBannerTests : IDisposable
{
    public UpdateBannerTests() => UpdateBanner.Take();

    [Fact]
    public void Take_HandsThePublishedNoticeToTheFirstCallerOnly()
    {
        UpdateBanner.Publish("UPDATE terse 0.1.0 -> 0.2.0");

        Assert.Equal("UPDATE terse 0.1.0 -> 0.2.0", UpdateBanner.Take());
        Assert.Null(UpdateBanner.Take());
    }

    [Fact]
    public void Take_WhenNothingWasPublished_ReturnsNothing() => Assert.Null(UpdateBanner.Take());

    [Fact]
    public void Publish_WithNoNotice_LeavesNothingToTake()
    {
        UpdateBanner.Publish(null);

        Assert.Null(UpdateBanner.Take());
    }

    public void Dispose() => UpdateBanner.Take();
}
