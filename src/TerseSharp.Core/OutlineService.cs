using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class OutlineService
{
    public static async Task<Result<string>> FileAsync(
        LoadedWorkspace workspace,
        string path,
        bool signatures,
        string ids,
        CancellationToken cancellationToken)
    {
        var document = DocumentLookup.Find(workspace, path);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        if (Rejected(ids) is { } refusal)
            return refusal;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        return Result.Ok(Render("get_file_outline", path, Declarations(root), model, signatures, ids));
    }

    public static async Task<Result<string>> TypeAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        bool signatures,
        string ids,
        CancellationToken cancellationToken)
    {
        if (Rejected(ids) is { } refusal)
            return refusal;

        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var document = workspace.Solution.GetDocument(node.SyntaxTree);
        var model = document is null
            ? null
            : await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        return model is null || node is not MemberDeclarationSyntax declaration || !IsTypeDeclaration(declaration)
            ? Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []))
            : Result.Ok(Render("get_type_outline", symbol.Name, [declaration], model, signatures, ids));
    }

    private static Result<string>? Rejected(string ids) =>
        ids is "short" or "full"
            ? null
            : Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"ids='{ids}' is not a known value"),
                "pass ids=short for names every tool accepts, or ids=full for documentation ids"));

    private static MemberDeclarationSyntax[] Declarations(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTypeDeclaration)];

    private static bool IsTypeDeclaration(MemberDeclarationSyntax member) =>
        member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    private static string Render(
        string tool,
        string argument,
        MemberDeclarationSyntax[] declarations,
        SemanticModel model,
        bool signatures,
        string ids)
    {
        var response = new ResponseBuilder(tool, argument);

        response.Summary(declarations.Length, declarations.Length, "types");

        foreach (var declaration in declarations)
            AppendType(response, declaration, model, signatures, ids);

        return response.ToString();
    }

    private static void AppendType(
        ResponseBuilder response,
        MemberDeclarationSyntax declaration,
        SemanticModel model,
        bool signatures,
        string ids)
    {
        var symbol = model.GetDeclaredSymbol(declaration);

        if (symbol is null)
            return;

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{Reference(symbol, ids)}  {SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)}  :{PositionFormat.LineRange(declaration)}"));

        foreach (var member in Members(declaration))
            AppendMember(response, member, model, signatures, ids);
    }

    private static IEnumerable<MemberDeclarationSyntax> Members(MemberDeclarationSyntax declaration) => declaration switch
    {
        TypeDeclarationSyntax type => type.Members,
        EnumDeclarationSyntax enumeration => enumeration.Members,
        _ => [],
    };

    private static void AppendMember(
        ResponseBuilder response,
        MemberDeclarationSyntax member,
        SemanticModel model,
        bool signatures,
        string ids)
    {
        var symbol = model.GetDeclaredSymbol(member);

        if (symbol is null || IsTypeDeclaration(member))
            return;

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"  {Reference(symbol, ids)}  {Signature(symbol, signatures)} :{PositionFormat.LineRange(member)}"));
    }

    private static string Reference(ISymbol symbol, string ids) =>
        string.Equals(ids, "full", StringComparison.OrdinalIgnoreCase) || !SymbolReference.RoundTrips(symbol)
            ? SymbolId.From(symbol).Value
            : SymbolReference.Brief(symbol);

    private static string Signature(ISymbol symbol, bool signatures) => signatures
        ? string.Create(CultureInfo.InvariantCulture, $"{SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)} ")
        : string.Create(CultureInfo.InvariantCulture, $"{SymbolFormat.Accessibility(symbol)} ");
}
