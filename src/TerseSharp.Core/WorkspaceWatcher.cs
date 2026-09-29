namespace TerseSharp.Core;

internal sealed class WorkspaceWatcher(FileSystemWatcher[] watchers) : IDisposable
{
    private const int BufferBytes = 64 * 1024;

    public static WorkspaceWatcher Create(string root, IReadOnlyList<string> projectRoots, WorkspaceSync sync, bool enabled)
    {
        if (enabled)
            return Started(root, projectRoots, sync);

        sync.Off();

        return new WorkspaceWatcher([]);
    }

    private static WorkspaceWatcher Started(string root, IReadOnlyList<string> projectRoots, WorkspaceSync sync)
    {
        var started = new List<FileSystemWatcher>(1 + projectRoots.Count);

        try
        {
            StartAll(root, projectRoots, sync, started);
            sync.Watching();

            return new WorkspaceWatcher([.. started]);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            started.ForEach(watcher => watcher.Dispose());
            sync.Degrade(exception.Message);

            return new WorkspaceWatcher([]);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
    }

    private static void StartAll(string root, IReadOnlyList<string> projectRoots, WorkspaceSync sync, List<FileSystemWatcher> started)
    {
        started.Add(Start(root, sync));

        foreach (var projectRoot in projectRoots)
        {
            if (Watched(projectRoot, sync) is { } watcher)
                started.Add(watcher);
        }
    }

    private static FileSystemWatcher? Watched(string projectRoot, WorkspaceSync sync)
    {
        try
        {
            return Directory.Exists(projectRoot) ? Start(projectRoot, sync) : null;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return null;
        }
    }

    private static FileSystemWatcher Start(string root, WorkspaceSync sync)
    {
        var started = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = BufferBytes,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };

        try
        {
            Subscribe(started, sync);

            return started;
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            started.Dispose();

            throw;
        }
    }

    private static void Subscribe(FileSystemWatcher started, WorkspaceSync sync)
    {
        started.Created += (_, args) => Appeared(sync, args.FullPath);
        started.Changed += (_, args) => sync.Notice(args.FullPath);
        started.Deleted += (_, args) => Appeared(sync, args.FullPath);
        started.Renamed += (_, args) => Renamed(sync, args);
        started.Error += (_, _) => sync.Gap();
        started.EnableRaisingEvents = true;
    }

    private static void Appeared(WorkspaceSync sync, string path)
    {
        sync.Notice(path);
        sync.Touched(path);
    }

    private static void Renamed(WorkspaceSync sync, RenamedEventArgs args)
    {
        Appeared(sync, args.OldFullPath);
        Appeared(sync, args.FullPath);
    }

    private static bool IsUnavailable(Exception exception) => exception
        is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException;
}
