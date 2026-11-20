namespace TerseSharp.Core;

public readonly record struct WorkspaceEviction(DateTimeOffset EvictedUtc, string EvictedBy, int Capacity)
{
    public string Describe(DateTimeOffset nowUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"evicted {Aged(nowUtc - EvictedUtc)} ago by the load of {EvictedBy} - at most {Capacity} workspace(s) stay loaded (--max-workspaces or TERSE_MAX_WORKSPACES raises it), so this load paid in full again");

    private static string Aged(TimeSpan elapsed) => elapsed switch
    {
        { TotalHours: >= 1 } => elapsed.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + "h",
        { TotalMinutes: >= 1 } => elapsed.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m",
        _ => Math.Max(elapsed.TotalSeconds, 0).ToString("0", CultureInfo.InvariantCulture) + "s",
    };
}
