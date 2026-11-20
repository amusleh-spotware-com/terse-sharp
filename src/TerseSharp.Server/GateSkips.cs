namespace TerseSharp.Server;

internal readonly record struct GateTargets(string? Path, string?[]? Paths, string[] Skipped)
{
    public bool Empty => Path is null && Paths is null && Skipped.Length > 0;
}

internal static class GateSkips
{
    public static GateTargets Split(LoadedWorkspace workspace, string? path, string?[]? paths)
    {
        var skipped = new List<string>();
        var keptPath = Kept(workspace, path, skipped);
        string?[]? keptPaths = paths is null ? null : [.. Kept(workspace, paths, skipped)];

        return new(keptPath, keptPaths is { Length: 0 } ? null : keptPaths, [.. skipped]);
    }

    public static string Answer(string[] skipped) =>
        string.Create(CultureInfo.InvariantCulture, $"skipped  {skipped.Length} file(s) no project compiles as C# - {SkipCheckers.Named(skipped)}");

    public static Result<string> Appended(Result<string> verdict, string[] skipped) =>
        verdict.IsOk ? Result.Ok(Appended(verdict.Value!, skipped)) : verdict;

    public static string Appended(string verdict, string[] skipped) => skipped.Length is 0 || verdict.StartsWith("ERROR", StringComparison.Ordinal)
        ? verdict
        : string.Create(CultureInfo.InvariantCulture, $"{verdict}\nskipped {skipped.Length} file(s) no project compiles as C# - {SkipCheckers.Named(skipped)}");

    private static IEnumerable<string?> Kept(LoadedWorkspace workspace, string?[] paths, List<string> skipped)
    {
        foreach (var entry in paths)
        {
            if (Kept(workspace, entry, skipped) is { } kept)
                yield return kept;
        }
    }

    private static string? Kept(LoadedWorkspace workspace, string? entry, List<string> skipped)
    {
        if (!Skips(workspace, entry))
            return entry;

        skipped.Add(entry!);

        return null;
    }

    private static bool Skips(LoadedWorkspace workspace, string? entry) =>
        entry is { Length: > 0 } && NamesOneOtherFile(workspace, entry) && DocumentLookup.Find(workspace, entry) is null;

    private static bool NamesOneOtherFile(LoadedWorkspace workspace, string entry) =>
        entry.AsSpan().IndexOfAny('*', '?', '{') < 0 && !SourceFile.IsCSharp(entry) && File.Exists(Path.GetFullPath(entry, workspace.Root));
}
