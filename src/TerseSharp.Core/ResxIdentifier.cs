using Microsoft.CodeAnalysis.CSharp;

namespace TerseSharp.Core;

public static class ResxIdentifier
{
    public static string From(string name)
    {
        var sanitized = string.Create(name.Length, name, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
                span[index] = char.IsLetterOrDigit(source[index]) ? source[index] : '_';
        });

        return sanitized is [var first, ..] && (char.IsDigit(first) || Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(sanitized) is not SyntaxKind.None)
            ? "_" + sanitized
            : sanitized;
    }
}
