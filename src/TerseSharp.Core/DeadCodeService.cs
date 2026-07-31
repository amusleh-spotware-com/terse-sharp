using System.Collections.Immutable;
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
        var document = path is null ? null : DocumentLookup.Find(workspace, path);
        var scope = DiagnosticScope.For(workspace, path);
        var findings = new List<string>();

        foreach (var project in Targets(workspace, document))
            await ScanAsync(workspace, project, scope, findings, cancellationToken).ConfigureAwait(false);

        return [.. findings.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, Document? document) =>
        document is null ? workspace.Solution.Projects : [document.Project];

    private static async Task ScanAsync(
        LoadedWorkspace workspace,
        Project project,
        DiagnosticScope scope,
        List<string> findings,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        var hinted = compilation
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => Hinted(diagnostic) && scope.Includes(diagnostic.Location));

        foreach (var diagnostic in hinted)
            findings.Add(Describe(diagnostic));

        await ScanMembersAsync(workspace, compilation, scope, findings, cancellationToken).ConfigureAwait(false);
    }

    private static bool Hinted(Diagnostic diagnostic) =>
        CompilerHints.Contains(diagnostic.Id, StringComparer.Ordinal) && !diagnostic.IsSuppressed;

    private static string Describe(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} info DeadCode {PositionFormat.Describe(diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static async Task ScanMembersAsync(
        LoadedWorkspace workspace,
        Compilation compilation,
        DiagnosticScope scope,
        List<string> findings,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in Candidates(compilation, scope))
        {
            var declaring = DeclaringDocuments(workspace.Solution, symbol.ContainingType);

            if (await IsUnreferencedAsync(workspace, symbol, declaring, cancellationToken).ConfigureAwait(false))
                findings.Add(Describe(symbol));
        }
    }

    private static async Task<bool> IsUnreferencedAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        ImmutableHashSet<Document>? declaring,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, declaring, cancellationToken)
            .ConfigureAwait(false);

        return references.Sum(reference => reference.Locations.Count(location => !location.IsImplicit)) is 0;
    }

    private static ImmutableHashSet<Document>? DeclaringDocuments(Solution solution, INamedTypeSymbol? type)
    {
        if (type is null)
            return null;

        var documents = type.DeclaringSyntaxReferences
            .Select(reference => solution.GetDocument(reference.SyntaxTree))
            .ToArray();

        return documents.Length is 0 || Array.Exists(documents, document => document is null)
            ? null
            : [.. documents.OfType<Document>()];
    }

    private static string Describe(ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"TERSE001 info DeadCode {SymbolFormat.Location(symbol)}: '{symbol.Name}' is never referenced ({SymbolId.From(symbol)})");

    private static IEnumerable<ISymbol> Candidates(Compilation compilation, DiagnosticScope scope) =>
        Types(compilation.Assembly.GlobalNamespace)
            .SelectMany(type => type.GetMembers())
            .Where(member => IsCandidate(member) && InScope(member, scope));

    private static bool InScope(ISymbol member, DiagnosticScope scope) =>
        member.Locations.Any(location => scope.Includes(location));

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
            foreach (var nested in Nested(type))
                yield return nested;
        }

        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var type in Types(child))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var deeper in Nested(nested))
                yield return deeper;
        }
    }
}
