using System.Text;

namespace TerseSharp.Core;

public readonly record struct ProjectSnapshot(string ProjectPath, byte[] Bytes, IReadOnlyList<string> AddedFiles);

public static class ProjectFileGuard
{
    public static ProjectSnapshot? Capture(string? projectPath, IReadOnlyList<string> addedFiles)
    {
        if (projectPath is not { Length: > 0 } path || addedFiles.Count is 0 || !File.Exists(path))
            return null;

        return ProjectGlobs.CompilesByGlob(path) is true
            ? new ProjectSnapshot(path, File.ReadAllBytes(path), addedFiles)
            : null;
    }

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
        if (!line.StartsWith("<Compile ", StringComparison.Ordinal))
            return false;

        foreach (var file in addedFiles)
        {
            if (line.Contains(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
