using System.Globalization;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class CrashLogTests
{
    private const string LogPath = "/home/u/.terse/crash.log";

    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Describe_ACrashTwoDaysOld_NamesItsTimeAndTheFileToRead() =>
        Assert.Equal(
            "WARNING terse crashed at 2026-11-18T12:00:00Z - read_text /home/u/.terse/crash.log for the exception, the memory and the version it ran",
            CrashLog.Describe(LogPath, Now.AddDays(-2), Now));

    [Fact]
    public void Describe_ACrashOlderThanAWeek_SaysNothing() => Assert.Null(CrashLog.Describe(LogPath, Now.AddDays(-8), Now));

    [Fact]
    public void Describe_NoCrashLog_SaysNothing() => Assert.Null(CrashLog.Describe(LogPath, null, Now));

    [Fact]
    public void Record_CarriesTheTimeProcessVersionMemoryAndTheWholeException()
    {
        var record = CrashLog.Record(Now, "0.85.0", terminating: true, workingSet: 3L * 1024 * 1024 * 1024, new InvalidOperationException("boom"));

        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"2026-11-20T12:00:00.0000000Z pid={Environment.ProcessId} terse=0.85.0 terminating=True workingSetMB=3072\nSystem.InvalidOperationException: boom"), record, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Append_TwoRecords_LandInOrderInTheCrashLogUnderTheHome()
    {
        var home = Directory.CreateTempSubdirectory("terse-crash-").FullName;

        try
        {
            CrashLog.Append(home, "first\n");
            CrashLog.Append(home, "second\n");

            Assert.Equal(Path.Join(home, ".terse", "crash.log"), CrashLog.PathIn(home));
            Assert.Equal("first\nsecond\n", await File.ReadAllTextAsync(CrashLog.PathIn(home), TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
