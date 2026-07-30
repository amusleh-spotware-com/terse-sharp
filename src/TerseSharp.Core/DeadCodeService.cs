using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class DeadCodeService
{
    private static readonly string[] CompilerHints = ["CS0169", "CS0414", "CS0162", "CS8019", "CS0219"];

    public static async Task<IReadOnlyList<string>> FindAsync(
        LoadedWorkspace workspace,
        string? path,
        CancellationToken cancellationToken)
    {
        var findings = new List<string>();

        foreach (var project in Targets(workspace, path))
            await ScanAsync(workspace, project, findings, cancellationToken).ConfigureAwait(false);

        return [.. findings.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, string? path)
    {
        var document = path is null ? null : DocumentLookup.Find(workspace, path);

        return document is null ? workspace.Solution.Projects : [document.Project];
    }

    private static async Task ScanAsync(
        LoadedWorkspace workspace,
        Project project,
        List<string> findings,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken).Where(Hinted))
        {
            findings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{diagnostic.Id} info DeadCode {PositionFormat.Describe(diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
        }

        await ScanMembersAsync(workspace, compilation, findings, cancellationToken).ConfigureAwait(false);
    }

    private static bool Hinted(Diagnostic diagnostic) =>
        CompilerHints.Contains(diagnostic.Id, StringComparer.Ordinal) && !diagnostic.IsSuppressed;

    private static async Task ScanMembersAsync(
        LoadedWorkspace workspace,
        Compilation compilation,
        List<string> findings,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in Candidates(compilation))
        {
            var references = await SymbolFinder
                .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
                .ConfigureAwait(false);

            if (references.Sum(reference => reference.Locations.Count(location => !location.IsImplicit)) is 0)
                findings.Add(Describe(symbol));
        }
    }

    private static string Describe(ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"TERSE001 info DeadCode {SymbolFormat.Location(symbol)}: '{symbol.Name}' is never referenced ({SymbolId.From(symbol)})");

    private static IEnumerable<ISymbol> Candidates(Compilation compilation) =>
        Types(compilation.Assembly.GlobalNamespace)
            .SelectMany(type => type.GetMembers())
            .Where(IsCandidate);

    private static bool IsCandidate(ISymbol member) =>
        member.DeclaredAccessibility is Accessibility.Private
        && !member.IsImplicitlyDeclared
        && member.Locations.Any(location => location.IsInSource)
        && member.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field
        && member is not IMethodSymbol { MethodKind: not MethodKind.Ordinary };

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var type in Types(child))
                yield return type;
        }
    }
}
