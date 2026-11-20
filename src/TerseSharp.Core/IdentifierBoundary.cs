namespace TerseSharp.Core;

internal static class IdentifierBoundary
{
    public static bool Splits(ReadOnlySpan<char> line, int start, ReadOnlySpan<char> head) =>
        start > 0 && Part(line[start - 1]) && Part(head[0]);

    private static bool Part(char character) => character is '_' || char.IsLetterOrDigit(character);
}
