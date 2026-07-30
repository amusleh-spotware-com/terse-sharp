using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct SymbolId(string Value)
{
    public static SymbolId From(ISymbol symbol) =>
        new(DocumentationCommentId.CreateDeclarationId(symbol) ?? symbol.ToDisplayString());

    public override string ToString() => Value;
}
