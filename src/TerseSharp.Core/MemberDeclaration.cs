using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class MemberDeclaration
{
    public static Result<MemberDeclarationSyntax> Parse(string declaration)
    {
        var parsed = SyntaxFactory.ParseMemberDeclaration(declaration);

        if (parsed is null)
            return Result.Fail<MemberDeclarationSyntax>(Errors.Invalid("the declaration did not parse", "pass a complete member declaration"));

        var errors = parsed.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error).ToArray();

        return errors.Length is 0
            ? Result.Ok(parsed)
            : Result.Fail<MemberDeclarationSyntax>(Rejected(errors));
    }

    private static TerseError Rejected(Diagnostic[] errors) => Errors.Invalid(
        "the declaration is not exactly one member: " + string.Join("; ", errors.Take(3).Select(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture))),
        "pass one complete member declaration; call the tool once per member");
}
