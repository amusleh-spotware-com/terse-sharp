using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class OutlineService
{
    public static async Task<Result<string>> FileAsync(
        LoadedWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var document = DocumentLookup.Find(workspace, path);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        return Result.Ok(Render("get_file_outline", path, Types(root), model));
    }

    public static async Task<Result<string>> TypeAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var document = workspace.Solution.GetDocument(node.SyntaxTree);
        var model = document is null
            ? null
            : await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        return model is null
            ? Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []))
            : Result.Ok(Render("get_type_outline", symbol.Name, [(TypeDeclarationSyntax)node], model));
    }

    private static TypeDeclarationSyntax[] Types(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<TypeDeclarationSyntax>()];

    private static string Render(
        string tool,
        string argument,
        TypeDeclarationSyntax[] types,
        SemanticModel model)
    {
        var response = new ResponseBuilder(tool, argument);

        response.Summary(types.Length, types.Length, "types");

        foreach (var type in types)
            AppendType(response, type, model);

        return response.ToString();
    }

    private static void AppendType(ResponseBuilder response, TypeDeclarationSyntax type, SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol(type);

        if (symbol is null)
            return;

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolId.From(symbol)}  {SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)}  :{PositionFormat.LineRange(type)}"));

        foreach (var member in type.Members)
            AppendMember(response, member, model);
    }

    private static void AppendMember(ResponseBuilder response, MemberDeclarationSyntax member, SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol(member);

        if (symbol is null || member is TypeDeclarationSyntax)
            return;

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"  {SymbolId.From(symbol)}  {SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol)}  :{PositionFormat.LineRange(member)}"));
    }
}
