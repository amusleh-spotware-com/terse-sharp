namespace TerseSharp.Core;

public readonly record struct WorkspaceMarkup(bool Xaml, bool Razor, bool Resx)
{
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

    private IEnumerable<string> Families()
    {
        if (!Xaml)
            yield return "xaml_*";

        if (!Razor)
            yield return "razor_*";

        if (!Resx)
            yield return "resx_*";
    }

    private static WorkspaceMarkup Kind(string path) => new(
        XamlDocument.IsXaml(path),
        RazorDocument.IsRazor(path),
        ResxIndex.IsResource(path));
}

public sealed record MarkupIndex(WorkspaceMarkup Families);
