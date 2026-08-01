using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public enum RazorMemberEdit
{
    Body,
    Declaration,
    Delete,
    Add,
}

public static class RazorSymbolEdit
{
    public static async Task<Result<string>?> TryAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        RazorMemberEdit edit,
        string text,
        RazorEditOptions options,
        CancellationToken cancellationToken)
    {
        var syntax = await DeclarationAsync(symbol, cancellationToken).ConfigureAwait(false);

        if (syntax is null || Mapped(syntax) is not { } mapped)
            return null;

        var opened = await RazorOpen.AtAsync(workspace, mapped.Path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var context = opened.Value!;
        var updated = Rewrite(context.Document, syntax, mapped, edit, text);

        return updated.IsOk
            ? await RazorEditGate.ApplyAsync(context, updated.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(updated.Error!);
    }

    public static async Task<Result<string>?> TryAddAsync(
        LoadedWorkspace workspace,
        ISymbol containingType,
        string declaration,
        RazorEditOptions options,
        CancellationToken cancellationToken)
    {
        if (containingType is not INamedTypeSymbol type)
            return null;

        var path = await RazorGeneratedMap.PathOfAsync(workspace, type, cancellationToken).ConfigureAwait(false);

        if (path is null)
            return null;

        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var context = opened.Value!;

        return await RazorEditGate
            .ApplyAsync(context, IntoCode(context.Document, declaration.Trim()), options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string IntoCode(RazorDocument document, string declaration)
    {
        var block = document.CodeBlocks.LastOrDefault(candidate => candidate.Keyword is "code" or "functions");

        if (block is null)
            return document.Text.TrimEnd() + "\n\n@code {\n    " + declaration + "\n}\n";

        var close = block.BodySpan.End;

        return document.Text.Insert(close, "\n    " + declaration + "\n");
    }

    private static Result<string> Rewrite(
        RazorDocument document,
        SyntaxNode syntax,
        FileLinePositionSpan mapped,
        RazorMemberEdit edit,
        string text)
    {
        var span = Span(document, mapped);

        return edit switch
        {
            RazorMemberEdit.Delete => Result.Ok(Cut(document.Text, span)),
            RazorMemberEdit.Declaration => Result.Ok(Replace(document.Text, span, text.Trim())),
            RazorMemberEdit.Add => Append(document, span, text.Trim()),
            _ => Body(document, syntax, span, text.Trim()),
        };
    }

    private static Result<string> Body(RazorDocument document, SyntaxNode syntax, TextSpan member, string body)
    {
        if (Mapped(syntax) is not { } declaration)
            return Result.Fail<string>(Unmapped());

        var source = document.Text[member.Start..member.End];
        var open = source.IndexOf('{', StringComparison.Ordinal);
        var close = source.LastIndexOf('}');

        if (open < 0 || close <= open)
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{declaration.Path}' declares this member without a block body"),
                "use replace_symbol to replace the whole declaration"));
        }

        return Result.Ok(Replace(document.Text, new TextSpan(member.Start + open, close + 1 - open), body.Trim()));
    }

    private static Result<string> Append(RazorDocument document, TextSpan member, string declaration)
    {
        var end = member.End;
        var indent = Indent(document.Text, member.Start);

        return Result.Ok(document.Text.Insert(end, "\n\n" + indent + declaration));
    }

    private static string Replace(string text, TextSpan span, string replacement) =>
        text.Remove(span.Start, span.Length).Insert(span.Start, replacement);

    private static string Cut(string text, TextSpan span)
    {
        var end = span.End;

        while (end < text.Length && text[end] is '\r' or '\n')
            end++;

        return text.Remove(span.Start, end - span.Start);
    }

    private static string Indent(string text, int start)
    {
        var from = start;

        while (from > 0 && text[from - 1] is not '\n')
            from--;

        var to = from;

        while (to < text.Length && text[to] is ' ' or '\t')
            to++;

        return text[from..to];
    }

    private static TextSpan Span(RazorDocument document, FileLinePositionSpan mapped)
    {
        var start = document.OffsetOf(mapped.StartLinePosition.Line, mapped.StartLinePosition.Character);
        var end = document.OffsetOf(mapped.EndLinePosition.Line, mapped.EndLinePosition.Character);

        return TextSpan.FromBounds(Math.Min(start, document.Text.Length), Math.Min(end, document.Text.Length));
    }

    private static async Task<SyntaxNode?> DeclarationAsync(ISymbol symbol, CancellationToken cancellationToken)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
            return null;

        var syntax = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);

        return syntax is MemberDeclarationSyntax or VariableDeclaratorSyntax ? syntax : null;
    }

    private static FileLinePositionSpan? Mapped(SyntaxNode syntax)
    {
        var mapped = syntax.GetLocation().GetMappedLineSpan();

        return mapped.HasMappedPath && RazorDocument.IsRazor(mapped.Path) ? mapped : null;
    }

    private static TerseError Unmapped() => Errors.Invalid(
        "this member is generated code with no Razor source span",
        "edit the .razor file with razor_set_attribute, or its code-behind with replace_symbol_body");
}
