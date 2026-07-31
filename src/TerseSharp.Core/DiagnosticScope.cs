using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct DiagnosticScope(string Root, string? File)
{
    public static DiagnosticScope For(LoadedWorkspace workspace, string? path) =>
        new(workspace.Root, path is null ? null : Resolve(workspace.Root, path));

    public bool Includes(Location location) => Includes(location.GetLineSpan().Path);

    public bool Includes(string? file) =>
        !GeneratedCode.IsGenerated(Root, file) && (File is null || PathBoundary.SameFile(file, File));

    private static string Resolve(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
}
