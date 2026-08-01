using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class DeadCodeService
{
    private static readonly string[] CompilerHints = ["CS0169", "CS0414", "CS0162", "CS8019", "CS0219"];

    public static async Task<IReadOnlyList<string>> FindAsync(
        LoadedWorkspace workspace,
        IEnumerable<Project> targets,
        DiagnosticScope scope,
        CancellationToken cancellationToken)
    {
        var findings = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            targets,
            ParallelWork.Sequential(cancellationToken),
            (project, token) => ScanAsync(workspace, project, scope, findings, token)).ConfigureAwait(false);

        return [.. findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static async ValueTask ScanAsync(
        LoadedWorkspace workspace,
        Project project,
        DiagnosticScope scope,
        ConcurrentBag<string> findings,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return;

        var hinted = compilation
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => Hinted(diagnostic) && scope.Includes(diagnostic.Location));

        foreach (var diagnostic in hinted)
            findings.Add(Describe(scope.Root, diagnostic));

        await ScanMembersAsync(workspace, compilation, scope, findings, cancellationToken).ConfigureAwait(false);
    }

    private static bool Hinted(Diagnostic diagnostic) =>
        CompilerHints.Contains(diagnostic.Id, StringComparer.Ordinal) && !diagnostic.IsSuppressed;

    private static string Describe(string root, Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} info DeadCode {PositionFormat.Describe(root, diagnostic.Location)}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");

    private static Task ScanMembersAsync(
        LoadedWorkspace workspace,
        Compilation compilation,
        DiagnosticScope scope,
        ConcurrentBag<string> findings,
        CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            Candidates(compilation, scope),
            ParallelWork.Options(cancellationToken),
            (symbol, token) => InspectAsync(workspace, symbol, scope, findings, token));

    private static async ValueTask InspectAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        DiagnosticScope scope,
        ConcurrentBag<string> findings,
        CancellationToken cancellationToken)
    {
        var declaring = DeclaringDocuments(workspace.Solution, symbol.ContainingType);
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, declaring, cancellationToken)
            .ConfigureAwait(false);

        if (references.Sum(reference => reference.Locations.Count(location => !location.IsImplicit)) is 0)
            findings.Add(Describe(scope.Root, symbol));
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

    private static string Describe(string root, ISymbol symbol) => string.Create(
        CultureInfo.InvariantCulture,
        $"TERSE001 info DeadCode {SymbolFormat.Location(root, symbol)}: '{symbol.Name}' is never referenced ({SymbolId.From(symbol)})");

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
