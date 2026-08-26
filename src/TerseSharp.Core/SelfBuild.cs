using System.Reflection;

namespace TerseSharp.Core;

public static class SelfBuild
{
    private static readonly string? Running = Located();

    public static string? RunningAssemblyOf(LoadedWorkspace workspace) =>
        Running is { Length: > 0 } running && Builds(Outputs(workspace), running) ? running : null;

    public static bool Builds(IEnumerable<string> outputs, string running)
    {
        var full = Path.GetFullPath(running);

        foreach (var output in outputs)
        {
            if (output is { Length: > 0 } && string.Equals(Path.GetFullPath(output), full, PathBoundary.Comparison))
                return true;
        }

        return false;
    }

    public static TerseError? Refusal(WorkspaceTarget target, bool writes) => writes && target.RunningAssembly is { Length: > 0 } running
            ? Errors.Invalid(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this solution builds the assembly this server is running from ({PositionFormat.Relative(target.Root, running)}), so the build would have to overwrite a file this process holds open and MSBuild answers MSB3026"),
                "copy the tool directory somewhere outside the solution and run the probe from the copy; project=, configuration= and noBuild=true all scope the call away from that output and are never refused")
            : null;

    private static IEnumerable<string> Outputs(LoadedWorkspace workspace)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            if (project.OutputFilePath is { Length: > 0 } output)
                yield return output;
        }
    }

    private static string? Located() =>
        Assembly.GetEntryAssembly()?.Location is { Length: > 0 } location ? Path.GetFullPath(location) : null;
}
