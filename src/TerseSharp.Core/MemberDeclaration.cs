using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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

        return errors.Length is 0 ? Result.Ok(parsed) : Result.Fail<MemberDeclarationSyntax>(Malformed(errors, declaration));
    }

    public static string? Headed(SyntaxNode target, string header)
    {
        if (target is not BaseTypeDeclarationSyntax { OpenBraceToken: { RawKind: (int)SyntaxKind.OpenBraceToken, IsMissing: false } } existing)
            return null;

        return SyntaxFactory.ParseMemberDeclaration(header, consumeFullText: false) is BaseTypeDeclarationSyntax parsed
            && IsHeaderOf(parsed, existing)
            && header.AsSpan(parsed.FullSpan.End).IsWhiteSpace()
                ? string.Concat(header.AsSpan().TrimEnd(), Body(existing))
                : null;
    }

    private static bool IsHeaderOf(BaseTypeDeclarationSyntax parsed, BaseTypeDeclarationSyntax existing) =>
        parsed.RawKind == existing.RawKind
        && parsed.OpenBraceToken.IsMissing
        && parsed.SemicolonToken is { RawKind: 0 } or { IsMissing: true };

    private static string Body(BaseTypeDeclarationSyntax existing) => existing.SyntaxTree.GetText().ToString(
        TextSpan.FromBounds(existing.OpenBraceToken.GetPreviousToken().Span.End, existing.Span.End));

    private static TerseError Trailing(string trailing) => Errors.Invalid(
        "the declaration is not exactly one member; it is followed by " + Excerpt(trailing),
        "pass one complete member declaration; call the tool once per member");

    private static TerseError Malformed(Diagnostic[] errors, string declaration) => Errors.Invalid(
        "the declaration did not parse: "
            + string.Join("; ", errors.Take(3).Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)))
            + Where(declaration, errors[0].Location.SourceSpan.Start),
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
                return Result.Fail<MemberDeclarationSyntax[]>(Malformed(errors, remaining));

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

    public static string Reindented(string declaration, int column)
    {
        if (column <= 0 || !Dedented(declaration))
            return declaration;

        var continued = Continued(declaration);
        var lines = declaration.Split('\n');

        return new string(' ', column) + Shifted(lines, column - Frame(declaration, lines, continued, column), continued);
    }

    private static string Shifted(string[] lines, int shift, HashSet<int> continued)
    {
        var builder = new System.Text.StringBuilder(lines.Sum(line => line.Length + 1) + (Math.Max(shift, 0) * lines.Length));

        for (var line = 0; line < lines.Length; line++)
        {
            if (line > 0)
                builder.Append('\n');

            var text = lines[line].AsSpan();

            builder.Append(line > 0 && !text.IsWhiteSpace() && !continued.Contains(line) ? Moved(text, shift, builder) : text);
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

    private const int Window = 40;

    private static string Where(string declaration, int at)
    {
        if (at < 0 || at > declaration.Length)
            return string.Empty;

        var from = Math.Max(0, at - Window);
        var to = Math.Min(declaration.Length, at + Window);
        var head = from > 0 ? "..." : string.Empty;
        var tail = to < declaration.Length ? "..." : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $" at offset {at} of {declaration.Length}: {head}{Flattened(declaration[from..to])}{tail}");
    }

    private static string Flattened(string excerpt) => excerpt
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    public static TerseError MalformedBody(Diagnostic[] errors, string body) => Errors.Invalid(
        "the body did not parse: "
            + string.Join("; ", errors.Take(3).Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)))
            + Where(body, errors[0].Location.SourceSpan.Start),
        "the offset above is where the parser stopped - on a truncated body that is the unbalanced brace; pass a block starting with '{', an expression body as '=> expr', or bare statements");

    private const int MemberIndent = 4;

    private static int Frame(string declaration, string[] lines, HashSet<int> continued, int column) => (Header(declaration), Braces(declaration)) switch
    {
        ( < int.MaxValue and var header, _) => header,
        (_, < int.MaxValue and var braces) => braces,
        _ => Shallowest(lines, continued) is var shallowest && shallowest != int.MaxValue && shallowest >= column + MemberIndent ? column : 0,
    };

    private static int Header(string declaration)
    {
        var header = int.MaxValue;

        for (var offset = 0; offset < declaration.Length;)
        {
            var member = SyntaxFactory.ParseMemberDeclaration(declaration[offset..], consumeFullText: false);

            if (member is null || member.FullSpan.End is 0)
                return header;

            header = HeaderColumn(declaration, offset, member, header);
            offset += member.FullSpan.End;
        }

        return header;
    }

    private static int HeaderColumn(string declaration, int offset, MemberDeclarationSyntax member, int header)
    {
        foreach (var list in member.AttributeLists)
            header = Math.Min(header, Opening(declaration, offset + list.SpanStart));

        var head = member.AttributeLists.Count is 0 ? member.GetFirstToken() : member.AttributeLists.Last().GetLastToken().GetNextToken();

        return Math.Min(header, Opening(declaration, offset + head.SpanStart));
    }

    private static int Braces(string declaration)
    {
        var braces = int.MaxValue;
        var nesting = 0;
        var framing = new Stack<bool>();
        var previous = SyntaxKind.None;

        foreach (var token in SyntaxFactory.ParseTokens(declaration))
        {
            nesting += Nesting(token.Kind());
            braces = Framing(token.Kind(), previous, nesting, framing) ? Math.Min(braces, Opening(declaration, token.SpanStart)) : braces;
            previous = token.Kind();
        }

        return braces;
    }

    private static int Nesting(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken => 1,
        SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken => -1,
        _ => 0,
    };

    private static bool Framing(SyntaxKind kind, SyntaxKind previous, int nesting, Stack<bool> framing) => kind switch
    {
        SyntaxKind.CloseBraceToken => framing.Count is 0 || framing.Pop(),
        SyntaxKind.OpenBraceToken => Pushed(framing, nesting is 0 && Opens(framing, previous)),
        _ => false,
    };

    private static bool Opens(Stack<bool> framing, SyntaxKind previous) => (framing.Count is 0 || framing.Peek()) && previous is not SyntaxKind.EqualsGreaterThanToken;

    private static bool Pushed(Stack<bool> framing, bool frames)
    {
        framing.Push(frames);

        return frames;
    }

    private static int Opening(ReadOnlySpan<char> declaration, int position)
    {
        var before = declaration[..position];
        var newline = before.LastIndexOf('\n');

        return newline >= 0 && before[(newline + 1)..].IsWhiteSpace() ? position - newline - 1 : int.MaxValue;
    }

    private static int Shallowest(string[] lines, HashSet<int> continued)
    {
        var shallowest = int.MaxValue;

        for (var line = 1; line < lines.Length; line++)
        {
            var trimmed = lines[line].AsSpan().TrimStart();

            if (!continued.Contains(line) && !trimmed.IsWhiteSpace())
                shallowest = Math.Min(shallowest, lines[line].Length - trimmed.Length);
        }

        return shallowest;
    }

    private static ReadOnlySpan<char> Moved(ReadOnlySpan<char> line, int shift, System.Text.StringBuilder builder)
    {
        if (shift >= 0)
        {
            builder.Append(' ', shift);

            return line;
        }

        var lead = line.Length - line.TrimStart().Length;

        return line[Math.Min(lead, -shift)..];
    }
}
