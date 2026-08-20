using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class RefactorService
{
    public static async Task<Result<string>> ExtractInterfaceAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string interfaceName,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (symbol is not INamedTypeSymbol type)
            return Result.Fail<string>(Errors.Invalid("the symbol is not a type", "pass a type symbol id"));

        var members = PublicInstanceMembers(type);

        if (members.Length is 0)
            return Result.Fail<string>(Errors.Invalid("the type has no public instance members", "nothing to extract"));

        return await CreateSiblingAsync(
            workspace, type, interfaceName, InterfaceDeclaration(interfaceName, members), options, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<Result<string>> MoveTypeToFileAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var located = await TypeNodeAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        if (located is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var (document, node) = located.Value;

        if (Path.GetFileNameWithoutExtension(document.FilePath ?? string.Empty).Equals(symbol.Name, StringComparison.Ordinal))
            return Result.Fail<string>(Errors.Invalid("the type already lives in its own file", "nothing to move"));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var trimmed = root?.RemoveNode(node, SyntaxRemoveOptions.KeepNoTrivia);

        if (trimmed is null)
            return Result.Fail<string>(Errors.DocumentNotFound(document.FilePath ?? document.Name));

        var moved = workspace.Solution.WithDocumentSyntaxRoot(document.Id, trimmed);
        var created = AddSibling(moved, document, symbol.Name, Unit(root!, (MemberDeclarationSyntax)node));

        return await EditGate
            .ApplyAsync(workspace, created.Solution, [document.Id, created.Id], options, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<Result<string>> MoveTypeToNamespaceAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string targetNamespace,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var located = await TypeNodeAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        if (located is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var (document, node) = located.Value;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var declaration = root?.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        if (root is null || declaration is null)
            return Result.Fail<string>(Errors.Invalid("the file has no namespace declaration", "add one first"));

        var renamed = declaration.WithName(SyntaxFactory.ParseName(targetNamespace).WithTrailingTrivia(SyntaxFactory.Space));
        var updated = workspace.Solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNode(declaration, renamed));

        return await EditGate.ApplyAsync(workspace, updated, [document.Id], options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Result<string>> ChangeSignatureAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string parameters,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        var node = reference is null ? null : await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);

        if (node is not MethodDeclarationSyntax method)
            return Result.Fail<string>(Errors.Invalid("the symbol is not a method", "pass a method symbol id"));

        var parsed = SyntaxFactory.ParseParameterList("(" + parameters + ")");

        if (parsed.ContainsDiagnostics)
            return Result.Fail<string>(Errors.Invalid("the parameter list did not parse", "pass e.g. 'int count, string name'"));

        var document = workspace.Solution.GetDocument(node.SyntaxTree);

        return document is null
            ? Result.Fail<string>(Errors.DocumentNotFound(node.SyntaxTree.FilePath))
            : await SwapAsync(workspace, document, method, method.WithParameterList(parsed), options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> SwapAsync(
        LoadedWorkspace workspace,
        Document document,
        SyntaxNode original,
        SyntaxNode replacement,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
            return Result.Fail<string>(Errors.DocumentNotFound(document.FilePath ?? document.Name));

        var updated = workspace.Solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNode(original, replacement));

        return await EditGate.ApplyAsync(workspace, updated, [document.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> CreateSiblingAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        string name,
        MemberDeclarationSyntax declaration,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var located = await TypeNodeAsync(workspace, type, cancellationToken).ConfigureAwait(false);

        if (located is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(type).Value, []));

        var (document, _) = located.Value;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var created = AddSibling(workspace.Solution, document, name, Unit(root!, declaration));

        return await EditGate.ApplyAsync(workspace, created.Solution, [created.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static (Solution Solution, DocumentId Id) AddSibling(
        Solution solution,
        Document sibling,
        string name,
        CompilationUnitSyntax unit)
    {
        var id = DocumentId.CreateNewId(sibling.Project.Id);
        var directory = Path.GetDirectoryName(sibling.FilePath ?? string.Empty) ?? string.Empty;
        var full = Path.Combine(directory, name + ".cs");

        var updated = solution.AddDocument(
            id,
            name + ".cs",
            unit.NormalizeWhitespace(),
            folders: DocumentPlacement.Folders(sibling.Project, full),
            filePath: full);

        return (updated, id);
    }

    private static CompilationUnitSyntax Unit(SyntaxNode root, MemberDeclarationSyntax member)
    {
        var original = root as CompilationUnitSyntax ?? SyntaxFactory.CompilationUnit();
        var declaration = original.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        var body = declaration is null
            ? (MemberDeclarationSyntax)member
            : SyntaxFactory.FileScopedNamespaceDeclaration(declaration.Name).AddMembers(member);

        return SyntaxFactory.CompilationUnit().WithUsings(original.Usings).AddMembers(body);
    }

    private static async Task<(Document Document, SyntaxNode Node)?> TypeNodeAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return null;

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var document = workspace.Solution.GetDocument(node.SyntaxTree);

        return document is null ? null : (document, node);
    }

    private static ISymbol[] PublicInstanceMembers(INamedTypeSymbol type) =>
        [.. type.GetMembers().Where(member =>
            member.DeclaredAccessibility is Accessibility.Public
            && !member.IsStatic
            && !member.IsImplicitlyDeclared
            && member.Kind is SymbolKind.Method or SymbolKind.Property
            && member is not IMethodSymbol { MethodKind: not MethodKind.Ordinary })];

    private static InterfaceDeclarationSyntax InterfaceDeclaration(string name, ISymbol[] members)
    {
        var declarations = members.Select(Signature).OfType<MemberDeclarationSyntax>().ToArray();

        return SyntaxFactory
            .InterfaceDeclaration(name)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddMembers(declarations);
    }

    private static MemberDeclarationSyntax? Signature(ISymbol member) => member switch
    {
        IMethodSymbol method => SyntaxFactory.ParseMemberDeclaration(
            $"{method.ReturnType.ToDisplayString()} {method.Name}({Parameters(method)});"),
        IPropertySymbol property => SyntaxFactory.ParseMemberDeclaration(
            $"{property.Type.ToDisplayString()} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : "set; ")}}}"),
        _ => null,
    };

    private static string Parameters(IMethodSymbol method) =>
        string.Join(", ", method.Parameters.Select(parameter => $"{parameter.Type.ToDisplayString()} {parameter.Name}"));
}
