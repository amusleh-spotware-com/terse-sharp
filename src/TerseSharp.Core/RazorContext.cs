using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record RazorContext(
    LoadedWorkspace Workspace,
    RazorDocument Document,
    string FullPath,
    string Relative,
    Project? Project,
    Compilation? Compilation,
    RazorGenerated? Generated,
    bool GeneratorRan,
    IReadOnlyList<RazorDocument> Imports,
    IReadOnlyList<string> Scopes)
{
    public INamedTypeSymbol? Self => Generated?.Type;

    public INamedTypeSymbol? Resolve(string tagName) => Compilation is null
        ? null
        : RazorSemantics.Resolve(Compilation, Scopes, tagName);

    public IEnumerable<ISymbol> DeclaredMembers => Self is null
        ? []
        : Self.GetMembers().Where(member => !member.IsImplicitlyDeclared && DeclaredHere(member));

    public int LineOf(ISymbol member) => member.Locations
        .Select(location => location.GetMappedLineSpan())
        .Where(span => PathBoundary.SameFile(span.Path, FullPath))
        .Select(span => span.StartLinePosition.Line + 1)
        .FirstOrDefault();

    private bool DeclaredHere(ISymbol member) => member.Locations
        .Any(location => PathBoundary.SameFile(location.GetMappedLineSpan().Path, FullPath));
}

public static class RazorOpen
{
    public static async Task<Result<RazorContext>> AtAsync(
        LoadedWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<RazorContext>(resolved.Error!);

        var full = resolved.Value!;

        if (!RazorDocument.IsRazor(full))
        {
            return Result.Fail<RazorContext>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' is not a Razor file"),
                "pass a .razor or .cshtml file"));
        }

        return RazorIndex.Of(full) is { } document
            ? Result.Ok(await BuildAsync(workspace, document, full, cancellationToken).ConfigureAwait(false))
            : Result.Fail<RazorContext>(Errors.DocumentNotFound(path));
    }

    public static Project? ProjectOf(LoadedWorkspace workspace, string fullPath) =>
        workspace.Solution.Projects.FirstOrDefault(project => Holds(project, fullPath))
        ?? workspace.Solution.Projects.FirstOrDefault(project => Under(project, fullPath));

    private static async Task<RazorContext> BuildAsync(
        LoadedWorkspace workspace,
        RazorDocument document,
        string full,
        CancellationToken cancellationToken)
    {
        var project = ProjectOf(workspace, full);
        var compilation = project is null ? null : await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var generated = await GeneratedAsync(project, cancellationToken).ConfigureAwait(false);

        var mine = generated.FirstOrDefault(entry => PathBoundary.SameFile(entry.RazorPath, full));
        var imports = Imports(workspace, full);
        var scopes = RazorSemantics.Scopes(document, imports, mine?.Type.ContainingNamespace.ToDisplayString());

        return new RazorContext(
            workspace,
            document,
            full,
            PositionFormat.Relative(workspace.Root, full),
            project,
            compilation,
            mine,
            generated.Count > 0,
            imports,
            scopes);
    }

    private static async Task<IReadOnlyList<RazorGenerated>> GeneratedAsync(Project? project, CancellationToken cancellationToken) =>
        project is null
            ? []
            : await RazorGeneratedMap.InProjectAsync(project, cancellationToken).ConfigureAwait(false);

    private static IReadOnlyList<RazorDocument> Imports(LoadedWorkspace workspace, string full) =>
    [
        .. RazorFiles.ImportsFor(full)
            .Where(import => workspace.Contains(import))
            .Select(RazorIndex.Of)
            .OfType<RazorDocument>(),
    ];

    private static bool Holds(Project project, string fullPath) =>
        project.AdditionalDocuments.Any(document => PathBoundary.SameFile(document.FilePath, fullPath));

    private static bool Under(Project project, string fullPath) =>
        Path.GetDirectoryName(project.FilePath) is { Length: > 0 } directory && PathBoundary.Contains(directory, fullPath);
}
