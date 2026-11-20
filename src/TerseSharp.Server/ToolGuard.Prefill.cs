namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private static string? Prefilled(string command)
    {
        if (Resolved(command) is not { } resolved)
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pipeline in Pipelines(resolved))
        {
            if (Sequential(pipeline.Lead))
                Recorded(pipeline.Text, values);
        }

        return values.Count > 0 && !values.Keys.Any(name => Rebound(command, name)) ? resolved : null;
    }

    private static GuardVerdict? Spelled(string command, string? cwd) => Prefilled(command) switch
    {
        { } prefilled => Unrolling(prefilled, cwd, environed: true),
        _ when Environed(command) is { } filled && Unrolling(filled, cwd, environed: true) is { Denied: false } literal => literal,
        _ => null,
    };
}
