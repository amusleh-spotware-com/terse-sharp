using System.IO.Enumeration;

namespace TerseSharp.Core;

public readonly record struct WorkspaceMarkup(bool Xaml, bool Razor, bool Resx)
{
    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };

    public static WorkspaceMarkup Every { get; } = new(true, true, true);

    public bool Complete => Xaml && Razor && Resx;

    public WorkspaceMarkup Union(WorkspaceMarkup other) =>
        new(Xaml || other.Xaml, Razor || other.Razor, Resx || other.Resx);

    public bool Serves(ReadOnlySpan<char> tool) => tool switch
    {
        _ when tool.StartsWith("xaml_", StringComparison.Ordinal) => Xaml,
        _ when tool.StartsWith("razor_", StringComparison.Ordinal) => Razor,
        _ when tool.StartsWith("resx_", StringComparison.Ordinal) => Resx,
        _ => true,
    };

    public string Hidden() => string.Join(", ", Families());

    public static WorkspaceMarkup Of(PathIndex paths)
    {
        var found = default(WorkspaceMarkup);

        foreach (var path in paths.Paths)
        {
            found = found.Union(Kind(path.FullPath));

            if (found.Complete)
                break;
        }

        return found;
    }

    public static WorkspaceMarkup Scan(string root)
    {
        var found = default(WorkspaceMarkup);

        try
        {
            foreach (var kind in MarkupFiles(root))
            {
                found = found.Union(kind);

                if (found.Complete)
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Every;
        }

        return found;
    }

    private IEnumerable<string> Families()
    {
        if (!Xaml)
            yield return "xaml_*";

        if (!Razor)
            yield return "razor_*";

        if (!Resx)
            yield return "resx_*";
    }

    private static WorkspaceMarkup Kind(ReadOnlySpan<char> path) => new(
        XamlDocument.IsXaml(path),
        RazorDocument.IsRazor(path),
        ResxIndex.IsResource(path));

    private static FileSystemEnumerable<WorkspaceMarkup> MarkupFiles(string root) =>
        new(root, static (ref entry) => Kind(entry.FileName), Recursive)
        {
            ShouldIncludePredicate = static (ref entry) =>
                !entry.IsDirectory && !WorkspaceFiles.HoldsSessionState(entry.Directory) && Kind(entry.FileName) != default,
            ShouldRecursePredicate = WorkspaceFiles.Traversable,
        };
}

public sealed record MarkupIndex(WorkspaceMarkup Families);
