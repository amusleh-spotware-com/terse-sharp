using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Server;

public readonly record struct ToolLatency(int Calls, double ResolveMs, double SyncMs, double ActionMs);

public sealed class ToolContext(WorkspaceRegistry registry, bool readOnly, ToolSurface surface = default) : IDisposable
{
    private Task ready = Task.CompletedTask;

    public WorkspaceRegistry Registry { get; } = registry;

    public bool ReadOnly { get; } = readOnly;

    public ToolSurface Surface { get; } = surface;

    public Func<CancellationToken, Task>? ToolsChanged { get; set; }

    public string? PreloadFailure { get; private set; }

    public void BeginPreload(string target, CancellationToken cancellationToken)
    {
        Preload(Task.Run(() => Registry.LoadAsync(target, cancellationToken), cancellationToken));

        _ = AnnouncedAsync(WorkspaceMarkup.Every, cancellationToken);
    }

    public Task ReadyAsync() => ready;

    internal void Preload(Task load) => ready = ObserveAsync(load);

    public async Task<string> WithSymbolAsync(
        string? workspace,
        string? symbolId,
        Func<LoadedWorkspace, ISymbol, Task<string>> action,
        CancellationToken cancellationToken,
        string? path = null,
        Func<LoadedWorkspace, string?>? guard = null,
        bool typesOnly = false,
        bool referenced = false,
        Func<LoadedWorkspace, TerseError, string>? unresolved = null)
    {
        if (symbolId is not { Length: > 0 } requested)
            return Errors.Blank("symbolId", "symbol").Render();

        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = await ResolveAsync(workspace, path, semantic: true, cancellationToken).ConfigureAwait(false);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            if (guard?.Invoke(lease.Workspace) is { } refused)
                return refused;

            return await AttributedAsync(lease.Workspace, async () =>
            {
                var symbol = await SymbolLookup.ResolveAsync(lease.Workspace, requested, path, cancellationToken, typesOnly, referenced).ConfigureAwait(false);

                if (symbol.IsOk)
                    return await action(lease.Workspace, symbol.Value!).ConfigureAwait(false);

                return unresolved is null ? symbol.Error!.Render() : unresolved(lease.Workspace, symbol.Error!);
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    public Task<string> WithWorkspace(
        string? workspace,
        string? pathHint,
        Func<LoadedWorkspace, string> action,
        bool semantic = true,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(workspace, pathHint, loaded => Task.FromResult(action(loaded)), semantic, cancellationToken);

    public async Task<string> WithWorkspaceAsync(
    string? workspace,
    string? pathHint,
    Func<LoadedWorkspace, Task<string>> action,
    bool semantic = true,
    CancellationToken cancellationToken = default)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var resolved = await ResolveAsync(workspace, pathHint, semantic, cancellationToken).ConfigureAwait(false);

            if (!resolved.IsOk)
                return resolved.Error!.Render();

            using var lease = resolved.Value!;

            return semantic
                ? await AttributedAsync(lease.Workspace, () => action(lease.Workspace)).ConfigureAwait(false)
                : await action(lease.Workspace).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public string? RejectWrite() => ReadOnly
        ? new TerseError(TerseErrorCode.ReadOnly, "the server is running with --read-only", "restart without --read-only").Render()
        : null;

    public async Task<string> WithTargetAsync(
        string? workspace,
        string? pathHint,
        Func<WorkspaceTarget, Task<string>> action,
        bool changed = false,
        bool tests = false,
        CancellationToken cancellationToken = default)
    {
        await ready.ConfigureAwait(false);

        return await ToolBoundary.RunAsync(async () =>
        {
            var target = await TargetAsync(workspace, pathHint, changed, tests, cancellationToken).ConfigureAwait(false);

            return target.IsOk ? await action(target.Value!).ConfigureAwait(false) : target.Error!.Render();
        }).ConfigureAwait(false);
    }

    private async Task<Result<WorkspaceTarget>> TargetAsync(
        string? workspace,
        string? pathHint,
        bool changed,
        bool tests,
        CancellationToken cancellationToken)
    {
        if (!changed)
            return Target(workspace, pathHint, changed: false, tests);

        var resolved = Registry.Resolve(workspace, pathHint);

        if (!resolved.IsOk)
            return Result.Fail<WorkspaceTarget>(resolved.Error!);

        var synced = await SyncedAsync(resolved.Value!, workspace, pathHint, cancellationToken).ConfigureAwait(false);

        if (!synced.IsOk)
            return Result.Fail<WorkspaceTarget>(synced.Error!);

        using var lease = synced.Value!;

        return Result.Ok(Described(lease.Workspace, changed: true, tests));
    }

    private Result<WorkspaceTarget> Target(string? workspace, string? pathHint, bool changed, bool tests = false)
    {
        var resolved = Registry.Resolve(workspace, pathHint);

        if (!resolved.IsOk)
            return Result.Fail<WorkspaceTarget>(resolved.Error!);

        using var lease = resolved.Value!;

        return Result.Ok(Described(lease.Workspace, changed, tests));
    }

    private static WorkspaceTarget Described(LoadedWorkspace loaded, bool changed, bool tests = false) => new(
            loaded.SolutionPath,
            loaded.Root,
            ProjectPaths(loaded),
            changed ? ChangedTestSelection.Select(loaded) : default,
            tests ? TestProjectsOf(loaded) : default,
            SelfBuild.RunningAssemblyOf(loaded));

    public void Dispose() => Registry.Dispose();

    private async Task ObserveAsync(Task load)
    {
        try
        {
            await load.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PreloadFailure = "the workspace preload was cancelled";
        }
        catch (Exception exception)
        {
            PreloadFailure = string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private async Task<Result<WorkspaceLease>> ResolveAsync(
        string? workspace,
        string? pathHint,
        bool semantic,
        CancellationToken cancellationToken)
    {
        var resolved = Registry.Resolve(workspace, pathHint, semantic);

        if (!semantic || !resolved.IsOk)
            return resolved;

        return await SyncedAsync(resolved.Value!, workspace, pathHint, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<WorkspaceLease>> SyncedAsync(
        WorkspaceLease lease,
        string? workspace,
        string? pathHint,
        CancellationToken cancellationToken)
    {
        bool reload;

        try
        {
            reload = await lease.Workspace.Sync.SyncAsync(lease.Workspace, pathHint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();

            throw;
        }

        if (!reload)
            return Result.Ok(lease);

        var stale = lease.Workspace;

        lease.Dispose();

        await Registry.RefreshAsync(stale, cancellationToken).ConfigureAwait(false);

        return Registry.Resolve(workspace, pathHint);
    }

    public bool OutsideEveryWorkspace(string path) =>
            Path.IsPathRooted(path)
            && Registry.All().All(loaded => !PathBoundary.Contains(loaded.Root, Path.GetFullPath(path)));

    private static ImmutableArray<string> ProjectPaths(LoadedWorkspace workspace)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in workspace.Solution.Projects)
        {
            if (project.FilePath is { Length: > 0 } path)
                paths.Add(path);
        }

        return [.. paths.Order(StringComparer.Ordinal)];
    }

    private static async Task<string> AttributedAsync(LoadedWorkspace loaded, Func<Task<string>> action)
    {
        if (loaded.CompilationsRealized)
            return await action().ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var answer = await action().ConfigureAwait(false);

        return loaded.CompilationsRealized
            && !answer.StartsWith("ERROR", StringComparison.Ordinal)
            && loaded.TakeRealizedNotice()
            ? answer + string.Create(
                CultureInfo.InvariantCulture,
                $"\ncompilations=realized in {stopwatch.ElapsedMilliseconds}ms (once per load, not per call)")
            : answer;
    }

    public async Task<ToolLatency> MeasureAsync(int calls, CancellationToken cancellationToken)
    {
        (long Resolve, long Sync, long Action) total = default;

        for (var call = 0; call < calls; call++)
        {
            var sample = await SampleAsync(cancellationToken).ConfigureAwait(false);

            total = (total.Resolve + sample.Resolve, total.Sync + sample.Sync, total.Action + sample.Action);
        }

        return new ToolLatency(calls, Average(total.Resolve, calls), Average(total.Sync, calls), Average(total.Action, calls));
    }

    private async Task<(long Resolve, long Sync, long Action)> SampleAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var found = Registry.Resolve(null, null, semantic: true);
        var resolve = stopwatch.ElapsedTicks;

        if (!found.IsOk)
            return (resolve, 0, 0);

        stopwatch.Restart();
        var synced = await SyncedAsync(found.Value!, null, null, cancellationToken).ConfigureAwait(false);
        var sync = stopwatch.ElapsedTicks;

        if (!synced.IsOk)
            return (resolve, sync, 0);

        using var lease = synced.Value!;

        stopwatch.Restart();
        _ = lease.Workspace.Solution.ProjectIds.Count;

        return (resolve, sync, stopwatch.ElapsedTicks);
    }

    private static double Average(long ticks, int calls) =>
        calls is 0 ? 0 : ticks * 1000d / (Stopwatch.Frequency * calls);

    public async Task<PhaseLatency> MeasurePhasesAsync(CancellationToken cancellationToken)
    {
        var found = Registry.Resolve(null, null, semantic: true);

        if (!found.IsOk)
            return new PhaseLatency(string.Empty, 0, 0, 0, 0);

        var synced = await SyncedAsync(found.Value!, null, null, cancellationToken).ConfigureAwait(false);

        if (!synced.IsOk)
            return new PhaseLatency(string.Empty, 0, 0, 0, 0);

        using var lease = synced.Value!;

        return await PhaseProbe.MeasureAsync(lease.Workspace, cancellationToken).ConfigureAwait(false);
    }

    public WorkspaceMarkup Served() => ToolProfile.Served(Registry);

    public async Task AnnounceAsync(WorkspaceMarkup before, CancellationToken cancellationToken)
    {
        if (!Surface.MarkupDerived || ToolsChanged is not { } notify)
            return;

        if (await ToolProfile.ServedAsync(Registry, cancellationToken).ConfigureAwait(false) == before)
            return;

        await notify(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkspaceMarkup> ServedAsync(CancellationToken cancellationToken)
    {
        await ready.WaitAsync(cancellationToken).ConfigureAwait(false);

        return await ToolProfile.ServedAsync(Registry, cancellationToken).ConfigureAwait(false);
    }

    private async Task AnnouncedAsync(WorkspaceMarkup before, CancellationToken cancellationToken)
    {
        await ReadyAsync().ConfigureAwait(false);

        try
        {
            await AnnounceAsync(before, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine("terse: tools/list_changed could not be sent: " + exception.Message);
        }
    }

    private static ImmutableArray<string> TestProjectsOf(LoadedWorkspace loaded) => loaded.Load.Failures.Count is 0
        ? TestScope.TestProjectsOf(loaded.Solution, loaded.Load.TargetFramework is null or { Length: 0 })
        : default;
}

public readonly record struct PhaseLatency(string Document, double RealizeMs, double OutlineMs, double GateMs, double DiffMs);
