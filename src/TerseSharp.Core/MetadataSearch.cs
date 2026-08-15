using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct MetadataMatches(IReadOnlyList<INamedTypeSymbol> Found, int Total);

public static class MetadataSearch
{
    public static bool IsMetadata(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Length is 0 && symbol.ContainingAssembly is not null;

    public static string Origin(ISymbol symbol) =>
        symbol.ContainingAssembly is { } assembly
            ? string.Create(CultureInfo.InvariantCulture, $"{assembly.Identity.Name} {assembly.Identity.Version}")
            : "-";

    public static async Task<MetadataMatches> FindAsync(
        LoadedWorkspace workspace,
        string name,
        int cap,
        CancellationToken cancellationToken,
        bool exhaustive = false)
    {
        var hunt = new Hunt(name, cap, exhaustive);

        foreach (var project in workspace.Solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is { } compilation)
                Collect(compilation, hunt, cancellationToken);

            if (hunt.Done)
                break;
        }

        return new MetadataMatches(hunt.Found, hunt.Total);
    }

    private static void Collect(Compilation compilation, Hunt hunt, CancellationToken cancellationToken)
    {
        if (hunt.Qualified && compilation.GetTypeByMetadataName(hunt.Name) is { } qualified)
        {
            hunt.Add(qualified);

            return;
        }

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reference is PortableExecutableReference
                && compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly
                && hunt.Assemblies.Add(assembly.Identity.GetDisplayName()))
            {
                Walk(assembly.GlobalNamespace, hunt, cancellationToken);
            }

            if (hunt.Done)
                return;
        }
    }

    private static void Walk(INamespaceSymbol space, Hunt hunt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var type in space.GetTypeMembers(hunt.Name))
            hunt.Add(type);

        foreach (var nested in space.GetNamespaceMembers())
        {
            if (hunt.Done)
                return;

            Walk(nested, hunt, cancellationToken);
        }
    }

    private sealed class Hunt(string name, int cap, bool exhaustive)
    {
        public string Name { get; } = name;

        public bool Qualified { get; } = name.Contains('.', StringComparison.Ordinal);

        public List<INamedTypeSymbol> Found { get; } = [];

        public HashSet<string> Assemblies { get; } = new(StringComparer.Ordinal);

        public int Total { get; private set; }

        private HashSet<string> Types { get; } = new(StringComparer.Ordinal);

        public bool Done => !exhaustive && Found.Count >= cap;

        public void Add(INamedTypeSymbol type)
        {
            if (type.DeclaredAccessibility is not Accessibility.Public || !Types.Add(type.ToDisplayString()))
                return;

            Total += 1;

            if (Found.Count < cap)
                Found.Add(type);
        }
    }
}
