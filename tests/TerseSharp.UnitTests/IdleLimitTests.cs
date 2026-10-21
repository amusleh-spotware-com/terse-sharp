using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class IdleLimitTests
{
    [Fact]
    public void Resolve_WithNothingSet_UsesTheSixtyMinuteDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(60), IdleLimit.Resolve(null, null));

    [Fact]
    public void Resolve_WithZero_TurnsTheSweepOff() =>
        Assert.Equal(TimeSpan.Zero, IdleLimit.Resolve(0, null));

    [Fact]
    public void Resolve_WithZeroInTheEnvironment_TurnsTheSweepOff() =>
        Assert.Equal(TimeSpan.Zero, IdleLimit.Resolve(null, "0"));

    [Fact]
    public void Resolve_PrefersTheOptionOverTheEnvironment() =>
        Assert.Equal(TimeSpan.FromMinutes(3), IdleLimit.Resolve(3, "60"));

    [Fact]
    public void Resolve_WithAnUnparsableEnvironment_FallsBackToTheDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(60), IdleLimit.Resolve(null, "soon"));

    [Fact]
    public void Resolve_WithANegativeEnvironment_FallsBackToTheDefault() =>
        Assert.Equal(TimeSpan.FromMinutes(60), IdleLimit.Resolve(null, "-5"));

    [Fact]
    public void Resolve_WithOnlyAConfiguredValue_UsesIt() =>
        Assert.Equal(TimeSpan.FromMinutes(45), IdleLimit.Resolve(null, null, 45));

    [Fact]
    public void Resolve_PrefersTheEnvironmentOverTheConfiguredValue() =>
        Assert.Equal(TimeSpan.FromMinutes(7), IdleLimit.Resolve(null, "7", 45));

    [Fact]
    public void Configured_ReadsTheWorkspaceIdleMinutes() =>
        Assert.Equal(90, IdleLimit.Configured("""{ "workspace": { "idleMinutes": 90 } }"""));

    [Fact]
    public void Configured_WithZero_KeepsTheCompilations() =>
        Assert.Equal(0, IdleLimit.Configured("""{ "workspace": { "idleMinutes": 0 } }"""));

    [Fact]
    public void Configured_WithANegativeTextOrMissingValue_IsNotConfigured()
    {
        Assert.Null(IdleLimit.Configured("""{ "workspace": { "idleMinutes": -1 } }"""));
        Assert.Null(IdleLimit.Configured("""{ "workspace": { "idleMinutes": "soon" } }"""));
        Assert.Null(IdleLimit.Configured("""{ "tools": {} }"""));
        Assert.Null(IdleLimit.Configured("not json"));
    }

    [Fact]
    public async Task ConfiguredAsync_OverAChain_LetsTheNearerFileWin()
    {
        var root = Directory.CreateTempSubdirectory("terse-idle-").FullName;

        try
        {
            var outer = Path.Combine(root, "outer.json");
            var nearer = Path.Combine(root, "nearer.json");
            await File.WriteAllTextAsync(outer, """{ "workspace": { "idleMinutes": 90 } }""", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(nearer, """{ "workspace": { "idleMinutes": 30 } }""", TestContext.Current.CancellationToken);

            Assert.Equal(30, await IdleLimit.ConfiguredAsync([outer, nearer], TestContext.Current.CancellationToken));
            Assert.Equal(90, await IdleLimit.ConfiguredAsync([outer, Path.Combine(root, "absent.json")], TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
