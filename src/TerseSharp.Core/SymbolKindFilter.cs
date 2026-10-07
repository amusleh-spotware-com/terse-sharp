using System.Collections.Frozen;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class SymbolKindFilter
{
    private static readonly string[] Kinds = ["type", "class", "record", "struct", "interface", "enum", "delegate", "method", "ctor", "property", "field", "event", "namespace", "namedtype"];

    private static readonly FrozenSet<string> KnownKinds = Kinds.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string Accepted { get; } = string.Join(", ", Kinds);

    public static bool IsKnown(string? kind) => string.IsNullOrWhiteSpace(kind) || KnownKinds.Contains(kind);

    public static TerseError? Refusal(string? kind) => IsKnown(kind)
        ? null
        : Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"kind='{kind}' is not a kind search_symbols can filter by"),
            string.Create(CultureInfo.InvariantCulture, $"pass one of {Accepted}, or leave kind empty to search every kind"));

    public static bool Matches(ISymbol symbol, string? kind) =>
        string.IsNullOrWhiteSpace(kind) || NamedTypeMatches(symbol, kind) || FormattedMatches(symbol, kind);

    private static bool NamedTypeMatches(ISymbol symbol, string kind) => symbol switch
    {
        INamedTypeSymbol when kind.Equals("type", StringComparison.OrdinalIgnoreCase) => true,
        INamedTypeSymbol { IsRecord: true } => kind.Equals("record", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool FormattedMatches(ISymbol symbol, string kind) =>
        SymbolFormat.Kind(symbol).Equals(kind, StringComparison.OrdinalIgnoreCase)
        || symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
}
