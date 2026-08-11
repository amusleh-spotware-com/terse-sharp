using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TerseSharp.Core;

public static class SymbolEditService
{
    public static async Task<Result<string>> ReplaceBodyAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string body,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (await RazorAsync(workspace, symbol, RazorMemberEdit.Body, body, options, cancellationToken).ConfigureAwait(false) is { } razor)
            return razor;

        var target = await TargetAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        if (target is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        var replacement = ParseBody(target.Node, body);

        return replacement is null
            ? Result.Fail<string>(Errors.Invalid("the body did not parse", "pass a block starting with '{' or an expression body"))
            : await SwapAsync(workspace, target, [replacement], options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Result<string>> ReplaceDeclarationAsync(
    LoadedWorkspace workspace,
    ISymbol symbol,
    string declaration,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        if (await RazorAsync(workspace, symbol, RazorMemberEdit.Declaration, declaration, options, cancellationToken).ConfigureAwait(false) is { } razor)
            return razor;
        var found = await TargetAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);
        if (found is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));
        var planned = Plan(found, declaration);
        return planned.IsOk
            ? await SwapAsync(workspace, planned.Value.Target, planned.Value.Nodes, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(planned.Error!);
    }

    private static SyntaxNode[] EnumRewritten(EnumMemberDeclarationSyntax[] members, SyntaxNode original) =>
    [
        members[0].WithTriviaFrom(original),
        .. members.Skip(1).Select(member => (SyntaxNode)OnANewLine(member)),
    ];

    public static async Task<Result<string>> AddMemberAsync(
        LoadedWorkspace workspace,
        ISymbol containingType,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var inserted = await RazorSymbolEdit
            .TryAddAsync(workspace, containingType, declaration, Razor(options), cancellationToken)
            .ConfigureAwait(false);

        if (inserted is { } razor)
            return razor;

        var target = await TargetAsync(workspace, containingType, cancellationToken).ConfigureAwait(false);

        if (target?.Node is EnumDeclarationSyntax enumeration)
            return await AddEnumMembersAsync(workspace, target, enumeration, declaration, options, cancellationToken).ConfigureAwait(false);

        if (target is null || target.Node is not TypeDeclarationSyntax type)
            return Result.Fail<string>(Errors.Invalid("the target is not a type declaration", "pass a type or enum symbol id"));

        var members = MemberDeclaration.ParseAll(declaration);

        return members.IsOk
            ? await SwapAsync(workspace, target, [Appended(type, members.Value!)], options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(members.Error!);
    }

    private static async Task<Result<string>> AddEnumMembersAsync(
        LoadedWorkspace workspace,
        EditTarget target,
        EnumDeclarationSyntax enumeration,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var parsed = MemberDeclaration.ParseEnumMembers(declaration);

        return parsed.IsOk
            ? await SwapAsync(workspace, target, [enumeration.AddMembers([.. parsed.Value!.Select(OnANewLine)])], options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(parsed.Error!);
    }

    private static EnumMemberDeclarationSyntax OnANewLine(EnumMemberDeclarationSyntax member) =>
        member.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

    public static async Task<Result<string>> DeleteAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        bool force,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var usages = await UsageCountAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        if (usages > 0 && !force)
            return Result.Fail<string>(UsageBlocked(symbol, usages));

        if (await RazorAsync(workspace, symbol, RazorMemberEdit.Delete, string.Empty, options, cancellationToken).ConfigureAwait(false) is { } razor)
            return razor;

        var found = await TargetAsync(workspace, symbol, cancellationToken).ConfigureAwait(false);

        if (found is null)
            return Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));

        return Shared(found) is { } refusal
            ? Result.Fail<string>(refusal)
            : await RemoveAsync(workspace, Promoted(found), options, cancellationToken).ConfigureAwait(false);
    }

