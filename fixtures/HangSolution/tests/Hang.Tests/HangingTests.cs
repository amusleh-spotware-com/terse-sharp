using Xunit;

namespace Hang.Tests;

public sealed class HangingTests
{
    [Fact]
    public async Task NeverFinishes() => await Task.Delay(TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);
}
