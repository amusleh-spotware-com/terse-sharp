namespace TerseSharp.Core;

public readonly record struct LoadFailureGroup(string Project, int Count);

public static class LoadFailureSummary
{
    private const int MaxUnattributedLength = 120;

    public static LoadFailureGroup[] Group(IReadOnlyList<string> failures)
    {
        if (failures.Count is 0)
            return [];

        var groups = new List<LoadFailureGroup>(failures.Count);

        foreach (var failure in failures)
            Add(groups, Name(failure));

        return [.. groups];
    }

    public static ReadOnlySpan<char> ProjectOf(ReadOnlySpan<char> failure)
    {
        var remaining = failure;

        while (remaining.IndexOf('\'') is var open and >= 0)
        {
            var rest = remaining[(open + 1)..];
            var close = rest.IndexOf('\'');

            if (close < 0)
                return [];

            if (rest[..close] is var quoted && quoted.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                return FileName(quoted);

            remaining = rest[(close + 1)..];
        }

        return [];
    }

    private static ReadOnlySpan<char> FileName(ReadOnlySpan<char> path) =>
        path.LastIndexOfAny('/', '\\') is var separator and >= 0 ? path[(separator + 1)..] : path;

    private static string Name(string failure) =>
        ProjectOf(failure) is { IsEmpty: false } project
            ? new string(project)
            : Shorten(failure);

    private static string Shorten(string failure) =>
        failure.Length <= MaxUnattributedLength ? failure : string.Concat(failure.AsSpan(0, MaxUnattributedLength), "...");

    private static void Add(List<LoadFailureGroup> groups, string project)
    {
        for (var index = 0; index < groups.Count; index++)
        {
            if (!string.Equals(groups[index].Project, project, StringComparison.Ordinal))
                continue;

            groups[index] = groups[index] with { Count = groups[index].Count + 1 };

            return;
        }

        groups.Add(new LoadFailureGroup(project, 1));
    }
}
