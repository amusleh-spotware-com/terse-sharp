
namespace TerseSharp.Server;

public readonly record struct WatchedRun(Func<Task<string>> Run)
{
    public static WatchedRun From(Func<Task<string>> run) => new(run);

    public async Task<string> InvokeAsync()
    {
        var before = EditPulse.Changed;
        var verdict = await Run().ConfigureAwait(false);

        return StaleRun.Annotated(verdict, before, EditPulse.Changed);
    }
}
