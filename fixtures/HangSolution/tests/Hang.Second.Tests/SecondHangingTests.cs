using Xunit;

namespace Hang.Second.Tests;

public sealed class SecondHangingTests
{
    [Fact]
    public async Task AlsoNeverFinishes() => await Task.Delay(TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);
}
