using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public sealed class WorkspaceSync(string root, WorkspaceGenerations seed) : IDisposable
{
    public const int PendingCap = 5_000;

    private static readonly string[] CodeExtensions = [".cs"];

    private static readonly string[] ProjectExtensions =
        [".csproj", ".props", ".targets", ".sln", ".slnx", ".slnf"];

    private static readonly string[] ProjectNames = ["global.json", ".editorconfig"];

    private static readonly string[] MarkupExtensions = [".xaml", ".axaml", ".paml"];

    private static readonly string[] ResourceExtensions = [".resx", ".resw"];

    private static readonly string[] RazorExtensions = [".razor", ".cshtml"];

    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileStamp> stamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock generationGate = new();

    private WorkspaceGenerations generations = seed;
    private int reloadRequired;
    private int gaps;
    private long lastSyncMilliseconds;

    public WatchState State { get; private set; }

    public string? StateDetail { get; private set; }

    public int PendingCount => pending.Count;

    public int Gaps => Volatile.Read(ref gaps);

    public long LastSyncMilliseconds => Volatile.Read(ref lastSyncMilliseconds);

    public bool Reloading => Volatile.Read(ref reloadRequired) is not 0;

    public WorkspaceGenerations Generations
    {
        get
        {
            lock (generationGate)
                return generations;
        }
    }

    public int Generation(ChangeKind kind) => Generations.Of(kind);

    public static ChangeKind? Classify(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());

        if (WorkspaceFiles.Matches(extension, CodeExtensions))
            return ChangeKind.Code;

        if (WorkspaceFiles.Matches(extension, ProjectExtensions) || IsProjectName(path))
            return ChangeKind.Project;

        if (WorkspaceFiles.Matches(extension, MarkupExtensions))
            return ChangeKind.Xaml;

        if (WorkspaceFiles.Matches(extension, RazorExtensions))
            return ChangeKind.Razor;

        return WorkspaceFiles.Matches(extension, ResourceExtensions) ? ChangeKind.Resx : null;
    }

    public void Notice(string path)
    {
        if (Classify(path) is null || WorkspaceFiles.IsTemporary(path) || WorkspaceFiles.IsExcluded(path, root))
            return;

        if (pending.Count >= PendingCap)
        {
            Rebuild();

            return;
        }

        pending[Path.GetFullPath(path)] = 0;
    }

    public void Settled(string path)
    {
        var full = Path.GetFullPath(path);

        stamps[full] = FileStamp.Of(full);
    }

    public async Task<bool> SyncAsync(LoadedWorkspace workspace, string? pathHint, CancellationToken cancellationToken)
    {
        Verify(workspace, pathHint);

        if (Reloading)
            return true;

        if (pending.IsEmpty)
            return false;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await DrainAsync(workspace, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    internal void Watching() => State = WatchState.Active;

    internal void Off() => State = WatchState.Off;

    internal void Degrade(string reason)
    {
        State = WatchState.Degraded;
        StateDetail = reason;
        Rebuild();
    }

    internal void Gap()
    {
        Interlocked.Increment(ref gaps);
        Rebuild();
    }

    private void Rebuild() => Volatile.Write(ref reloadRequired, 1);

    private Solution Rebuild(Solution solution)
    {
        Rebuild();

        return solution;
    }

    private void Verify(LoadedWorkspace workspace, string? pathHint)
    {
        if (pathHint is null || PathGuard.Resolve(workspace, pathHint) is not { IsOk: true } resolved)
            return;

        var full = resolved.Value!;

        if (stamps.TryGetValue(full, out var known) && known == FileStamp.Of(full))
            return;

        Notice(full);
    }

    private async Task<bool> DrainAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var paths = Take();

        try
        {
            var (code, kinds) = Sort(paths);
            var changed = await AbsorbAsync(workspace, code, kinds, cancellationToken).ConfigureAwait(false);

            Settle(workspace, paths, changed, kinds);
        }
        catch
        {
            Restore(paths);

            throw;
        }

        Volatile.Write(ref lastSyncMilliseconds, stopwatch.ElapsedMilliseconds);

        return Reloading;
    }

    private string[] Take()
    {
        var taken = pending.Keys.ToArray();

        foreach (var path in taken)
            pending.TryRemove(path, out _);

        return taken;
    }

    private (List<string> Code, HashSet<ChangeKind> Kinds) Sort(string[] paths)
    {
        var code = new List<string>(paths.Length);
        var kinds = new HashSet<ChangeKind>();

        foreach (var path in paths)
            Route(path, code, kinds);

        return (code, kinds);
    }

    private void Route(string path, List<string> code, HashSet<ChangeKind> kinds)
    {
        if (Classify(path) is not { } kind)
            return;

        if (kind is ChangeKind.Code)
        {
            code.Add(path);

            return;
        }

        kinds.Add(kind);

        if (kind is ChangeKind.Project)
            Rebuild();
    }

    private async Task<List<string>> AbsorbAsync(
    LoadedWorkspace workspace,
    List<string> code,
    HashSet<ChangeKind> kinds,
    CancellationToken cancellationToken)
    {
        var changed = new List<string>();
        if (code.Count is 0 || Reloading)
            return changed;

        var updated = workspace.Solution;
        foreach (var path in code)
            updated = await MergeAsync(updated, path, changed, cancellationToken).ConfigureAwait(false);

        if (changed.Count is 0)
            return changed;

        if (Shifted(changed))
            return [];

        if (await workspace.AdoptAsync(updated, cancellationToken).ConfigureAwait(false))
            kinds.Add(ChangeKind.Code);
        else
            Rebuild();

        return changed;
    }

    private async Task<Solution> MergeAsync(
        Solution solution,
        string path,
        List<string> changed,
        CancellationToken cancellationToken)
    {
        var stamp = FileStamp.Of(path);
        if (stamps.TryGetValue(path, out var known) && known == stamp)
            return solution;

        if (Find(solution, path) is not { } document)
        {
            stamps[path] = stamp;

            return Absent(solution, path, stamp);
        }

        if (stamp == FileStamp.Missing)
            return Rebuild(solution);

        Solution merged;
        try
        {
            merged = await ReplaceAsync(document, changed, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            Requeue(path);

            return solution;
        }

        if (FileStamp.Of(path) == stamp)
            stamps[path] = stamp;

        return merged;
    }
    private static async Task<Solution> ReplaceAsync(
        Document document,
        List<string> changed,
        CancellationToken cancellationToken)
    {
        var materialised = document.TryGetText(out _);
        var text = Read(document.FilePath!);
        var current = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        if (current.ContentEquals(text))
        {
            if (!materialised)
                changed.Add(document.FilePath!);

            return document.Project.Solution;
        }

        changed.Add(document.FilePath!);
        return document.Project.Solution.WithDocumentText(document.Id, text);
    }
    private Solution Absent(Solution solution, string path, FileStamp stamp) =>
        stamp != FileStamp.Missing && Attributable(solution, path) ? Rebuild(solution) : solution;

    private void Settle(
        LoadedWorkspace workspace,
        string[] paths,
        IReadOnlyList<string> changed,
        HashSet<ChangeKind> kinds)
    {
        foreach (var path in paths.Where(path => Classify(path) is not ChangeKind.Code))
            stamps[path] = FileStamp.Of(path);
        workspace.DropSnapshots(changed);
        lock (generationGate)
        {
            foreach (var kind in kinds.Where(Countable))
                generations = generations.Bump(kind);
        }
    }
    private static SourceText Read(string path)
    {
        using var stream = File.OpenRead(path);
        return SourceText.From(stream);
    }
    private static Document? Find(Solution solution, string path) => solution
        .GetDocumentIdsWithFilePath(path)
        .Select(solution.GetDocument)
        .FirstOrDefault(document => document is not null);

    private static bool Attributable(Solution solution, string path) => solution.Projects
        .Select(project => project.FilePath)
        .OfType<string>()
        .Select(Path.GetDirectoryName)
        .OfType<string>()
        .Any(directory => PathBoundary.Contains(directory, path));

    private static bool IsProjectName(string path) =>
        ProjectNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private void Restore(string[] paths)
    {
        foreach (var path in paths)
            Requeue(path);
    }
    private bool Countable(ChangeKind kind) =>
                kind is ChangeKind.Xaml or ChangeKind.Resx or ChangeKind.Razor || !Reloading;

    internal void Bumped(ChangeKind kind)
    {
        lock (generationGate)
            generations = generations.Bump(kind);
    }

    public void Noticed(string path, ChangeKind kind)
    {
        Notice(path);
        Touched(path);
        Bumped(kind);

        if (kind is ChangeKind.Razor)
            RazorGeneratedMap.Forget();
    }

    public void Touched(string path)
    {
        if (WorkspaceFiles.IsTemporary(path) || WorkspaceFiles.IsExcluded(path, root))
            return;

        Bumped(ChangeKind.Files);
    }

    private bool Shifted(List<string> changed)
    {
        if (changed.All(Stable))
            return false;

        foreach (var path in changed)
            Requeue(path);

        return true;
    }

    private bool Stable(string path) =>
        stamps.TryGetValue(path, out var stamp) && stamp == FileStamp.Of(path);

    private void Requeue(string path)
    {
        stamps.TryRemove(path, out _);
        pending[path] = 0;
    }
}
