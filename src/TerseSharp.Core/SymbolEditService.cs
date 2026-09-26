using System.Buffers;
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
            ? Result.Fail<string>(BodyRefusal(target.Node, symbol, body))
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
        if (!planned.IsOk)
            return Result.Fail<string>(planned.Error!);
        return options.Add.IsDefaultOrEmpty
            ? await SwapAsync(workspace, planned.Value.Target, planned.Value.Nodes, options, cancellationToken).ConfigureAwait(false)
            : await BatchedAsync(workspace, [planned.Value], options, cancellationToken).ConfigureAwait(false);
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
            ? await AddedAsync(workspace, target, type, members.Value!, options, cancellationToken).ConfigureAwait(false)
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

    public static async Task<Result<string>> DeleteManyAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> symbolIds,
        bool force,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (symbolIds.Count is 0 or > MaxBatchedEdits)
            return Result.Fail<string>(TooMany(symbolIds.Count));

        var deletions = await DeletionsAsync(workspace, symbolIds, cancellationToken).ConfigureAwait(false);

        if (!deletions.IsOk)
            return Result.Fail<string>(deletions.Error!);

        if (!force && await OutsideUsageAsync(workspace, deletions.Value!, cancellationToken).ConfigureAwait(false) is { } blocked)
            return Result.Fail<string>(blocked);

        return await RemovedManyAsync(workspace, deletions.Value!, options, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct Deletion(ISymbol Symbol, EditTarget Target);

    private static async Task<Result<Deletion[]>> DeletionsAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> symbolIds,
        CancellationToken cancellationToken)
    {
        var deletions = new List<Deletion>(symbolIds.Count);

        foreach (var symbolId in symbolIds)
        {
            var deletion = await DeletionAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

            if (!deletion.IsOk)
                return Result.Fail<Deletion[]>(AtEntry(deletion.Error!, deletions.Count));

            deletions.Add(deletion.Value);
        }

        return Result.Ok(Outermost(deletions));
    }

    private static async Task<Result<Deletion>> DeletionAsync(
        LoadedWorkspace workspace,
        string symbolId,
        CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        if (!symbol.IsOk)
            return Result.Fail<Deletion>(symbol.Error!);

        if (await RazorSymbolEdit.DeclaredInRazorAsync(symbol.Value!, cancellationToken).ConfigureAwait(false))
            return Result.Fail<Deletion>(Errors.Invalid("'" + symbol.Value!.Name + "' is declared in a .razor file, which a symbolIds= batch does not edit", "delete it with delete_symbol symbolId= on its own"));

        var found = await TargetAsync(workspace, symbol.Value!, cancellationToken).ConfigureAwait(false);

        if (found is null)
            return Result.Fail<Deletion>(Errors.SymbolNotFound(symbolId, []));

        return Shared(found) is { } refusal
            ? Result.Fail<Deletion>(refusal)
            : Result.Ok(new Deletion(symbol.Value!, Promoted(found)));
    }

    private static TerseError AtEntry(TerseError error, int index) => error with
    {
        Message = string.Create(CultureInfo.InvariantCulture, $"symbolIds[{index}]: {error.Message}"),
    };

    private static Deletion[] Outermost(List<Deletion> deletions) =>
    [
        .. deletions
        .DistinctBy(deletion => deletion.Target.Node)
        .Where(deletion => !deletions.Exists(other => other.Target.Node != deletion.Target.Node && other.Target.Node.Contains(deletion.Target.Node))),
];

    private static async Task<TerseError?> OutsideUsageAsync(
        LoadedWorkspace workspace,
        Deletion[] deletions,
        CancellationToken cancellationToken)
    {
        foreach (var deletion in deletions)
        {
            var usages = await OutsideCountAsync(workspace, deletion.Symbol, deletions, cancellationToken).ConfigureAwait(false);

            if (usages > 0)
                return UsageBlocked(deletion.Symbol, usages);
        }

        return null;
    }

    private static async Task<int> OutsideCountAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        IReadOnlyList<Deletion> deleted,
        CancellationToken cancellationToken)
    {
        var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        return references.Sum(reference => reference.Locations.Count(location => !location.IsImplicit && !Inside(location.Location, deleted)));
    }

    private static bool Inside(Location location, IReadOnlyList<Deletion> deleted)
    {
        foreach (var deletion in deleted)
        {
            if (deletion.Target.Node.SyntaxTree == location.SourceTree && deletion.Target.Node.FullSpan.Contains(location.SourceSpan))
                return true;
        }

        return false;
    }

    private static async Task<Result<string>> RemovedManyAsync(
        LoadedWorkspace workspace,
        Deletion[] deletions,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var solution = workspace.Solution;
        var groups = deletions.GroupBy(deletion => deletion.Target.Document.Id).ToArray();

        foreach (var group in groups)
        {
            var trimmed = await TrimmedAsync(solution, group, cancellationToken).ConfigureAwait(false);

            if (!trimmed.IsOk)
                return Result.Fail<string>(trimmed.Error!);

            solution = trimmed.Value!;
        }

        return await EditGate.ApplyAsync(workspace, solution, [.. groups.Select(group => group.Key)], options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<Solution>> TrimmedAsync(
        Solution solution,
        IGrouping<DocumentId, Deletion> group,
        CancellationToken cancellationToken)
    {
        var document = group.First().Target.Document;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var trimmed = root?.RemoveNodes(group.Select(deletion => deletion.Target.Node), SyntaxRemoveOptions.KeepNoTrivia);

        return trimmed is null
            ? Result.Fail<Solution>(Errors.DocumentNotFound(document.FilePath ?? document.Name))
            : Result.Ok(solution.WithDocumentSyntaxRoot(group.Key, trimmed));
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

    private static Task<int> UsageCountAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken) =>
        OutsideCountAsync(workspace, symbol, [], cancellationToken);

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
            CancellationToken cancellationToken,
            bool annotate = true)
    {
        var planned = new PlannedEdit(target, replacements, annotate);

        if (Identical(planned) && options.Usings.IsDefaultOrEmpty)
            return Result.Ok(Unchanged(options.Tool));

        var swapped = await SwappedAsync(workspace.Solution, [planned], options.Usings, [], cancellationToken).ConfigureAwait(false);

        if (!swapped.IsOk)
            return Result.Fail<string>(swapped.Error!);

        var applied = await EditGate.ApplyAsync(workspace, swapped.Value!, [target.Document.Id], options, cancellationToken).ConfigureAwait(false);

        return Warned(applied, Dropped([planned]));
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

    private static TypeDeclarationSyntax Appended(TypeDeclarationSyntax type, IReadOnlyList<MemberDeclarationSyntax> members, int index)
    {
        var updated = type;
        var at = index;

        foreach (var member in members)
        {
            updated = updated.WithMembers(updated.Members.Insert(at, Separated(member, at > 0 && NeedsBlankLine(member))));
            at++;
        }

        return updated.WithCloseBraceToken(OnItsOwnLine(updated.CloseBraceToken));
    }

    private static SyntaxToken OnItsOwnLine(SyntaxToken closeBrace) =>
        StartsALine(closeBrace)
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
        LayoutKept(members[0], original).WithTriviaFrom(original),
        .. members.Skip(1).Select(member => (SyntaxNode)Separated(member, NeedsBlankLine(member))),
    ];

    private static MemberDeclarationSyntax LayoutKept(MemberDeclarationSyntax sent, SyntaxNode original) =>
        sent.RawKind == original.RawKind && ParameterListOf(sent) is { } fresh && KeptLayout(fresh, ParameterListOf(original)) is { } existing
            ? sent.ReplaceNode(fresh, existing)
            : sent;

    private static BaseParameterListSyntax? KeptLayout(BaseParameterListSyntax fresh, BaseParameterListSyntax? existing) =>
        existing is not null && OnlyLayoutDiffers(fresh, existing) ? existing.WithTrailingTrivia(fresh.GetTrailingTrivia()) : null;

    private static BaseParameterListSyntax? ParameterListOf(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => method.ParameterList,
        TypeDeclarationSyntax type => type.ParameterList,
        DelegateDeclarationSyntax @delegate => @delegate.ParameterList,
        IndexerDeclarationSyntax indexer => indexer.ParameterList,
        _ => null,
    };

    private static bool OnlyLayoutDiffers(SyntaxNode sent, SyntaxNode existing) =>
        SyntaxFactory.AreEquivalent(sent, existing, topLevel: false) && SameCommentary(sent, existing);

    private static bool SameCommentary(SyntaxNode sent, SyntaxNode existing)
    {
        using var left = Commentary(sent).GetEnumerator();
        using var right = Commentary(existing).GetEnumerator();

        while (left.MoveNext())
        {
            if (!right.MoveNext() || !left.Current.IsEquivalentTo(right.Current))
                return false;
        }

        return !right.MoveNext();
    }

    private static IEnumerable<SyntaxTrivia> Commentary(SyntaxNode node) =>
        node.DescendantTrivia().Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia));

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

        var appended = Formattable(Spaced(members.Value!));

        return await RootedAsync(workspace, document, Placed(unit, appended), options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<string>> RootedAsync(
    LoadedWorkspace workspace,
    Document document,
    CompilationUnitSyntax unit,
    EditOptions options,
    CancellationToken cancellationToken)
    {
        var withUsings = UsingDirectives.Ensured(unit, options.Usings);
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

    private readonly record struct PlannedEdit(EditTarget Target, IReadOnlyList<SyntaxNode> Nodes, bool Annotate = true);

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
        var reindented = MemberDeclaration.Reindented(declaration, column);
        var parsed = MemberDeclaration.ParseAll(MemberDeclaration.Headed(target.Node, reindented) ?? reindented);

        return parsed.IsOk
            ? Result.Ok(new PlannedEdit(target, Rewritten(parsed.Value!, target.Node)))
            : Result.Fail<PlannedEdit>(parsed.Error!);
    }

    private static TerseError Mismatched(int symbolIds, int declarations, string ids = "symbolIds") => Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{ids} has {symbolIds} entries and declarations has {declarations}, so they cannot be paired"),
            "pass one declaration per id, in the same order");

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

        var planned = await PlannedAsync(workspace, symbolIds, declarations, options.Rename, cancellationToken).ConfigureAwait(false);

        return planned.IsOk
            ? await BatchedAsync(workspace, planned.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(planned.Error!);
    }
    private static bool Identical(PlannedEdit planned) =>
        planned.Nodes is [var only] && only.ToFullString().Equals(planned.Target.Node.ToFullString(), StringComparison.Ordinal);

    private static Result<SyntaxNode> Applied(SyntaxNode root, IReadOnlyList<PlannedEdit> planned, IReadOnlyList<AppendedMembers> appended)
    {
        var targets = planned.Select(edit => edit.Target.Node);
        var current = root.TrackNodes(appended.Count is 0 ? targets : targets.Concat(appended.Select(plan => (SyntaxNode)plan.Type)));

        foreach (var edit in planned)
        {
            if (current.GetCurrentNode(edit.Target.Node) is not { } node)
                return Result.Fail<SyntaxNode>(Overlapping(edit.Target.Document.Name));

            current = current.ReplaceNode(node, edit.Nodes.Select(replacement => Annotated(replacement, edit.Annotate)));
        }

        return Grown(current, appended);
    }

    private static async Task<Result<Solution>> SwappedAsync(
                Solution solution,
                IReadOnlyList<PlannedEdit> planned,
                System.Collections.Immutable.ImmutableArray<string> usings,
                IReadOnlyList<AppendedMembers> appended,
                CancellationToken cancellationToken)
    {
        var document = planned[0].Target.Document;

        if (Overlaps(planned))
            return Result.Fail<Solution>(Overlapping(document.Name));

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
            return Result.Fail<Solution>(Errors.DocumentNotFound(document.FilePath ?? document.Name));

        var rewritten = Applied(root, planned, appended);

        if (!rewritten.IsOk)
            return Result.Fail<Solution>(rewritten.Error!);

        var updated = UsingDirectives.Ensured(rewritten.Value!, usings);
        var formatted = await IndentedAsync(document.WithSyntaxRoot(updated), cancellationToken).ConfigureAwait(false);

        return Result.Ok(solution.WithDocumentSyntaxRoot(document.Id, formatted));
    }

    private static async Task<Result<PlannedEdit[]>> PlannedAsync(
            LoadedWorkspace workspace,
            IReadOnlyList<string> symbolIds,
            IReadOnlyList<string> declarations,
            bool rename,
            CancellationToken cancellationToken)
    {
        var planned = new PlannedEdit[symbolIds.Count];

        for (var index = 0; index < planned.Length; index++)
        {
            var one = await OneAsync(workspace, symbolIds[index], declarations[index], rename, cancellationToken).ConfigureAwait(false);

            if (!one.IsOk)
                return Result.Fail<PlannedEdit[]>(Attributed(one.Error!, index));

            planned[index] = one.Value;
        }

        return Result.Ok(planned);
    }

    private static async Task<Result<PlannedEdit>> OneAsync(
            LoadedWorkspace workspace,
            string symbolId,
            string declaration,
            bool rename,
            CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveAsync(workspace, symbolId, cancellationToken).ConfigureAwait(false);

        if (!symbol.IsOk)
            return Result.Fail<PlannedEdit>(symbol.Error!);

        var found = await TargetAsync(workspace, symbol.Value!, cancellationToken).ConfigureAwait(false);

        if (found is null)
            return Result.Fail<PlannedEdit>(Errors.SymbolNotFound(symbolId, []));

        var planned = Plan(found, declaration);

        return !rename && planned.IsOk && Misnamed(symbol.Value!, planned.Value.Nodes) is { } refusal
            ? Result.Fail<PlannedEdit>(refusal)
            : planned;
    }

    private static async Task<Result<string>> BatchedAsync(
        LoadedWorkspace workspace,
        PlannedEdit[] planned,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Add.IsDefaultOrEmpty)
            return await SwappedManyAsync(workspace, planned, options, [], cancellationToken).ConfigureAwait(false);

        var routed = Routed(options.Add, options.AddTo);

        return routed.IsOk
            ? await RoutedAsync(workspace, planned, routed.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(routed.Error!);
    }

    private static async Task<Result<string>> SwappedManyAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<PlannedEdit> planned,
        EditOptions options,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var forced = !options.Usings.IsDefaultOrEmpty || appended.Count > 0;
        var edits = planned.Where(edit => forced || !Identical(edit)).GroupBy(edit => edit.Target.Document.Id).ToArray();

        if (edits.Length is 0)
            return Result.Ok(Unchanged(options.Tool));

        var landed = await LandedAsync(workspace.Solution, edits, options.Usings, appended, cancellationToken).ConfigureAwait(false);

        if (!landed.IsOk)
            return Result.Fail<string>(landed.Error!);

        var applied = await EditGate.ApplyAsync(workspace, landed.Value!, [.. edits.Select(group => group.Key)], options, cancellationToken).ConfigureAwait(false);

        return Warned(applied, Dropped(planned) + Renamed(planned));
    }

    private static async Task<Result<Solution>> SplicedAsync(
        Solution solution,
        IGrouping<DocumentId, PlannedEdit>[] edits,
        Func<DocumentId, System.Collections.Immutable.ImmutableArray<string>> usings,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var current = solution;

        foreach (var group in edits)
        {
            var swapped = await GroupedAsync(current, group, usings(group.Key), appended, cancellationToken).ConfigureAwait(false);

            if (!swapped.IsOk)
                return swapped;

            current = swapped.Value!;
        }

        return Result.Ok(current);
    }

    private static async Task<Result<Solution>> LandedAsync(
        Solution solution,
        IGrouping<DocumentId, PlannedEdit>[] edits,
        System.Collections.Immutable.ImmutableArray<string> usings,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var full = await SplicedAsync(solution, edits, _ => usings, appended, cancellationToken).ConfigureAwait(false);

        if (!full.IsOk || usings.IsDefaultOrEmpty || edits.Length < 2)
            return full;

        var needed = await NeededUsingsAsync(full.Value!, edits, usings, appended, cancellationToken).ConfigureAwait(false);

        return needed.Values.All(kept => kept.Count == usings.Length)
            ? full
            : await SplicedAsync(solution, edits, document => Landed(usings, needed, document), appended, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<DocumentId, List<string>>> NeededUsingsAsync(
        Solution spliced,
        IGrouping<DocumentId, PlannedEdit>[] edits,
        System.Collections.Immutable.ImmutableArray<string> usings,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var needed = new Dictionary<DocumentId, List<string>>(edits.Length);

        foreach (var group in edits)
            needed[group.Key] = await NeededInAsync(spliced, group, usings, appended, cancellationToken).ConfigureAwait(false);

        return needed;
    }

    private static async Task<List<string>> NeededInAsync(
        Solution spliced,
        IGrouping<DocumentId, PlannedEdit> group,
        System.Collections.Immutable.ImmutableArray<string> usings,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var baseline = await ErrorCountAsync(spliced.GetDocument(group.Key), cancellationToken).ConfigureAwait(false);
        var kept = new List<string>(usings.Length);

        foreach (var entry in usings)
        {
            if (baseline is null || await ErrorsWithoutAsync(spliced, group, usings.Remove(entry), appended, cancellationToken).ConfigureAwait(false) is not { } without || without > baseline)
                kept.Add(entry);
        }

        return kept;
    }

    private static async Task<int?> ErrorsWithoutAsync(
        Solution spliced,
        IGrouping<DocumentId, PlannedEdit> group,
        System.Collections.Immutable.ImmutableArray<string> remaining,
        IReadOnlyList<AppendedMembers> appended,
        CancellationToken cancellationToken)
    {
        var trial = await GroupedAsync(spliced, group, remaining, appended, cancellationToken).ConfigureAwait(false);

        return trial.IsOk ? await ErrorCountAsync(trial.Value!.GetDocument(group.Key), cancellationToken).ConfigureAwait(false) : null;
    }

    private static async Task<int?> ErrorCountAsync(Document? document, CancellationToken cancellationToken)
    {
        var model = document is null ? null : await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        return model?.GetDiagnostics(cancellationToken: cancellationToken).Count(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error);
    }

    private static System.Collections.Immutable.ImmutableArray<string> Landed(
        System.Collections.Immutable.ImmutableArray<string> usings,
        Dictionary<DocumentId, List<string>> needed,
        DocumentId document) =>
        [.. usings.Where(entry => needed[document].Contains(entry) || !needed.Values.Any(kept => kept.Contains(entry)))];

    private static Task<Result<Solution>> GroupedAsync(
            Solution solution,
            IGrouping<DocumentId, PlannedEdit> group,
            System.Collections.Immutable.ImmutableArray<string> usings,
            IReadOnlyList<AppendedMembers> appended,
            CancellationToken cancellationToken) =>
            SwappedAsync(
                solution,
                [.. group],
                usings,
                [.. appended.Where(plan => group.Key.Equals(plan.Document))],
                cancellationToken);

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

    private readonly record struct AppendedMembers(
                DocumentId Document,
                TypeDeclarationSyntax Type,
                IReadOnlyList<MemberDeclarationSyntax> Members,
                MemberPlacement? Placement = null);

    private static TerseError AddNotShared(BaseTypeDeclarationSyntax?[] types) => Errors.Invalid(
        types is not [_]
            ? Unshared(types)
            : "add= appends to the type that contains the replaced member, and this target's container is " + Named(types[0]) + ", which cannot take member declarations",
        "pass addTo= to name which of them takes the new members, send one call per containing type, or add the members with add_member first and replace the members afterwards");

    private static string Named(BaseTypeDeclarationSyntax? type) => type switch
    {
        null => "no containing type declaration",
        EnumDeclarationSyntax => "the enum " + type.Identifier.ValueText,
        _ => type.Identifier.ValueText,
    };

    private static TerseError AddReplacesItsOwnType() => Errors.Invalid(
        "add= appends to the type that contains the replaced member, and this call replaces that type itself",
        "write the new members into the declaration you are already sending, or append them with add_member afterwards");

    private static bool Scattered(IReadOnlyList<PlannedEdit> planned, BaseTypeDeclarationSyntax?[] types)
    {
        for (var index = 1; index < planned.Count; index++)
        {
            if (types[index] is not { } other || other.Span != types[0]!.Span || !planned[index].Target.Document.Id.Equals(planned[0].Target.Document.Id))
                return true;
        }

        return false;
    }

    private static async Task<Result<string>> RoutedAsync(
        LoadedWorkspace workspace,
        PlannedEdit[] planned,
        AddRoute[] routes,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var types = planned.Select(edit => Container(edit.Target.Node)).ToArray();
        var foreign = await ForeignAsync(workspace, [.. routes.Where(route => Foreign(planned, types, route))], options, cancellationToken).ConfigureAwait(false);

        if (!foreign.IsOk)
            return Result.Fail<string>(foreign.Error!);

        var appended = RoutedPlans(planned, [.. routes.Where(route => !Foreign(planned, types, route))], options.Placement);

        return appended.IsOk
            ? await SwappedManyAsync(workspace, [.. planned, .. foreign.Value!], options, appended.Value!, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(appended.Error!);
    }

    private static bool Foreign(IReadOnlyList<PlannedEdit> planned, BaseTypeDeclarationSyntax?[] types, AddRoute route) =>
        route.Container is { Length: > 0 } container
        && AmbiguousContainer(types, container) is null
        && Chosen(planned, types, container) < 0;

    private static async Task<Result<PlannedEdit[]>> ForeignAsync(
        LoadedWorkspace workspace,
        AddRoute[] routes,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var added = new PlannedEdit[routes.Length];

        for (var index = 0; index < added.Length; index++)
        {
            var route = routes[index];
            var one = await AdditionAsync(workspace, route.Container!, string.Join("\n\n", route.Members), options, cancellationToken).ConfigureAwait(false);

            if (!one.IsOk)
                return Result.Fail<PlannedEdit[]>(ForeignRefused(route.Container!, one.Error!));

            added[index] = one.Value;
        }

        return Result.Ok(added);
    }

    private static TerseError ForeignRefused(string container, TerseError error) => error with
    {
        Message = "addTo=" + container + " names no type add= can land in: " + error.Message,
    };

    private static Result<SyntaxNode> Grown(SyntaxNode current, AppendedMembers appended)
    {
        if (current.GetCurrentNode(appended.Type) is not TypeDeclarationSyntax type)
            return Result.Fail<SyntaxNode>(Overlapping(Path.GetFileName(appended.Type.SyntaxTree.FilePath)));

        var at = Placed(type, appended.Placement);

        return at.IsOk
            ? Result.Ok(current.ReplaceNode(type, Appended(type, Formattable(appended.Members), at.Value)))
            : Result.Fail<SyntaxNode>(PlacementLost(appended));
    }

    private static int Chosen(IReadOnlyList<PlannedEdit> planned, BaseTypeDeclarationSyntax?[] types, string? addTo)
    {
        if (addTo is not { Length: > 0 } wanted)
            return types[0] is TypeDeclarationSyntax && !Scattered(planned, types) ? 0 : -1;

        var reference = Reference(wanted);

        for (var index = 0; index < types.Length; index++)
        {
            if (types[index] is TypeDeclarationSyntax type && Addresses(type, reference))
                return index;
        }

        return -1;
    }

    private static bool Addresses(BaseTypeDeclarationSyntax type, string reference)
    {
        if (!reference.Contains('.', StringComparison.Ordinal))
            return string.Equals(type.Identifier.ValueText, reference, StringComparison.Ordinal);

        var qualified = Qualified(type).AsSpan();

        return qualified.Equals(reference, StringComparison.Ordinal)
            || (qualified.Length > reference.Length
                && qualified[^(reference.Length + 1)] is '.'
                && qualified[^reference.Length..].Equals(reference, StringComparison.Ordinal));
    }

    private static string Reference(string wanted)
    {
        var text = wanted.AsSpan();
        var colon = text.IndexOf(':');

        return new string(colon < 0 ? text : text[(colon + 1)..]);
    }

    private static string Qualified(BaseTypeDeclarationSyntax type)
    {
        var parts = new List<string>(4);

        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            if (node is BaseTypeDeclarationSyntax declaration)
                parts.Add(declaration.Identifier.ValueText);
            else if (node is BaseNamespaceDeclarationSyntax @namespace)
                parts.Add(@namespace.Name.ToString());
        }

        parts.Reverse();

        return string.Join(".", parts);
    }

    private static TerseError? AmbiguousContainer(BaseTypeDeclarationSyntax?[] types, string? addTo)
    {
        if (addTo is not { Length: > 0 } wanted)
            return null;

        var reference = Reference(wanted);
        var matched = new List<string>(2);

        foreach (var type in types)
        {
            if (type is not TypeDeclarationSyntax candidate || !Addresses(candidate, reference))
                continue;

            var qualified = Qualified(candidate);

            if (!matched.Contains(qualified, StringComparer.Ordinal))
                matched.Add(qualified);
        }

        return matched.Count > 1
            ? Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"addTo={wanted} names {matched.Count} different containing types of these targets"),
                "qualify it with the namespace so exactly one is addressed: " + string.Join(", ", matched))
            : null;
    }

    private static string Unshared(BaseTypeDeclarationSyntax?[] types) =>
        "add= appends to the type that contains the replaced member, and these targets do not share one: " + string.Join(", ", types.Select(Named));

    private static TerseError Attributed(TerseError error, int index, string ids = "symbolIds") => error with
    {
        Message = string.Create(
                CultureInfo.InvariantCulture,
                $"{(error.Code is TerseErrorCode.InvalidArgument ? "declarations" : ids)}[{index}]: {error.Message}"),
    };

    private static Result<string> Warned(Result<string> applied, string warning) =>
        applied.IsOk && warning.Length > 0 ? Result.Ok(applied.Value + warning) : applied;

    private static string Dropped(IReadOnlyList<PlannedEdit> planned)
    {
        var names = new List<string>(4);

        foreach (var edit in planned)
        {
            var kept = Attributes(edit.Nodes);

            foreach (var name in Attributes([edit.Target.Node]))
            {
                if (!kept.Contains(name, StringComparer.Ordinal) && !names.Contains(name, StringComparer.Ordinal))
                    names.Add(name);
            }
        }

        return names.Count is 0
            ? string.Empty
            : "\nWARNING attributes dropped: " + string.Join(", ", names);
    }

    private static List<string> Attributes(IReadOnlyList<SyntaxNode> nodes)
    {
        var names = new List<string>(4);

        foreach (var node in nodes)
        {
            if (node is MemberDeclarationSyntax member)
                names.AddRange(member.AttributeLists.SelectMany(list => list.Attributes).Select(attribute => attribute.Name.ToString()));
        }

        return names;
    }

    private static TerseError? NameTaken(TypeDeclarationSyntax type, IReadOnlyList<MemberDeclarationSyntax> added)
    {
        foreach (var member in added)
        {
            if (Signature(member) is not { } signature)
                continue;

            var existing = type.Members.FirstOrDefault(candidate => string.Equals(Signature(candidate), signature, StringComparison.Ordinal));

            if (existing is not null)
                return Errors.NameTaken(signature, type.Identifier.Text, existing.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
        }

        return null;
    }

    private static string? Signature(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method when Comparable(method.ExplicitInterfaceSpecifier, method.Modifiers) =>
            method.Identifier.Text + Arity(method.Arity) + Parameters(method.ParameterList),
        ConstructorDeclarationSyntax constructor => ".ctor" + Parameters(constructor.ParameterList),
        PropertyDeclarationSyntax property when Comparable(property.ExplicitInterfaceSpecifier, property.Modifiers) => property.Identifier.Text,
        FieldDeclarationSyntax field => Single(field.Declaration),
        EventFieldDeclarationSyntax declared => Single(declared.Declaration),
        BaseTypeDeclarationSyntax nested when !nested.Modifiers.Any(SyntaxKind.PartialKeyword) => nested.Identifier.Text,
        _ => null,
    };

    private static string? Single(VariableDeclarationSyntax declaration) =>
            declaration.Variables.Count is 1 ? declaration.Variables[0].Identifier.Text : null;

    private static string Parameters(ParameterListSyntax? list) => list is null
            ? "()"
            : "(" + string.Join(',', list.Parameters.Select(parameter => parameter.Modifiers.ToString() + parameter.Type)) + ")";

    private static bool Comparable(ExplicitInterfaceSpecifierSyntax? specifier, SyntaxTokenList modifiers) =>
            specifier is null && !modifiers.Any(SyntaxKind.PartialKeyword);

    private static string Arity(int arity) => arity is 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"`{arity}");

    private readonly record struct AddRoute(string? Container, IReadOnlyList<string> Members);

    private static List<string> Containers(string? addTo)
    {
        var found = new List<string>(2);

        if (addTo is not { Length: > 0 })
            return found;

        foreach (var part in addTo.AsSpan().Split(','))
        {
            var name = addTo.AsSpan()[part].Trim();

            if (!name.IsEmpty)
                found.Add(new string(name));
        }

        return found;
    }

    private static TerseError RouteMismatch(int containers, int members) => Errors.Invalid(
            string.Create(
                CultureInfo.InvariantCulture,
                $"addTo= names {containers} containing types but add= has {members} {(members is 1 ? "entry" : "entries")}"),
            "pass one addTo= per add= entry, comma-separated and in the same order, or a single addTo= that takes all of them");

    private static AddRoute[] Merged(List<string> containers, System.Collections.Immutable.ImmutableArray<string> add) =>
            [.. add
                .Select((declaration, index) => (Container: containers[index], Declaration: declaration))
                .GroupBy(entry => entry.Container, StringComparer.Ordinal)
                .Select(group => new AddRoute(group.Key, [.. group.Select(entry => entry.Declaration)]))];

    private static Result<AddRoute[]> Routed(System.Collections.Immutable.ImmutableArray<string> add, string? addTo)
    {
        var containers = Containers(addTo);

        if (containers.Count is 0)
        {
            return addTo is { Length: > 0 }
                ? Result.Fail<AddRoute[]>(BlankContainer(addTo))
                : Result.Ok<AddRoute[]>([new AddRoute(null, [.. add])]);
        }

        if (containers.Count is 1)
            return Result.Ok<AddRoute[]>([new AddRoute(containers[0], [.. add])]);

        return containers.Count == add.Length
            ? Result.Ok(Merged(containers, add))
            : Result.Fail<AddRoute[]>(RouteMismatch(containers.Count, add.Length));
    }

    private static Result<AppendedMembers> RoutePlan(
            IReadOnlyList<PlannedEdit> planned,
            BaseTypeDeclarationSyntax?[] types,
            AddRoute route,
            MemberPlacement? placement)
    {
        var target = Targeted(planned, types, route);

        if (!target.IsOk)
            return Result.Fail<AppendedMembers>(target.Error!);

        var at = Placed(target.Value.Container, placement);

        if (!at.IsOk)
            return Result.Fail<AppendedMembers>(at.Error!);

        var members = MemberDeclaration.ParseAll(string.Join("\n\n", route.Members));

        return members.IsOk
            ? Result.Ok(new AppendedMembers(target.Value.Document, target.Value.Container, members.Value!, placement))
            : Result.Fail<AppendedMembers>(members.Error!);
    }

    private readonly record struct AddTarget(DocumentId Document, TypeDeclarationSyntax Container);

    private static Result<AddTarget> Targeted(
        IReadOnlyList<PlannedEdit> planned,
        BaseTypeDeclarationSyntax?[] types,
        AddRoute route)
    {
        if (AmbiguousContainer(types, route.Container) is { } ambiguous)
            return Result.Fail<AddTarget>(ambiguous);

        var chosen = Chosen(planned, types, route.Container);

        if (chosen < 0 || types[chosen] is not TypeDeclarationSyntax container)
            return Result.Fail<AddTarget>(AddNotShared(types));

        var document = planned[chosen].Target.Document.Id;

        return planned.Any(edit => edit.Target.Document.Id == document && edit.Target.Node.Span == container.Span)
            ? Result.Fail<AddTarget>(AddReplacesItsOwnType())
            : Result.Ok(new AddTarget(document, container));
    }

    private static Result<AppendedMembers[]> RoutedPlans(
            IReadOnlyList<PlannedEdit> planned,
            IReadOnlyList<AddRoute> routes,
            MemberPlacement? placement)
    {
        var types = planned.Select(edit => Container(edit.Target.Node)).ToArray();
        var plans = new AppendedMembers[routes.Count];

        for (var index = 0; index < routes.Count; index++)
        {
            var one = RoutePlan(planned, types, routes[index], placement);

            if (!one.IsOk)
                return Result.Fail<AppendedMembers[]>(one.Error!);

            plans[index] = one.Value;
        }

        return Result.Ok(plans);
    }

    private static Result<SyntaxNode> Grown(SyntaxNode current, IReadOnlyList<AppendedMembers> appended)
    {
        foreach (var plan in appended)
        {
            var grown = Grown(current, plan);

            if (!grown.IsOk)
                return grown;

            current = grown.Value!;
        }

        return Result.Ok(current);
    }

    private static TerseError BlankContainer(string addTo) => Errors.Invalid(
            "addTo=" + addTo + " names no containing type - every comma-separated entry was blank",
            "name one containing type per add= entry, or drop addTo= to append to the container the targets share");

    private static TerseError BodyRefusal(SyntaxNode node, ISymbol symbol, string body)
    {
        if (!HasReplaceableBody(node))
            return Errors.NoBody(SymbolId.From(symbol).Value, node.Kind().ToString());

        var (text, errors) = BodyErrors(body);

        return errors.Length is 0
            ? Errors.Invalid("the body did not parse", "pass a block starting with '{' or an expression body")
            : MemberDeclaration.MalformedBody(errors, text);
    }

    private static bool HasReplaceableBody(SyntaxNode node) =>
        node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax;

    private static (string Text, Diagnostic[] Errors) BodyErrors(string body)
    {
        var trimmed = body.Trim();

        if (trimmed.StartsWith("=>", StringComparison.Ordinal))
            return (trimmed, BodyDiagnostics(SyntaxFactory.ParseExpression(trimmed[2..])));

        var text = trimmed.StartsWith('{') ? trimmed : "{" + body + "}";

        return (text, BodyDiagnostics(SyntaxFactory.ParseStatement(text)));
    }

    private static Diagnostic[] BodyDiagnostics(SyntaxNode parsed) =>
        [.. parsed.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)];

    private static string? DeclaredName(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        PropertyDeclarationSyntax property => property.Identifier.Text,
        EventDeclarationSyntax declared => declared.Identifier.Text,
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        EnumMemberDeclarationSyntax member => member.Identifier.Text,
        BaseFieldDeclarationSyntax field => Single(field.Declaration),
        _ => null,
    };

    private static TerseError? Misnamed(ISymbol symbol, IReadOnlyList<SyntaxNode> nodes)
    {
        string? declared = null;

        foreach (var node in nodes)
        {
            if (DeclaredName(node) is not { } name)
                return null;

            if (string.Equals(name, symbol.Name, StringComparison.Ordinal))
                return null;

            declared ??= name;
        }

        return declared is null ? null : Errors.Misnamed(declared, symbol.Name);
    }

    private static string Renamed(IReadOnlyList<PlannedEdit> planned)
    {
        var moved = new List<string>(2);

        foreach (var edit in planned)
        {
            if (Moved(edit) is { } rename && !moved.Contains(rename, StringComparer.Ordinal))
                moved.Add(rename);
        }

        return moved.Count is 0 ? string.Empty : "\nNOTE renamed: " + string.Join(", ", moved);
    }

    private static string? Moved(PlannedEdit edit)
    {
        if (edit.Nodes.Count is not 1 || DeclaredName(edit.Target.Node) is not { } before || DeclaredName(edit.Nodes[0]) is not { } after)
            return null;

        return string.Equals(before, after, StringComparison.Ordinal) ? null : before + " -> " + after;
    }

    private static BaseTypeDeclarationSyntax? Container(SyntaxNode node) => node is BaseTypeDeclarationSyntax type
        ? node.Parent?.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>() ?? type
        : node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>();

    private readonly record struct RegionScan(int Depth, int Opened);

    private static RegionScan Stepped(RegionScan scan, SyntaxKind kind, int index) => kind switch
    {
        SyntaxKind.RegionDirectiveTrivia => new RegionScan(scan.Depth + 1, scan.Depth is 0 ? index : scan.Opened),
        SyntaxKind.EndRegionDirectiveTrivia when scan.Depth <= 1 => new RegionScan(0, -1),
        SyntaxKind.EndRegionDirectiveTrivia => new RegionScan(scan.Depth - 1, scan.Opened),
        _ => scan,
    };

    private static RegionScan Scanned(RegionScan scan, SyntaxTriviaList leading, int index)
    {
        var current = scan;

        foreach (var trivia in leading)
            current = Stepped(current, trivia.Kind(), index);

        return current;
    }

    private static int OutsideRegions(TypeDeclarationSyntax type)
    {
        var scan = new RegionScan(0, -1);

        for (var index = 0; index < type.Members.Count; index++)
            scan = Scanned(scan, type.Members[index].GetLeadingTrivia(), index);

        return scan.Depth > 0 && scan.Opened >= 0 ? scan.Opened : type.Members.Count;
    }

    private static int AfterFields(TypeDeclarationSyntax type)
    {
        var last = -1;

        for (var index = 0; index < type.Members.Count; index++)
        {
            if (type.Members[index] is FieldDeclarationSyntax)
                last = index;
        }

        return last + 1;
    }

    private static int Cut(ReadOnlySpan<char> text)
    {
        var at = text.IndexOfAny(NameMarkers);

        return at < 0 ? text.Length : at;
    }

    private static ReadOnlySpan<char> Plain(ReadOnlySpan<char> signature)
    {
        var head = signature[..Cut(signature)];
        var dot = head.LastIndexOf('.');

        return dot < 0 ? head : head[(dot + 1)..];
    }

    private static bool Names(MemberDeclarationSyntax member, string reference, AnchorTier tier) =>
            Signature(member) is { } signature && tier switch
            {
                AnchorTier.Exact => signature.AsSpan().Equals(reference, StringComparison.Ordinal),
                AnchorTier.Name => Plain(signature).Equals(Plain(reference), StringComparison.Ordinal),
                _ => string.Equals(AnchorSignature.Canonical(signature, tier), AnchorSignature.Canonical(reference, tier), StringComparison.Ordinal),
            };

    private static string PlacementCandidates(TypeDeclarationSyntax type)
    {
        var names = new List<string>(Math.Min(type.Members.Count, MaxPlacementCandidates));
        var total = 0;

        foreach (var member in type.Members)
        {
            if (Signature(member) is not { } signature)
                continue;

            total++;

            if (names.Count < MaxPlacementCandidates)
                names.Add(signature);
        }

        return names.Count is 0
            ? "it declares none that a name can address"
            : string.Create(CultureInfo.InvariantCulture, $"showing {names.Count} of {total}: {string.Join(", ", names)}");
    }

    private const int MaxPlacementCandidates = 10;

    private static TerseError PlacementNotFound(TypeDeclarationSyntax type, string wanted, string parameter) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"{parameter}={wanted} names no member of {type.Identifier.ValueText}"),
        "pass a member this type declares - " + PlacementCandidates(type) + " - or drop it to append at the end");

    private static Result<int> Indexed(TypeDeclarationSyntax type, string wanted, int offset, string parameter)
    {
        var reference = Reference(wanted);
        var widest = new AnchorMatch(-1, -1, 0);

        foreach (var tier in AnchorTiers)
        {
            widest = Anchored(type, reference, tier);

            if (widest.Adjacent)
                return Result.Ok(offset is 0 ? widest.First : widest.Last + 1);
        }

        return Result.Fail<int>(widest.Count is 0
            ? PlacementNotFound(type, wanted, parameter)
            : PlacementAmbiguous(type, wanted, parameter, reference));
    }

    private static int Positioned(TypeDeclarationSyntax type, MemberPosition position) => position switch
    {
        MemberPosition.First => 0,
        MemberPosition.AfterFields => AfterFields(type),
        _ => OutsideRegions(type),
    };

    private static Result<int> Placed(TypeDeclarationSyntax type, MemberPlacement? placement)
    {
        if (placement is not { } wanted)
            return Result.Ok(OutsideRegions(type));

        return (wanted.Before, wanted.After) switch
        {
            ({ Length: > 0 } before, _) => Indexed(type, before, 0, wanted.BeforeName),
            (_, { Length: > 0 } after) => Indexed(type, after, 1, wanted.AfterName),
            _ => Result.Ok(Positioned(type, wanted.Position)),
        };
    }

    private static async Task<Result<string>> AddedAsync(
        LoadedWorkspace workspace,
        EditTarget target,
        TypeDeclarationSyntax type,
        IReadOnlyList<MemberDeclarationSyntax> members,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (NameTaken(type, members) is { } taken)
            return Result.Fail<string>(taken);

        var at = Placed(type, options.Placement);

        return at.IsOk
            ? await SwapAsync(workspace, target, [Appended(type, Formattable(members), at.Value)], options, cancellationToken, annotate: false).ConfigureAwait(false)
            : Result.Fail<string>(at.Error!);
    }

    private static readonly SearchValues<char> NameMarkers = SearchValues.Create("(<`~");

    private static TerseError PlacementAmbiguous(TypeDeclarationSyntax type, string wanted, string parameter, string reference) => Errors.Invalid(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{parameter}={wanted} names {AmbiguousAnchors(type, reference)}, which do not sit next to each other, so the insertion point is not decided"),
            "pass one of those, or any spelling of its parameter list - whitespace, parameter names and nullable annotations are ignored; adjacent overloads need no parameter list at all");

    private readonly record struct AnchorMatch(int First, int Last, int Count)
    {
        public AnchorMatch With(int index) => Count is 0 ? new AnchorMatch(index, index, 1) : new AnchorMatch(First, index, Count + 1);

        public bool Adjacent => Count > 0 && Last - First + 1 == Count;
    }

    private static AnchorMatch Anchored(TypeDeclarationSyntax type, string reference, AnchorTier tier)
    {
        var match = new AnchorMatch(-1, -1, 0);

        for (var index = 0; index < type.Members.Count; index++)
        {
            if (Names(type.Members[index], reference, tier))
                match = match.With(index);
        }

        return match;
    }

    private static string AmbiguousAnchors(TypeDeclarationSyntax type, string reference)
    {
        var names = new List<string>(4);

        foreach (var member in type.Members)
        {
            if (Names(member, reference, AnchorTier.Name) && Signature(member) is { } signature)
                names.Add(signature);
        }

        return string.Join(" and ", names);
    }

    private static MemberDeclarationSyntax[] Formattable(IReadOnlyList<MemberDeclarationSyntax> members) =>
        [.. members.Select(member => member.WithAdditionalAnnotations(Formatter.Annotation))];

    private static CompilationUnitSyntax Placed(CompilationUnitSyntax unit, IReadOnlyList<MemberDeclarationSyntax> appended) =>
        Namespaced(unit) is { } declared
            ? unit.ReplaceNode(declared, Filled(declared, appended))
            : unit.WithMembers(unit.Members.AddRange(appended));

    private static SyntaxNode Annotated(SyntaxNode replacement, bool annotate) =>
        annotate ? replacement.WithAdditionalAnnotations(Formatter.Annotation) : replacement;

    private static bool StartsALine(SyntaxToken closeBrace) =>
        closeBrace.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia)
        || closeBrace.GetPreviousToken().TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia);

    private static readonly AnchorTier[] AnchorTiers = [AnchorTier.Exact, AnchorTier.Spacing, AnchorTier.Structural, AnchorTier.Name];

    public static async Task<Result<string>> AddMembersAsync(
            LoadedWorkspace workspace,
            IReadOnlyList<string> typeSymbolIds,
            IReadOnlyList<string> declarations,
            EditOptions options,
            CancellationToken cancellationToken)
    {
        if (typeSymbolIds.Count != declarations.Count)
            return Result.Fail<string>(Mismatched(typeSymbolIds.Count, declarations.Count, "typeSymbolIds"));

        if (typeSymbolIds.Count is 0 or > MaxBatchedEdits)
            return Result.Fail<string>(TooMany(typeSymbolIds.Count));

        var planned = await AdditionsAsync(workspace, typeSymbolIds, declarations, options, cancellationToken).ConfigureAwait(false);

        return planned.IsOk
            ? await SwappedManyAsync(workspace, planned.Value!, options, [], cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(planned.Error!);
    }

    private static async Task<Result<PlannedEdit[]>> AdditionsAsync(
            LoadedWorkspace workspace,
            IReadOnlyList<string> typeSymbolIds,
            IReadOnlyList<string> declarations,
            EditOptions options,
            CancellationToken cancellationToken)
    {
        var planned = new PlannedEdit[typeSymbolIds.Count];

        for (var index = 0; index < planned.Length; index++)
        {
            var one = await AdditionAsync(workspace, typeSymbolIds[index], declarations[index], options, cancellationToken).ConfigureAwait(false);

            if (!one.IsOk)
                return Result.Fail<PlannedEdit[]>(Attributed(one.Error!, index, "typeSymbolIds"));

            planned[index] = one.Value;
        }

        return Result.Ok(planned);
    }

    private static async Task<Result<PlannedEdit>> AdditionAsync(
            LoadedWorkspace workspace,
            string typeSymbolId,
            string declaration,
            EditOptions options,
            CancellationToken cancellationToken)
    {
        var symbol = await SymbolLookup.ResolveAsync(workspace, typeSymbolId, null, cancellationToken, typesOnly: true).ConfigureAwait(false);

        if (!symbol.IsOk)
            return Result.Fail<PlannedEdit>(symbol.Error!);

        var target = await TargetAsync(workspace, symbol.Value!, cancellationToken).ConfigureAwait(false);

        return target?.Node is TypeDeclarationSyntax type
            ? Insertion(target, type, declaration, options)
            : Result.Fail<PlannedEdit>(Errors.Invalid(
                "the target is not a type declaration",
                "pass a type symbol id - an enum container and a path= file take one add_member call each"));
    }

    private static Result<PlannedEdit> Insertion(EditTarget target, TypeDeclarationSyntax type, string declaration, EditOptions options)
    {
        var members = MemberDeclaration.ParseAll(MemberDeclaration.Reindented(declaration, type.GetLocation().GetLineSpan().StartLinePosition.Character));

        if (!members.IsOk)
            return Result.Fail<PlannedEdit>(members.Error!);

        if (NameTaken(type, members.Value!) is { } taken)
            return Result.Fail<PlannedEdit>(taken);

        var at = Placed(type, options.Placement);

        return at.IsOk
            ? Result.Ok(new PlannedEdit(target, [Appended(type, Formattable(members.Value!), at.Value)], false))
            : Result.Fail<PlannedEdit>(at.Error!);
    }

    private static TerseError PlacementLost(AppendedMembers appended) => Errors.Invalid(
            string.Create(
                CultureInfo.InvariantCulture,
                $"the anchor {Anchor(appended.Placement)} resolved before the replacement and no longer does after it, so where the add= members belong is not decided"),
            "anchor on a member this edit does not rewrite, send the helpers with add_member after the replacement, or drop the placement to append them at the end of the type");

    private static string Anchor(MemberPlacement? placement) => placement switch
    {
        { Before: { Length: > 0 } before } => "addBefore=" + before,
        { After: { Length: > 0 } after } => "addAfter=" + after,
        _ => "the requested placement",
    };
}

internal sealed record EditTarget(Document Document, SyntaxNode Node);
