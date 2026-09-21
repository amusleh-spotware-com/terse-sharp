using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

internal static class AttributeText
{
    public static bool Any(ImmutableArray<IParameterSymbol> parameters)
    {
        foreach (var parameter in parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (!Implied(attribute))
                    return true;
            }
        }

        return false;
    }

    public static void Append(StringBuilder builder, IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (Implied(attribute))
                continue;

            builder.Append('[').Append(Named(attribute));
            Arguments(builder, attribute);
            builder.Append("] ");
        }
    }

    private static ReadOnlySpan<char> Named(AttributeData attribute)
    {
        var name = attribute.AttributeClass!.Name.AsSpan();

        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    private static void Arguments(StringBuilder builder, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length is 0 && attribute.NamedArguments.Length is 0)
            return;

        var opened = builder.Length;

        builder.Append('(');
        Positional(builder, attribute.ConstructorArguments, opened);
        Assigned(builder, attribute.NamedArguments, opened);
        Closed(builder, opened);
    }

    private static void Positional(StringBuilder builder, ImmutableArray<TypedConstant> arguments, int opened)
    {
        foreach (var argument in arguments)
        {
            ConstantText.Separated(builder, opened);
            ConstantText.Append(builder, argument);
        }
    }

    private static void Assigned(StringBuilder builder, ImmutableArray<KeyValuePair<string, TypedConstant>> arguments, int opened)
    {
        foreach (var argument in arguments)
        {
            ConstantText.Separated(builder, opened);
            builder.Append(argument.Key).Append(" = ");
            ConstantText.Append(builder, argument.Value);
        }
    }

    private static readonly string[] Implicit =
        [
            "ParamArrayAttribute",
            "ParamCollectionAttribute",
            "OptionalAttribute",
            "DefaultParameterValueAttribute",
            "DecimalConstantAttribute",
            "InAttribute",
            "OutAttribute",
            "IsReadOnlyAttribute",
            "RequiresLocationAttribute",
            "NullableAttribute",
            "DynamicAttribute",
            "NativeIntegerAttribute",
            "TupleElementNamesAttribute",
            "ScopedRefAttribute",
        ];

    private static bool Implied(AttributeData attribute) =>
        attribute.AttributeClass is not { } declared || Array.IndexOf(Implicit, declared.Name) >= 0;

    private const int MaxArgumentChars = 40;

    private static void Closed(StringBuilder builder, int opened)
    {
        if (builder.Length - opened <= MaxArgumentChars)
        {
            builder.Append(')');

            return;
        }

        builder.Length = opened;
        builder.Append("(...)");
    }
}
