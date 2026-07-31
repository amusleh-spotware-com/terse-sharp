using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct DiagnosticScope(string Root, string? File)
{
    public static DiagnosticScope For(LoadedWorkspace workspace, string? path) =>
        new(workspace.Root, path is null ? null : Resolve(workspace.Root, path));

    public bool Includes(Diagnostic diagnostic) => diagnostic.Severity is DiagnosticSeverity.Error
        ? NamesTheFile(diagnostic.Location.GetLineSpan().Path)
        : Includes(diagnostic.Location);

    public bool Includes(Location location) => Includes(location.GetLineSpan().Path);

    public bool Includes(string? file) =>
        NamesTheFile(file) && (File is not null || !GeneratedCode.IsGenerated(Root, file));

    private bool NamesTheFile(string? file) => File is null || PathBoundary.SameFile(file, File);

    private static string Resolve(string root, string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
}
