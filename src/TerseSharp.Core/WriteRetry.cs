using System.Text;

namespace TerseSharp.Core;

public static class WriteRetry
{
    public static string WithUsings(string content, IReadOnlyList<string> usings)
    {
        if (usings.Count is 0 || content.Length is 0)
            return content;

        var ending = LineEndings.Dominant(content);
        var directives = new StringBuilder();

        foreach (var name in usings)
        {
            var directive = "using " + name + ";";

            if (!Declares(content, directive))
                directives.Append(directive).Append(ending);
        }

        return directives.Length is 0 ? content : directives.Append(content).ToString();
    }

    private static bool Declares(string content, string directive)
    {
        foreach (var line in content.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            var end = trimmed.IndexOf(';');

            if (end >= 0 && trimmed[..(end + 1)].Equals(directive, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
