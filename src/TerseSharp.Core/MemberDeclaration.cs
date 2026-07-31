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
}
