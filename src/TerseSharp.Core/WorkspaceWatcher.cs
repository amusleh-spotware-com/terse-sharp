namespace TerseSharp.Core;

internal sealed class WorkspaceWatcher(FileSystemWatcher? watcher) : IDisposable
{
    private const int BufferBytes = 64 * 1024;

    public static WorkspaceWatcher Create(string root, WorkspaceSync sync, bool enabled)
    {
        if (!enabled)
        {
            sync.Off();

            return new WorkspaceWatcher(null);
        }

        try
        {
            var started = Start(root, sync);

            sync.Watching();

            return new WorkspaceWatcher(started);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            sync.Degrade(exception.Message);

            return new WorkspaceWatcher(null);
        }
    }

    public void Dispose() => watcher?.Dispose();

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
        started.Created += (_, args) => sync.Notice(args.FullPath);
        started.Changed += (_, args) => sync.Notice(args.FullPath);
        started.Deleted += (_, args) => sync.Notice(args.FullPath);
        started.Renamed += (_, args) => Renamed(sync, args);
        started.Error += (_, _) => sync.Gap();
        started.EnableRaisingEvents = true;
    }

    private static void Renamed(WorkspaceSync sync, RenamedEventArgs args)
    {
        sync.Notice(args.OldFullPath);
        sync.Notice(args.FullPath);
    }

    private static bool IsUnavailable(Exception exception) => exception
        is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException;
}
