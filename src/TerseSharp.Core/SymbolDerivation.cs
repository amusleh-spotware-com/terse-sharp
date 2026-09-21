using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public static class SymbolDerivation
{
    public static async Task<ISymbol[]> FindAsync(Solution solution, ISymbol symbol, CancellationToken cancellationToken)
    {
        var found = new List<ISymbol>();

        found.AddRange(await SymbolFinder
            .FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
            .ConfigureAwait(false));

        if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Class } named)
        {
            found.AddRange(await SymbolFinder
                .FindDerivedClassesAsync(named, solution, transitive: true, cancellationToken: cancellationToken)
                .ConfigureAwait(false));
        }

        if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            found.AddRange(await SymbolFinder
                .FindOverridesAsync(symbol, solution, cancellationToken: cancellationToken)
                .ConfigureAwait(false));
        }

        return [.. found.Distinct<ISymbol>(SymbolEqualityComparer.Default)];
    }

    public static string WhyEmpty(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Interface } => "nothing in this solution implements it",
        INamedTypeSymbol { IsSealed: true } closed => closed.Name + " is a sealed " + SymbolFormat.Kind(closed) + ", so nothing can derive from it",
        INamedTypeSymbol => "nothing in this solution derives from it",
        { ContainingType.TypeKind: TypeKind.Interface } => "nothing in this solution implements it",
        { IsAbstract: false, IsVirtual: false, IsOverride: false } member => member.Name + " is not virtual, abstract or an interface member, so nothing can override it",
        _ => "nothing in this solution overrides or implements it",
    };
}