    private static Task<Result<string>?> RazorAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        RazorMemberEdit edit,
        string text,
        EditOptions options,
        CancellationToken cancellationToken) =>
        RazorSymbolEdit.TryAsync(workspace, symbol, edit, text, Razor(options), cancellationToken);

    private static RazorEditOptions Razor(EditOptions options) =>
        new(options.Tool, options.DryRun, options.AllowErrors);

    private static TerseError UsageBlocked(ISymbol symbol, int usages) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{symbol.Name}' still has {usages} usages"),
        "remove the usages first, or pass force=true");

    private static async Task<int> UsageCountAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        return references.Sum(reference => reference.Locations.Count(location => !location.IsImplicit));
    }

    private static async Task<EditTarget?> TargetAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return null;

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var document = workspace.Solution.GetDocument(node.SyntaxTree);

        return document is null ? null : new EditTarget(document, node);
    }

    private static async Task<Result<string>> SwapAsync(
    LoadedWorkspace workspace,
    EditTarget target,
    IReadOnlyList<SyntaxNode> replacements,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        var planned = new PlannedEdit(target, replacements);

        if (Identical(planned) && options.Usings.IsDefaultOrEmpty)
            return Result.Ok(Unchanged(options.Tool));

        var swapped = await SwappedAsync(workspace.Solution, [planned], options.Usings, cancellationToken).ConfigureAwait(false);

        return swapped.IsOk
            ? await EditGate.ApplyAsync(workspace, swapped.Value!, [target.Document.Id], options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(swapped.Error!);
    }

    private static async Task<Result<string>> RemoveAsync(
        LoadedWorkspace workspace,
        EditTarget target,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var trimmed = root?.RemoveNode(target.Node, SyntaxRemoveOptions.KeepNoTrivia);

        if (trimmed is null)
            return Result.Fail<string>(Errors.DocumentNotFound(target.Document.FilePath ?? target.Document.Name));

        var updated = workspace.Solution.WithDocumentSyntaxRoot(target.Document.Id, trimmed);

        return await EditGate.ApplyAsync(workspace, updated, [target.Document.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static SyntaxNode? ParseBody(SyntaxNode node, string body)
    {
        var trimmed = body.Trim();

        if (trimmed.StartsWith("=>", StringComparison.Ordinal))
            return WithExpression(node, trimmed);

        if (trimmed.StartsWith('{'))
            return AsBlock(node, trimmed);

        return IsExpressionBodied(node) && WithExpression(node, "=>" + trimmed) is { } expression
            ? expression
            : AsBlock(node, "{" + body + "}");
    }

    private static SyntaxNode? WithBody(SyntaxNode node, BlockSyntax block) => Bodied(node, block) is { } bodied
    ? bodied.WithTrailingTrivia(node.GetTrailingTrivia())
    : null;

    private static SyntaxNode? Bodied(SyntaxNode node, BlockSyntax block) => node switch
    {
        MethodDeclarationSyntax method => method.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default),
        ConstructorDeclarationSyntax ctor => ctor.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default),
        AccessorDeclarationSyntax accessor => accessor.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default),
        LocalFunctionStatementSyntax local => local.WithBody(block).WithExpressionBody(null).WithSemicolonToken(default),
        _ => null,
    };

    private static MemberDeclarationSyntax Separated(MemberDeclarationSyntax member, bool blankLineBefore)
    {
        var leading = member.GetLeadingTrivia();
        var spaced = blankLineBefore
            ? leading.Insert(0, SyntaxFactory.ElasticCarriageReturnLineFeed)
            : leading;

        return member
            .WithLeadingTrivia(spaced)
            .WithTrailingTrivia(member.GetTrailingTrivia().Add(SyntaxFactory.ElasticCarriageReturnLineFeed));
    }

    private static TypeDeclarationSyntax Appended(TypeDeclarationSyntax type, IReadOnlyList<MemberDeclarationSyntax> members)
    {
        var updated = type;

        foreach (var member in members)
            updated = updated.AddMembers(Separated(member, updated.Members.Count > 0 && NeedsBlankLine(member)));

        return updated.WithCloseBraceToken(OnItsOwnLine(updated.CloseBraceToken));
    }

    private static SyntaxToken OnItsOwnLine(SyntaxToken closeBrace) =>
        closeBrace.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia)
            ? closeBrace
            : closeBrace.WithLeadingTrivia(closeBrace.LeadingTrivia.Insert(0, SyntaxFactory.ElasticCarriageReturnLineFeed));
    private static string Unchanged(string tool) => new ResponseBuilder(tool, "applied")
        .Summary(0, 0, "files changed")
        .Note("the declaration is identical to what is already there, so nothing was written")
        .ToString();
    private static async Task<SyntaxNode> IndentedAsync(Document document, CancellationToken cancellationToken)
    {
        var formatted = await Formatter.FormatAsync(document, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);

        return await formatted.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("the formatted document has no syntax root");
    }

    private static EditTarget Promoted(EditTarget target) => target.Node switch
    {
        VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field }
            when field.Declaration.Variables.Count is 1 => target with { Node = field },
        _ => target,
    };

    private static TerseError? Shared(EditTarget target) =>
        target.Node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: > 1 } declaration }
            ? Errors.Invalid(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this field shares one declaration with {declaration.Variables.Count - 1} other variable(s), so it cannot be replaced or deleted as a whole member"),
                "split the declaration into one field per line first, or edit it with edit_text force=true")
            : null;

    private static SyntaxNode? WithExpression(SyntaxNode node, string body)
    {
        var expression = SyntaxFactory.ParseExpression(body[2..].TrimEnd().TrimEnd(';'));
        if (expression.ContainsDiagnostics)
            return null;
        var arrow = SyntaxFactory.ArrowExpressionClause(expression);
        var semicolon = SyntaxFactory.Token(SyntaxKind.SemicolonToken);
        return Arrowed(node, arrow, semicolon) is { } arrowed
            ? arrowed.WithTrailingTrivia(node.GetTrailingTrivia())
            : null;
    }

    private static SyntaxNode? Arrowed(SyntaxNode node, ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon) => node switch
    {
        MethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(arrow).WithSemicolonToken(semicolon),
        ConstructorDeclarationSyntax ctor => ctor.WithBody(null).WithExpressionBody(arrow).WithSemicolonToken(semicolon),
        AccessorDeclarationSyntax accessor => accessor.WithBody(null).WithExpressionBody(arrow).WithSemicolonToken(semicolon),
        LocalFunctionStatementSyntax local => local.WithBody(null).WithExpressionBody(arrow).WithSemicolonToken(semicolon),
        _ => null,
    };

    private static bool IsExpressionBodied(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.ExpressionBody is not null,
        ConstructorDeclarationSyntax constructor => constructor.ExpressionBody is not null,
        AccessorDeclarationSyntax accessor => accessor.ExpressionBody is not null,
        LocalFunctionStatementSyntax local => local.ExpressionBody is not null,
        _ => false,
    };

    private static SyntaxNode[] Rewritten(MemberDeclarationSyntax[] members, SyntaxNode original) =>
