using System.Diagnostics;

namespace TerseSharp.Core;

public readonly record struct ActiveRun(string Tool, string Detail, long StartedTicks);

public static class ActiveRuns
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ActiveRun> Running = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryEnter(string solutionPath, string tool, string detail, out ActiveRun holder)
    {
        lock (Gate)
        {
            if (Running.TryGetValue(solutionPath, out holder))
                return false;

            holder = new ActiveRun(tool, detail, Stopwatch.GetTimestamp());
            Running[solutionPath] = holder;

            return true;
        }
    }

    public static void Leave(string solutionPath)
    {
        lock (Gate)
            Running.Remove(solutionPath);
    }

    public static TimeSpan Age(ActiveRun holder) => Stopwatch.GetElapsedTime(holder.StartedTicks);
}
