using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class RazorRegistrationsHostServiceTests
{
    [Theory]
    [InlineData("NavigationManager")]
    [InlineData("IJSRuntime")]
    [InlineData("ILogger")]
    [InlineData("IConfiguration")]
    [InlineData("PersistentComponentState")]
    [InlineData("IServiceProvider")]
    [InlineData("IWebHostEnvironment")]
    public void IsHostProvided_ForAServiceTheHostAlwaysRegisters_IsTrue(string name) =>
        Assert.True(RazorRegistrations.IsHostProvided(name));

    [Theory]
    [InlineData("IMemoryCache")]
    [InlineData("IDistributedCache")]
    [InlineData("IStringLocalizer")]
    [InlineData("IHttpClientFactory")]
    [InlineData("HttpClient")]
    [InlineData("AuthenticationStateProvider")]
    public void IsHostProvided_ForAServiceThatNeedsAnExplicitAddCall_IsFalse(string name) =>
        Assert.False(RazorRegistrations.IsHostProvided(name));

    [Theory]
    [InlineData("IOrderRepository")]
    [InlineData("ITradingGateway")]
    public void IsHostProvided_ForAnApplicationService_IsFalse(string name) =>
        Assert.False(RazorRegistrations.IsHostProvided(name));
}
