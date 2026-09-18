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
}
