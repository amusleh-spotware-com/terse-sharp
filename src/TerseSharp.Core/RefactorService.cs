using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

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

        return symbol switch
        {
            INamedTypeSymbol { ContainingType: not null } nested when LiftRefusal(nested, targetNamespace) is { } refusal => Result.Fail<string>(refusal),
            INamedTypeSymbol { ContainingType: not null } nested => await LiftAsync(workspace, nested, (MemberDeclarationSyntax)node, options, cancellationToken).ConfigureAwait(false),
            _ => await RenameNamespaceAsync(workspace, document, targetNamespace, options, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<Result<string>> RenameNamespaceAsync(
            LoadedWorkspace workspace,
            Document document,
            string targetNamespace,
            EditOptions options,
            CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var declaration = root?.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        if (root is null || declaration is null)
            return Result.Fail<string>(Errors.Invalid("the file has no namespace declaration", "add one first"));

        var renamed = declaration.WithName(SyntaxFactory.ParseName(targetNamespace).WithTriviaFrom(declaration.Name));
        var updated = workspace.Solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNode(declaration, renamed));

        return await EditGate.ApplyAsync(workspace, updated, [document.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> LiftAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol nested,
        MemberDeclarationSyntax node,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder.FindReferencesAsync(nested, workspace.Solution, cancellationToken).ConfigureAwait(false);
        var sites = references.SelectMany(reference => reference.Locations).Where(site => !site.IsImplicit).ToLookup(site => site.Document.Id);
        var declaring = workspace.Solution.GetDocumentId(node.SyntaxTree)!;
        var solution = await UnqualifiedAsync(workspace.Solution, sites, declaring, cancellationToken).ConfigureAwait(false);
        var lifted = await LiftedDocumentAsync(solution, node, sites[declaring], NamespaceAccessibility(nested), cancellationToken).ConfigureAwait(false);
        DocumentId[] changed = [.. sites.Select(site => site.Key).Append(declaring).Distinct()];

        return await EditGate.ApplyAsync(workspace, lifted, changed, options, cancellationToken).ConfigureAwait(false);
    }

    private static TerseError? LiftRefusal(INamedTypeSymbol nested, string targetNamespace) => nested switch
    {
        { ContainingType.ContainingType: not null } => Errors.Invalid(nested.Name + " is nested more than one level deep", "lift " + nested.ContainingType.Name + " first, then call again"),
        { ContainingType.IsGenericType: true } => Errors.Invalid(nested.ContainingType.Name + " is generic, so " + nested.Name + " can use its type parameters", "declare " + nested.Name + " at namespace level with add_member path= instead"),
        { DeclaringSyntaxReferences.Length: > 1 } => Errors.Invalid(nested.Name + " is partial across several declarations", "merge them into one first"),
        _ when targetNamespace.Length > 0 && !targetNamespace.Equals(nested.ContainingNamespace.ToDisplayString(), StringComparison.Ordinal) =>
            Errors.Invalid(nested.Name + " is nested, so it is lifted into its own file's namespace " + nested.ContainingNamespace.ToDisplayString(), "pass targetNamespace=" + nested.ContainingNamespace.ToDisplayString() + " or omit it, then call again on the lifted type to move it"),
        _ when !nested.ContainingNamespace.GetTypeMembers(nested.Name, nested.Arity).IsEmpty => Errors.Invalid(nested.ContainingNamespace.ToDisplayString() + " already declares " + nested.Name, "rename_symbol one of them first"),
        _ => null,
    };

    private static async Task<Solution> UnqualifiedAsync(
        Solution solution,
        ILookup<DocumentId, ReferenceLocation> sites,
        DocumentId declaring,
        CancellationToken cancellationToken)
    {
        var updated = solution;

        foreach (var site in sites.Where(site => !site.Key.Equals(declaring) && site.First().Document is not SourceGeneratedDocument))
        {
            var root = await site.First().Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is not null)
                updated = updated.WithDocumentSyntaxRoot(site.Key, root.ReplaceNodes(Qualified(root, site), (_, rewritten) => Unqualified(rewritten)));
        }

        return updated;
    }

    private static async Task<Solution> LiftedDocumentAsync(
        Solution solution,
        MemberDeclarationSyntax node,
        IEnumerable<ReferenceLocation> sites,
        SyntaxKind accessibility,
        CancellationToken cancellationToken)
    {
        var root = await node.SyntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var id = solution.GetDocumentId(node.SyntaxTree)!;

        return solution.WithDocumentSyntaxRoot(id, LiftedRoot(root, node, Qualified(root, sites), accessibility));
    }

    private static SyntaxNode LiftedRoot(SyntaxNode root, MemberDeclarationSyntax node, SyntaxNode[] qualified, SyntaxKind accessibility)
    {
        var moving = new SyntaxAnnotation();
        var anchor = new SyntaxAnnotation();
        var container = node.Parent!;
        var column = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Character;
        SyntaxNode[] targets = [.. qualified, node, container];
        var marked = root.ReplaceNodes(targets, (original, rewritten) =>
            original == node ? rewritten.WithAdditionalAnnotations(moving)
            : original == container ? rewritten.WithAdditionalAnnotations(anchor)
            : Unqualified(rewritten));
        var moved = marked.GetAnnotatedNodes(moving).OfType<MemberDeclarationSyntax>().Single();
        var trimmed = marked.RemoveNode(moved, SyntaxRemoveOptions.KeepNoTrivia)!;
        var outer = trimmed.GetAnnotatedNodes(anchor).Single();

        return trimmed.InsertNodesAfter(outer, [Dedented(Lifted(moved, accessibility, EndOfLine(outer)), column)]);
    }

    private static SyntaxNode[] Qualified(SyntaxNode root, IEnumerable<ReferenceLocation> sites) =>
        [.. sites
            .Select(site => root.FindNode(site.Location.SourceSpan, getInnermostNodeForTie: true))
            .Select(name => name.Parent switch
            {
                QualifiedNameSyntax qualified when qualified.Right == name => (SyntaxNode)qualified,
                MemberAccessExpressionSyntax access when access.Name == name => access,
                _ => null,
            })
            .OfType<SyntaxNode>()];


    private static SyntaxNode Unqualified(SyntaxNode qualified) => qualified switch
    {
        QualifiedNameSyntax { Left: QualifiedNameSyntax container } name => name.WithLeft(container.Left),
        QualifiedNameSyntax name => name.Right.WithTriviaFrom(name),
        MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax container } access => access.WithExpression(container.Expression),
        MemberAccessExpressionSyntax access => access.Name.WithTriviaFrom(access),
        _ => qualified,
    };

    private static MemberDeclarationSyntax Lifted(MemberDeclarationSyntax declaration, SyntaxKind accessibility, SyntaxTrivia endOfLine)
    {
        var comments = declaration.GetLeadingTrivia().SkipWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        var modifiers = declaration.Modifiers
            .Where(modifier => !IsNestedOnly(modifier.Kind()))
            .Prepend(SyntaxFactory.Token(accessibility).WithTrailingTrivia(SyntaxFactory.Space));

        return declaration.WithoutLeadingTrivia()
            .WithModifiers(SyntaxFactory.TokenList(modifiers))
            .WithLeadingTrivia(comments.Prepend(endOfLine));
    }

    private static SyntaxTrivia EndOfLine(SyntaxNode node) =>
        node.GetLastToken().TrailingTrivia.FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)) is { RawKind: not 0 } found
            ? found
            : SyntaxFactory.ElasticCarriageReturnLineFeed;


    private static bool IsNestedOnly(SyntaxKind modifier) =>
        modifier is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword or SyntaxKind.PublicKeyword or SyntaxKind.NewKeyword;


    private static SyntaxKind NamespaceAccessibility(INamedTypeSymbol nested) =>
        (nested.DeclaredAccessibility, nested.ContainingType.DeclaredAccessibility) is (Accessibility.Public, Accessibility.Public)
            ? SyntaxKind.PublicKeyword
            : SyntaxKind.InternalKeyword;

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

    private static MemberDeclarationSyntax Dedented(MemberDeclarationSyntax declaration, int column)
    {
        if (column is 0 || SpansLines(declaration))
            return declaration;

        var text = declaration.ToFullString().Replace("\n" + new string(' ', column), "\n", StringComparison.Ordinal);

        return SyntaxFactory.ParseMemberDeclaration(text) is { } parsed ? parsed : declaration;
    }

    private static bool SpansLines(MemberDeclarationSyntax declaration) =>
        declaration.DescendantTokens().Any(token =>
            token.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.InterpolatedStringTextToken
            && token.Text.Contains('\n', StringComparison.Ordinal));
}
