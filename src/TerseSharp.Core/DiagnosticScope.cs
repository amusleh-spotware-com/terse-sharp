using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct DiagnosticScope(string Root, IReadOnlySet<string>? Files)
{
    public static DiagnosticScope For(LoadedWorkspace workspace, string? path) =>
        new(workspace.Root, path is null ? null : Set([Resolve(workspace.Root, path)]));

    public static DiagnosticScope Of(string root, IEnumerable<string> files) => new(root, Set(files));

    public bool Includes(Diagnostic diagnostic) => diagnostic.Severity is DiagnosticSeverity.Error
        ? NamesTheFile(diagnostic.Location.GetLineSpan().Path)
        : Includes(diagnostic.Location);

    public bool Includes(Location location) => Includes(location.GetLineSpan().Path);

    public bool Includes(string? file) =>
        NamesTheFile(file) && (Files is not null || !GeneratedCode.IsGenerated(Root, file));

    private bool NamesTheFile(string? file) =>
        Files is null || (file is { Length: > 0 } named && Files.Contains(Resolve(Root, named)));

    private static HashSet<string> Set(IEnumerable<string> files) =>
        files.ToHashSet(PathBoundary.Comparer);

    private static string Resolve(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
}
