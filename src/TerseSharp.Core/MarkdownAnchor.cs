using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static partial class MarkdownAnchor
{
    private const int StackLimit = 256;

    public static string Of(ReadOnlySpan<char> heading)
    {
        var trimmed = heading.TrimStart('#').Trim();

        return trimmed.Contains("](", StringComparison.Ordinal)
            ? Slug(LinkTarget().Replace(trimmed.ToString(), "]"))
            : Slug(trimmed);
    }

    private static string Slug(ReadOnlySpan<char> text)
    {
        var buffer = text.Length <= StackLimit ? stackalloc char[StackLimit] : new char[text.Length];
        var written = 0;

        foreach (var character in text)
        {
            if (character is ' ' or '-')
                buffer[written++] = '-';
            else if (char.IsLetterOrDigit(character) || character is '_')
                buffer[written++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..written]);
    }

    [GeneratedRegex(@"\]\([^)]*\)")]
    private static partial Regex LinkTarget();
}