[
    members[0].WithTriviaFrom(original),
    .. members.Skip(1).Select(member => (SyntaxNode)Separated(member, NeedsBlankLine(member))),
];

    private static SyntaxNode? AsBlock(SyntaxNode node, string text) =>
            SyntaxFactory.ParseStatement(text) is BlockSyntax parsed && !parsed.ContainsDiagnostics
                ? WithBody(node, parsed)
                : null;

    private static bool NeedsBlankLine(MemberDeclarationSyntax member) =>
            !member.GetLeadingTrivia().Any(SyntaxKind.EndOfLineTrivia);

    public static async Task<Result<string>> AddToFileAsync(
        LoadedWorkspace workspace,
        string path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var document = DocumentLookup.Find(workspace, path);

        if (document is null)
            return Result.Fail<string>(MissingDocument.Write(workspace, path));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is not CompilationUnitSyntax unit)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        var members = MemberDeclaration.ParseAll(declaration);

        if (!members.IsOk)
            return Result.Fail<string>(members.Error!);

        var appended = Spaced(members.Value!);

        return Namespaced(unit) is { } declared
            ? await SwapAsync(workspace, new EditTarget(document, declared), [Filled(declared, appended)], options, cancellationToken).ConfigureAwait(false)
            : await RootedAsync(workspace, document, unit.WithMembers(unit.Members.AddRange(appended)), options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> RootedAsync(
    LoadedWorkspace workspace,
    Document document,
    CompilationUnitSyntax unit,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        var annotated = unit.WithAdditionalAnnotations(Formatter.Annotation);
        var withUsings = UsingDirectives.Ensured(annotated, options.Usings);
        var formatted = await IndentedAsync(document.WithSyntaxRoot(withUsings), cancellationToken).ConfigureAwait(false);
        var updated = workspace.Solution.WithDocumentSyntaxRoot(document.Id, formatted);

        return await EditGate.ApplyAsync(workspace, updated, [document.Id], options, cancellationToken).ConfigureAwait(false);
    }

    private static BaseNamespaceDeclarationSyntax? Namespaced(CompilationUnitSyntax unit) =>
        unit.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();


    private static MemberDeclarationSyntax[] Spaced(IReadOnlyList<MemberDeclarationSyntax> members) =>
        [.. members.Select(member => Separated(member, true))];


    private static BaseNamespaceDeclarationSyntax Filled(
        BaseNamespaceDeclarationSyntax declared,
        IReadOnlyList<MemberDeclarationSyntax> members) => declared switch
        {
            NamespaceDeclarationSyntax block => block.WithMembers(block.Members.AddRange(members)),
            FileScopedNamespaceDeclarationSyntax scoped => scoped.WithMembers(scoped.Members.AddRange(members)),
            _ => declared,
        };

    private const int MaxBatchedEdits = 20;

    private readonly record struct PlannedEdit(EditTarget Target, IReadOnlyList<SyntaxNode> Nodes);

    private static Result<PlannedEdit> Plan(EditTarget found, string declaration) =>
        found.Node is EnumMemberDeclarationSyntax ? EnumPlan(found, declaration) : MemberPlan(found, declaration);

    private static Result<PlannedEdit> EnumPlan(EditTarget found, string declaration)
    {
        var parsed = MemberDeclaration.ParseEnumMembers(declaration);
        return parsed.IsOk
            ? Result.Ok(new PlannedEdit(found, EnumRewritten(parsed.Value!, found.Node)))
            : Result.Fail<PlannedEdit>(parsed.Error!);
    }

    private static Result<PlannedEdit> MemberPlan(EditTarget found, string declaration)
    {
        if (Shared(found) is { } refusal)
            return Result.Fail<PlannedEdit>(refusal);

        var target = Promoted(found);
        var column = target.Node.GetLocation().GetLineSpan().StartLinePosition.Character;
        var parsed = MemberDeclaration.ParseAll(MemberDeclaration.Reindented(declaration, column));

        return parsed.IsOk
            ? Result.Ok(new PlannedEdit(target, Rewritten(parsed.Value!, target.Node)))
            : Result.Fail<PlannedEdit>(parsed.Error!);
    }


    private static TerseError Mismatched(int symbolIds, int declarations) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"symbolIds has {symbolIds} entries and declarations has {declarations}, so they cannot be paired"),
        "pass one declaration per symbolId, in the same order");

    private static TerseError TooMany(int requested) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"a batch carries at most {MaxBatchedEdits} edits and {requested} were passed"),
        "split the batch, or edit the remaining members in a second call");

    public static async Task<Result<string>> ReplaceDeclarationsAsync(
    LoadedWorkspace workspace,
    IReadOnlyList<string> symbolIds,
    IReadOnlyList<string> declarations,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        if (symbolIds.Count != declarations.Count)
            return Result.Fail<string>(Mismatched(symbolIds.Count, declarations.Count));
        if (symbolIds.Count is 0 or > MaxBatchedEdits)
            return Result.Fail<string>(TooMany(symbolIds.Count));
        var planned = await PlannedAsync(workspace, symbolIds, declarations, cancellationToken).ConfigureAwait(false);
        return planned.IsOk
            ? await BatchedAsync(workspace, planned.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(planned.Error!);
    }
    private static bool Identical(PlannedEdit planned) =>
        planned.Nodes is [var only] && only.ToFullString().Equals(planned.Target.Node.ToFullString(), StringComparison.Ordinal);

    private static SyntaxNode? Applied(SyntaxNode root, IReadOnlyList<PlannedEdit> planned)
    {
        var current = root.TrackNodes(planned.Select(edit => edit.Target.Node));

        foreach (var edit in planned)
        {
            if (current.GetCurrentNode(edit.Target.Node) is not { } node)
                return null;

            current = current.ReplaceNode(node, edit.Nodes.Select(replacement => replacement.WithAdditionalAnnotations(Formatter.Annotation)));
        }

        return current;
    }

    private static async Task<Result<Solution>> SwappedAsync(
    Solution solution,
    IReadOnlyList<PlannedEdit> planned,
    System.Collections.Immutable.ImmutableArray<string> usings,
    CancellationToken cancellationToken)
    {
        var document = planned[0].Target.Document;

        if (Overlaps(planned))
            return Result.Fail<Solution>(Overlapping(document.Name));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
            return Result.Fail<Solution>(Errors.DocumentNotFound(document.FilePath ?? document.Name));

        if (Applied(root, planned) is not { } rewritten)
            return Result.Fail<Solution>(Overlapping(document.Name));

        var updated = UsingDirectives.Ensured(rewritten, usings);
        var formatted = await IndentedAsync(document.WithSyntaxRoot(updated), cancellationToken).ConfigureAwait(false);

        return Result.Ok(solution.WithDocumentSyntaxRoot(document.Id, formatted));
    }

    private static async Task<Result<PlannedEdit[]>> PlannedAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> symbolIds,
        IReadOnlyList<string> declarations,
        CancellationToken cancellationToken)
    {
        var planned = new PlannedEdit[symbolIds.Count];
        for (var index = 0; index < planned.Length; index++)
        {
            var one = await OneAsync(workspace, symbolIds[index], declarations[index], cancellationToken).ConfigureAwait(false);
            if (!one.IsOk)
                return Result.Fail<PlannedEdit[]>(one.Error!);
            planned[index] = one.Value;
        }
        return Result.Ok(planned);
    }

    private static async Task<Result<PlannedEdit>> OneAsync(
        LoadedWorkspace workspace,
        string symbolId,
        string declaration,
        CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);
        if (!symbol.IsOk)
            return Result.Fail<PlannedEdit>(symbol.Error!);
        var found = await TargetAsync(workspace, symbol.Value!, cancellationToken).ConfigureAwait(false);
        return found is null
            ? Result.Fail<PlannedEdit>(Errors.SymbolNotFound(symbolId, []))
            : Plan(found, declaration);
    }

    private static async Task<Result<string>> BatchedAsync(
    LoadedWorkspace workspace,
    PlannedEdit[] planned,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        var solution = workspace.Solution;
        var changed = new List<DocumentId>(planned.Length);
        var imports = !options.Usings.IsDefaultOrEmpty;

        foreach (var group in planned.Where(edit => imports || !Identical(edit)).GroupBy(edit => edit.Target.Document.Id))
        {
            var swapped = await SwappedAsync(solution, [.. group], options.Usings, cancellationToken).ConfigureAwait(false);

            if (!swapped.IsOk)
                return Result.Fail<string>(swapped.Error!);

            solution = swapped.Value!;
            changed.Add(group.Key);
        }

        return changed.Count is 0
            ? Result.Ok(Unchanged(options.Tool))
            : await EditGate.ApplyAsync(workspace, solution, changed, options, cancellationToken).ConfigureAwait(false);
    }

    private static TerseError Overlapping(string file) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"two of the batched edits in {file} overlap - one declaration contains the other, so applying the outer one removes the inner"),
        "send the outer declaration alone, already written the way you want the inner member, or split the batch");

    private static bool Overlaps(IReadOnlyList<PlannedEdit> planned)
    {
        for (var outer = 0; outer < planned.Count; outer++)
        {
            for (var inner = outer + 1; inner < planned.Count; inner++)
            {
                if (Encloses(planned[outer], planned[inner]) || Encloses(planned[inner], planned[outer]))
                    return true;
            }
        }
        return false;
    }

    private static bool Encloses(PlannedEdit outer, PlannedEdit inner) =>
        outer.Target.Node.Span.Contains(inner.Target.Node.Span);
}

internal sealed record EditTarget(Document Document, SyntaxNode Node);
