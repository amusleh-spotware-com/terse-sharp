using System.Text;

namespace TerseSharp.Core;

public static class UnifiedDiff
{
    public static string Between(string path, string before, string after)
    {
        var beforeLines = Split(before);
        var afterLines = Split(after);
        var text = new StringBuilder(256);

        text.Append(CultureInfo.InvariantCulture, $"--- {path}\n+++ {path}\n");

        AppendHunks(text, beforeLines, afterLines);

        return text.ToString();
    }

    public static int ChangedLines(string before, string after)
    {
        var beforeLines = Split(before);
        var afterLines = Split(after);
        var common = CommonPrefix(beforeLines, afterLines);
        var suffix = CommonSuffix(beforeLines, afterLines, common);

        return Math.Max(beforeLines.Length - common - suffix, afterLines.Length - common - suffix);
    }

    private static void AppendHunks(StringBuilder text, string[] beforeLines, string[] afterLines)
    {
        var prefix = CommonPrefix(beforeLines, afterLines);
        var suffix = CommonSuffix(beforeLines, afterLines, prefix);
        var removed = beforeLines[prefix..(beforeLines.Length - suffix)];
        var added = afterLines[prefix..(afterLines.Length - suffix)];

        text.Append(CultureInfo.InvariantCulture, $"@@ -{prefix + 1},{removed.Length} +{prefix + 1},{added.Length} @@\n");

        foreach (var line in removed)
            text.Append('-').Append(line).Append('\n');

        foreach (var line in added)
            text.Append('+').Append(line).Append('\n');
    }

    private static int CommonPrefix(string[] before, string[] after)
    {
        var limit = Math.Min(before.Length, after.Length);
        var index = 0;

        while (index < limit && string.Equals(before[index], after[index], StringComparison.Ordinal))
            index++;

        return index;
    }

    private static int CommonSuffix(string[] before, string[] after, int prefix)
    {
        var limit = Math.Min(before.Length, after.Length) - prefix;
        var index = 0;

        while (index < limit
            && string.Equals(before[^(index + 1)], after[^(index + 1)], StringComparison.Ordinal))
            index++;

        return index;
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
