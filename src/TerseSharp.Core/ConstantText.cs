using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TerseSharp.Core;

internal static class ConstantText
{
    public static void Append(StringBuilder builder, TypedConstant constant)
    {
        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
                Listed(builder, constant);

                return;
            case TypedConstantKind.Type:
                builder.Append("typeof(").Append(constant.Value is ITypeSymbol type ? type.Name : "?").Append(')');

                return;
            case TypedConstantKind.Enum:
                Enumerated(builder, constant);

                return;
            default:
                builder.Append(Literal(constant.Value));

                return;
        }
    }

    public static void Separated(StringBuilder builder, int opened)
    {
        if (builder.Length > opened + 1)
            builder.Append(", ");
    }

    private static void Listed(StringBuilder builder, TypedConstant constant)
    {
        var opened = builder.Length;

        builder.Append('[');

        foreach (var item in constant.Values)
        {
            Separated(builder, opened);
            Append(builder, item);
        }

        builder.Append(']');
    }

    private static void Enumerated(StringBuilder builder, TypedConstant constant)
    {
        if (constant.Type is INamedTypeSymbol declared && Member(declared, constant.Value) is { } field)
        {
            builder.Append(declared.Name).Append('.').Append(field.Name);

            return;
        }

        builder.Append(Literal(constant.Value));
    }

    private static IFieldSymbol? Member(INamedTypeSymbol declared, object? value)
    {
        foreach (var member in declared.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, value))
                return field;
        }

        return null;
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        string text => SymbolDisplay.FormatLiteral(text, quote: true),
        char letter => SymbolDisplay.FormatLiteral(letter, quote: true),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
