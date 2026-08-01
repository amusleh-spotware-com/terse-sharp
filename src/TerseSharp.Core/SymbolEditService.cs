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
            : await SwapAsync(workspace, target, replacement, options, cancellationToken).ConfigureAwait(false);
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

        if (Shared(found) is { } refusal)
            return Result.Fail<string>(refusal);

        var target = Promoted(found);
        var parsed = MemberDeclaration.Parse(declaration);

        return parsed.IsOk
            ? await SwapAsync(workspace, target, parsed.Value!.WithTriviaFrom(target.Node), options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(parsed.Error!);
    }

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

        if (target is null || target.Node is not TypeDeclarationSyntax type)
            return Result.Fail<string>(Errors.Invalid("the target is not a type declaration", "pass a type symbol id"));

        var member = MemberDeclaration.Parse(declaration);

        return member.IsOk
            ? await SwapAsync(workspace, target, Appended(type, member.Value!), options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(member.Error!);
    }

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
        SyntaxNode replacement,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var root = await target.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
            return Result.Fail<string>(Errors.DocumentNotFound(target.Document.FilePath ?? target.Document.Name));

        if (replacement.ToFullString().Equals(target.Node.ToFullString(), StringComparison.Ordinal))
            return Result.Ok(Unchanged(options.Tool));

        var swapped = root.ReplaceNode(target.Node, replacement.WithAdditionalAnnotations(Formatter.Annotation));
        var formatted = await IndentedAsync(target.Document.WithSyntaxRoot(swapped), cancellationToken).ConfigureAwait(false);
        var updated = workspace.Solution.WithDocumentSyntaxRoot(target.Document.Id, formatted);

        return await EditGate.ApplyAsync(workspace, updated, [target.Document.Id], options, cancellationToken).ConfigureAwait(false);
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
        var block = SyntaxFactory.ParseStatement(body.TrimStart().StartsWith('{') ? body : "{" + body + "}");

        return block is BlockSyntax parsed ? WithBody(node, parsed) : null;
    }

    private static SyntaxNode? WithBody(SyntaxNode node, BlockSyntax block) => node switch
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

    private static TypeDeclarationSyntax Appended(TypeDeclarationSyntax type, MemberDeclarationSyntax member) =>
        type.AddMembers(Separated(member, type.Members.Count > 0)).WithCloseBraceToken(OnItsOwnLine(type.CloseBraceToken));

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
}

internal sealed record EditTarget(Document Document, SyntaxNode Node);
