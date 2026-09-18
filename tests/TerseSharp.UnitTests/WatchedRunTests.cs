using System.Globalization;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

[Collection(nameof(EditPulseCollection))]
public sealed class WatchedRunTests
{
    private const string Green = "build ok  errors=0 warnings=0  elapsedMs=1";

    [Fact]
    public async Task InvokeAsync_WhenADocumentChangesWhileTheRunIsInFlight_AnnotatesTheVerdictStale()
    {
        var run = WatchedRun.From(() =>
        {
            EditPulse.Bump(2);

            return Task.FromResult(Green);
        });

        var verdict = await run.InvokeAsync();

        Assert.StartsWith(Green + "\n", verdict, StringComparison.Ordinal);
        Assert.Contains("document(s) changed after this run started", verdict, StringComparison.Ordinal);
        Assert.True(Counted(verdict) >= 2, verdict);
    }

    [Fact]
    public async Task InvokeAsync_NeverReplacesTheVerdictItWraps()
    {
        var run = WatchedRun.From(() => Task.FromResult(Green));

        Assert.StartsWith(Green, await run.InvokeAsync(), StringComparison.Ordinal);
    }

    private static int Counted(string verdict)
    {
        var digits = verdict.AsSpan(verdict.IndexOf("STALE ", StringComparison.Ordinal) + 6);

        return int.Parse(digits[..digits.IndexOf(' ')], CultureInfo.InvariantCulture);
    }
}
