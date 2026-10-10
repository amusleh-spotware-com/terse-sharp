using System.Text;

namespace TerseSharp.Core;

public enum AnchorTier
{
    Exact,
    Spacing,
    Structural,
    Name
}

public static class AnchorSignature
{
    private const int StackLimit = 256;

    private static readonly string[] ParameterModifiers = ["ref", "out", "in", "params", "this", "scoped", "readonly"];

    public static string Canonical(string signature, AnchorTier tier)
    {
        var text = signature.AsSpan();
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
            return Stripped(text, dropNullable: false);

        var builder = new StringBuilder(signature.Length);

        builder.Append(Stripped(text[..open], dropNullable: false)).Append('(');
        Parameters(builder, text[(open + 1)..close], tier);

        return builder.Append(')').ToString();
    }

    private static void Parameters(StringBuilder builder, ReadOnlySpan<char> list, AnchorTier tier)
    {
        var rest = list;
        var first = true;

        while (!rest.IsEmpty)
        {
            var element = Next(ref rest);

            if (!first)
                builder.Append(',');

            builder.Append(Normalized(element, tier));
            first = false;
        }
    }

    private static string Normalized(ReadOnlySpan<char> element, AnchorTier tier)
    {
        var trimmed = element.Trim();
        var structural = tier is AnchorTier.Structural;

        return Stripped(structural ? Unnamed(trimmed) : trimmed, structural);
    }

    private static ReadOnlySpan<char> Next(ref ReadOnlySpan<char> list)
    {
        var depth = 0;
        var cut = list.Length;

        for (var index = 0; index < list.Length && cut == list.Length; index++)
        {
            depth += Nesting(list[index]);

            if (depth is 0 && list[index] is ',')
                cut = index;
        }

        var element = list[..cut];

        list = cut < list.Length ? list[(cut + 1)..] : default;

        return element;
    }

    private static int Nesting(char character) => character switch
    {
        '<' or '(' or '[' => 1,
        '>' or ')' or ']' => -1,
        _ => 0,
    };

    internal static ReadOnlySpan<char> Unnamed(ReadOnlySpan<char> element)
    {
        var cut = element.Length;

        while (cut > 0 && IsNamePart(element[cut - 1]))
            cut--;

        if (cut is 0 || cut == element.Length || !char.IsWhiteSpace(element[cut - 1]))
            return element;

        var head = element[..cut].TrimEnd();

        return head.IsEmpty || EndsWithModifier(head) ? element : head;
    }

    private static bool IsNamePart(char character) => char.IsLetterOrDigit(character) || character is '_';

    private static bool EndsWithModifier(ReadOnlySpan<char> head)
    {
        var space = head.LastIndexOfAny(' ', '\t', '\n');
        var word = space < 0 ? head : head[(space + 1)..];

        foreach (var modifier in ParameterModifiers)
        {
            if (word.Equals(modifier, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Stripped(ReadOnlySpan<char> text, bool dropNullable)
    {
        var kept = text.Length <= StackLimit ? stackalloc char[StackLimit] : new char[text.Length];
        var length = 0;

        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character) && !(dropNullable && character is '?'))
                kept[length++] = character;
        }

        return new string(kept[..length]);
    }

    public static bool SameStructure(string signature, string reference)
    {
        var actual = signature.AsSpan();
        var requested = reference.AsSpan();
        var actualOpen = actual.IndexOf('(');
        var requestedOpen = requested.IndexOf('(');
        var actualClose = actual.LastIndexOf(')');
        var requestedClose = requested.LastIndexOf(')');

        return actualOpen >= 0 && actualClose > actualOpen && requestedOpen >= 0 && requestedClose > requestedOpen
            && SameMember(actual[..actualOpen], requested[..requestedOpen])
            && SameParameters(actual[(actualOpen + 1)..actualClose], requested[(requestedOpen + 1)..requestedClose]);
    }

    private static bool SameMember(ReadOnlySpan<char> actual, ReadOnlySpan<char> requested) =>
        actual.Equals(requested[(requested.LastIndexOf('.') + 1)..].Trim(), StringComparison.Ordinal);

    private static bool SameParameters(ReadOnlySpan<char> actual, ReadOnlySpan<char> requested)
    {
        while (!actual.IsEmpty && !requested.IsEmpty)
        {
            var left = Normalized(Next(ref actual), AnchorTier.Structural);
            var right = Normalized(Next(ref requested), AnchorTier.Structural);

            if (!SymbolReference.SameSpelling(left, right))
                return false;
        }

        return actual.IsEmpty && requested.IsEmpty;
    }

    public static string FromDocumentationId(string reference)
    {
        var text = reference.AsSpan();
        var signature = text.IndexOf('~') is var returns and >= 0 ? text[..returns] : text;
        var buffer = signature.Length <= StackLimit ? stackalloc char[StackLimit] : new char[signature.Length];
        var angled = buffer[..signature.Length];

        signature.CopyTo(angled);
        angled.Replace('{', '<');
        angled.Replace('}', '>');

        return Respelled(angled);
    }

    private static string Respelled(ReadOnlySpan<char> signature)
    {
        var open = signature.IndexOf('(');
        var close = signature.LastIndexOf(')');
        var builder = new StringBuilder(signature.Length).Append(MemberName(open < 0 ? signature : signature[..open]));

        if (open < 0 || close <= open)
            return builder.ToString();

        RespelledParameters(builder.Append('('), signature[(open + 1)..close]);

        return builder.Append(')').ToString();
    }

    private static ReadOnlySpan<char> MemberName(ReadOnlySpan<char> head)
    {
        var name = head[(head.LastIndexOf('.') + 1)..];

        return name is "#ctor" ? ".ctor".AsSpan() : name;
    }

    private static void RespelledParameters(StringBuilder builder, ReadOnlySpan<char> list)
    {
        var rest = list;
        var first = true;

        while (!rest.IsEmpty)
        {
            if (!first)
                builder.Append(',');

            RespelledType(builder, Next(ref rest));
            first = false;
        }
    }

    private static void RespelledType(StringBuilder builder, ReadOnlySpan<char> element)
    {
        var open = element.IndexOf('<');
        var close = element.LastIndexOf('>');

        if (open < 0 || close < open)
        {
            RespelledLeaf(builder, element);
            return;
        }

        var nullable = element[..open] is "System.Nullable";

        if (!nullable)
            builder.Append(element[..open]).Append('<');

        RespelledParameters(builder, element[(open + 1)..close]);
        builder.Append(nullable ? '?' : '>').Append(element[(close + 1)..]);
    }

    private static void RespelledLeaf(StringBuilder builder, ReadOnlySpan<char> element)
    {
        var bracket = element.IndexOf('[');
        var name = bracket < 0 ? element : element[..bracket];

        builder.Append(Keyword(name)).Append(element[name.Length..]);
    }

    private static ReadOnlySpan<char> Keyword(ReadOnlySpan<char> name) => name switch
    {
        "System.Boolean" => "bool",
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Char" => "char",
        "System.Decimal" => "decimal",
        "System.Double" => "double",
        "System.Single" => "float",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.Object" => "object",
        "System.String" => "string",
        _ => name,
    };
}
