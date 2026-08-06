using System.Text;

namespace TerseSharp.Core;

public readonly record struct ProjectSnapshot(string ProjectPath, byte[] Bytes, IReadOnlyList<string> AddedFiles);

public static class ProjectFileGuard
{
    public static async Task<ProjectSnapshot?> CaptureAsync(
        string? projectPath,
        IReadOnlyList<string> addedFiles,
        CancellationToken cancellationToken)
    {
        if (projectPath is not { Length: > 0 } path || addedFiles.Count is 0 || !File.Exists(path))
            return null;

        if (!Globs(path))
            return null;

        return new ProjectSnapshot(path, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), addedFiles);
    }

    private static bool Globs(string path)
    {
        var stamp = new FileInfo(path);
        var key = new GlobKey(path, stamp.LastWriteTimeUtc, stamp.Length);

        if (Verdicts.TryGetValue(key, out var known))
            return known;

        var verdict = ProjectGlobs.CompilesByGlob(path) is true;

        Verdicts[key] = verdict;

        return verdict;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<GlobKey, bool> Verdicts = new();

    private readonly record struct GlobKey(string Path, DateTime LastWriteUtc, long Length);

    public static async Task<bool> RestoreAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!File.Exists(snapshot.ProjectPath))
            return false;

        var current = await File.ReadAllBytesAsync(snapshot.ProjectPath, cancellationToken).ConfigureAwait(false);

        if (current.AsSpan().SequenceEqual(snapshot.Bytes))
            return false;

        if (!OnlyRedundantCompileItems(Text(snapshot.Bytes), Text(current), snapshot.AddedFiles))
            return false;

        await AtomicWrite.BytesAsync(snapshot.ProjectPath, snapshot.Bytes, cancellationToken).ConfigureAwait(false);

        return true;
    }

    internal static bool OnlyRedundantCompileItems(string before, string after, IReadOnlyList<string> addedFiles)
    {
        var remaining = new List<string>(Lines(before));

        foreach (var line in Lines(after))
        {
            if (remaining.Remove(line))
                continue;

            if (!IsAddedItem(line, addedFiles) && !IsItemGroupTag(line))
                return false;
        }

        return remaining.Count is 0;
    }

    private static bool IsAddedItem(string line, IReadOnlyList<string> addedFiles)
    {
        if (!line.StartsWith("<Compile ", StringComparison.Ordinal) || Included(line) is not { } include)
            return false;

        var name = Path.GetFileName(include.AsSpan());

        foreach (var file in addedFiles)
        {
            if (name.Equals(Path.GetFileName(file.AsSpan()), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? Included(string line)
    {
        const string Marker = "Include=\"";
        var start = line.IndexOf(Marker, StringComparison.Ordinal);

        if (start < 0)
            return null;

        var from = start + Marker.Length;
        var end = line.IndexOf('"', from);

        return end < 0 ? null : line[from..end];
    }

    private static bool IsItemGroupTag(string line) =>
        line is "<ItemGroup>" or "</ItemGroup>";

    private static List<string> Lines(string text)
    {
        var lines = new List<string>(64);

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();

            if (!trimmed.IsEmpty)
                lines.Add(new string(trimmed));
        }

        return lines;
    }

    private static string Text(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).TrimStart('﻿');
}
