using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class MissingDocument
{
    public static async Task<TerseError> ReadAsync(LoadedWorkspace workspace, string path, CancellationToken cancellationToken)
    {
        var declaring = await DeclaringFileAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        return declaring is null ? Errors.DocumentNotFound(path) : Errors.DocumentNotFound(path, declaring);
    }

    public static TerseError Write(LoadedWorkspace workspace, string path) =>
        IsUnwrittenSource(workspace, path)
            ? Errors.DocumentNotFound(
                path,
                string.Create(CultureInfo.InvariantCulture, $"no file exists there yet - create it with write_text path={path} force=true"))
            : Errors.DocumentNotFound(path);

    private static async Task<string?> DeclaringFileAsync(LoadedWorkspace workspace, string path, CancellationToken cancellationToken)
    {
        if (!path.AsSpan().EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return null;

        var stem = Path.GetFileNameWithoutExtension(path.AsSpan());

        if (stem.IsEmpty)
            return null;

        var name = stem.ToString();
        var found = await SymbolSearch.FindAsync(workspace, name, null, null, TypeCap, cancellationToken).ConfigureAwait(false);
        var declared = found.Ranked.FirstOrDefault(symbol => Declares(symbol, name));

        return declared is null ? null : Steer(workspace, declared, name);
    }

    private static bool Declares(ISymbol symbol, string name) =>
        symbol is INamedTypeSymbol
        && string.Equals(symbol.Name, name, StringComparison.Ordinal)
        && Source(symbol) is not null;

    private static string? Steer(LoadedWorkspace workspace, ISymbol declared, string name) =>
        Source(declared) is { } file
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"'{name}' is declared in {PositionFormat.Relative(workspace.Root, file)} - use get_file_outline or get_type_outline on that path")
            : null;

    private static string? Source(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath;

    private static bool IsUnwrittenSource(LoadedWorkspace workspace, string path)
    {
        if (!path.AsSpan().EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        var full = Path.GetFullPath(Path.Combine(workspace.Root, path));

        return PathBoundary.Contains(workspace.Root, full) && !File.Exists(full);
    }

    private const int TypeCap = 8;
}
