using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Server;

internal static class PhaseProbe
{
    public static async Task<PhaseLatency> MeasureAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        if (Widest(workspace) is not { FilePath: { Length: > 0 } path } document)
            return new PhaseLatency(string.Empty, 0, 0, 0);

        var outline = await TimedAsync(() => OutlineService.FileAsync(workspace, path, true, "short", false, cancellationToken)).ConfigureAwait(false);
        var gate = await TimedAsync(() => EditGate.ApplyAsync(
            workspace,
            workspace.Solution,
            [document.Id],
            new EditOptions("doctor", DryRun: true, AllowErrors: false),
            cancellationToken)).ConfigureAwait(false);
        var diff = await TimedAsync(() => GitRunner.ReadAsync(workspace.Root, ["diff", "--stat"], cancellationToken)).ConfigureAwait(false);

        return new PhaseLatency(PositionFormat.Relative(workspace.Root, path), outline, gate, diff);
    }

    private static async Task<double> TimedAsync<TResult>(Func<Task<TResult>> phase)
    {
        var stopwatch = Stopwatch.StartNew();

        _ = await phase().ConfigureAwait(false);

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static Document? Widest(LoadedWorkspace workspace)
    {
        Document? widest = null;
        var longest = -1L;

        foreach (var document in workspace.Solution.Projects.SelectMany(project => project.Documents))
        {
            var length = Length(workspace.Root, document.FilePath);

            if (length <= longest)
                continue;

            (longest, widest) = (length, document);
        }

        return widest;
    }

    private static long Length(string root, string? path) =>
    path is { Length: > 0 } file
    && file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
    && !GeneratedCode.IsGenerated(root, file)
    && File.Exists(file)
        ? new FileInfo(file).Length
        : -1;
}
