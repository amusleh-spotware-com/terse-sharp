using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class MemberDeclaration
{
    public static Result<MemberDeclarationSyntax> Parse(string declaration)
    {
        var parsed = SyntaxFactory.ParseMemberDeclaration(declaration, consumeFullText: false);

        if (parsed is null)
            return Result.Fail<MemberDeclarationSyntax>(Errors.Invalid("the declaration did not parse", "pass a complete member declaration"));

        var trailing = declaration[parsed.FullSpan.End..].Trim();

        if (trailing.Length > 0)
            return Result.Fail<MemberDeclarationSyntax>(Trailing(trailing));

        var errors = parsed.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error).ToArray();

        return errors.Length is 0 ? Result.Ok(parsed) : Result.Fail<MemberDeclarationSyntax>(Malformed(errors));
    }

    private static TerseError Trailing(string trailing) => Errors.Invalid(
        "the declaration is not exactly one member; it is followed by " + Excerpt(trailing),
        "pass one complete member declaration; call the tool once per member");

    private static TerseError Malformed(Diagnostic[] errors) => Errors.Invalid(
        "the declaration did not parse: " + string.Join("; ", errors.Take(3).Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture))),
        "pass a complete, syntactically valid member declaration");

    private static string Excerpt(string trailing) =>
        trailing.Length <= 60 ? trailing : trailing[..60] + "...";

    public static Result<MemberDeclarationSyntax[]> ParseAll(string declarations)
    {
        var members = new List<MemberDeclarationSyntax>();
        var remaining = declarations;

        while (remaining.Trim().Length > 0)
        {
            var parsed = SyntaxFactory.ParseMemberDeclaration(remaining, consumeFullText: false);

            if (parsed is null || parsed.FullSpan.End is 0)
                return Result.Fail<MemberDeclarationSyntax[]>(Unparsed());

            if (Fatal(parsed) is { Length: > 0 } errors)
                return Result.Fail<MemberDeclarationSyntax[]>(Malformed(errors));

            members.Add(parsed);
            remaining = remaining[parsed.FullSpan.End..];
        }

        return members.Count is 0
            ? Result.Fail<MemberDeclarationSyntax[]>(Unparsed())
            : Result.Ok(members.ToArray());
    }

    private static Diagnostic[] Fatal(MemberDeclarationSyntax parsed) =>
            [.. parsed.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)];

    private static TerseError Unparsed() => Errors.Invalid(
            "the declaration did not parse",
            "pass one or more complete member declarations");

    public static Result<EnumMemberDeclarationSyntax[]> ParseEnumMembers(string declarations)
    {
        var wrapped = SyntaxFactory.ParseCompilationUnit("enum TerseEnumerationProbe\n{\n" + declarations.Trim().TrimEnd(',') + ",\n}");

        if (wrapped.Members is not [EnumDeclarationSyntax parsed] || Fatal(parsed) is { Length: > 0 })
            return Result.Fail<EnumMemberDeclarationSyntax[]>(NotEnumMembers());

        return parsed.Members.Count is 0
            ? Result.Fail<EnumMemberDeclarationSyntax[]>(NotEnumMembers())
            : Result.Ok<EnumMemberDeclarationSyntax[]>([.. parsed.Members]);
    }

    private static Diagnostic[] Fatal(EnumDeclarationSyntax parsed) =>
        [.. parsed.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)];


    private static TerseError NotEnumMembers() => Errors.Invalid(
        "the declaration did not parse as enum members",
        "pass one or more enum member names, e.g. 'Internal' or 'Internal = 3, Retry'");

    public static string Reindented(string declaration, int column) =>
    column <= 0 || !Dedented(declaration) ? declaration : Shifted(declaration, column, Continued(declaration));

    private static string Shifted(string declaration, int column, HashSet<int> continued)
    {
        var lines = declaration.Split('\n');
        var builder = new System.Text.StringBuilder(declaration.Length + (column * lines.Length));

        for (var line = 0; line < lines.Length; line++)
        {
            if (line > 0)
                builder.Append('\n');

            if (line > 0 && lines[line].Trim().Length > 0 && !continued.Contains(line))
                builder.Append(' ', column);

            builder.Append(lines[line]);
        }

        return builder.ToString();
    }

    private static bool Dedented(string declaration)
    {
        var lines = declaration.Split('\n');

        return lines.Length > 1
            && lines[0].Length > 0
            && !char.IsWhiteSpace(lines[0][0])
            && Array.Exists(lines[1..], line => line.Trim().Length > 0);
    }

    private static HashSet<int> Continued(string declaration)
    {
        var inside = new HashSet<int>();
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(declaration);

        foreach (var token in SyntaxFactory.ParseTokens(declaration))
            Span(inside, text, token);

        return inside;
    }

    private static void Span(HashSet<int> inside, Microsoft.CodeAnalysis.Text.SourceText text, SyntaxToken token)
    {
        if (!token.Text.Contains('\n', StringComparison.Ordinal))
            return;

        var span = text.Lines.GetLinePositionSpan(token.Span);

        for (var line = span.Start.Line + 1; line <= span.End.Line; line++)
            inside.Add(line);
    }
}
