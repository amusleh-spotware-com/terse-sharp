
namespace TerseSharp.Server;

public readonly record struct WatchedRun(Func<Task<string>> Run, Func<IReadOnlyList<string>>? Roots = null)
{
    public static WatchedRun From(Func<Task<string>> run) => new(run);

    public static WatchedRun From(Func<Task<string>> run, Func<IReadOnlyList<string>> roots) => new(run, roots);

    public async Task<string> InvokeAsync()
    {
        var before = EditPulse.Material;
        var verdict = await Run().ConfigureAwait(false);

        return StaleRun.Annotated(verdict, before, EditPulse.Material, Roots?.Invoke());
    }
}
