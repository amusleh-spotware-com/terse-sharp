namespace TerseSharp.Core;

public static class RazorEditService
{
    public static Task<Result<string>> SetAttributeAsync(
        LoadedWorkspace workspace,
        string path,
        string target,
        string attribute,
        string? value,
        RazorEditOptions options,
        CancellationToken cancellationToken) =>
        OnElementAsync(
            workspace,
            path,
            target,
            options,
            (context, element) => Result.Ok(Attribute(context.Document.Text, element, attribute, value)),
            cancellationToken);

    public static Task<Result<string>> AddElementAsync(
        LoadedWorkspace workspace,
        string path,
        string parent,
        string markup,
        string position,
        RazorEditOptions options,
        CancellationToken cancellationToken) =>
        OnElementAsync(
            workspace,
            path,
            parent,
            options,
            (context, element) => Insert(context.Document, element, markup.Trim(), position),
            cancellationToken);

    public static Task<Result<string>> RemoveElementAsync(
        LoadedWorkspace workspace,
        string path,
        string target,
        RazorEditOptions options,
        CancellationToken cancellationToken) =>
        OnElementAsync(
            workspace,
            path,
            target,
            options,
            (context, element) => Remove(context.Document.Text, element),
            cancellationToken);

    public static async Task<Result<string>> SetDirectiveAsync(
        LoadedWorkspace workspace,
        string path,
        string directive,
        string? value,
        bool remove,
        RazorEditOptions options,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var context = opened.Value!;
        var updated = Directive(context.Document, directive.TrimStart('@'), value, remove);

        return updated.IsOk
            ? await RazorEditGate.ApplyAsync(context, updated.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(updated.Error!);
    }

    private static async Task<Result<string>> OnElementAsync(
        LoadedWorkspace workspace,
        string path,
        string target,
        RazorEditOptions options,
        Func<RazorContext, RazorElement, Result<string>> rewrite,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var context = opened.Value!;
        var located = Locate(context, target);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var updated = rewrite(context, located.Value!);

        return updated.IsOk
            ? await RazorEditGate.ApplyAsync(context, updated.Value!, options, cancellationToken).ConfigureAwait(false)
            : Result.Fail<string>(updated.Error!);
    }

    private static Result<RazorElement> Locate(RazorContext context, string target)
    {
        var matches = context.Document.Matches(target);

        if (matches > 1 && context.Document.Locate(target) is null)
        {
            return Result.Fail<RazorElement>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{target}' matches {matches} elements"),
                "address one element by the path razor_outline prints, or by #ref"));
        }

        return context.Document.Locate(target) is { } element
            ? Result.Ok(element)
            : Result.Fail<RazorElement>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{target}' matches no element in this file"),
                "use razor_outline to see the element paths"));
    }

    private static string Attribute(string text, RazorElement element, string name, string? value)
    {
        if (element.Attribute(name) is { } existing)
            return value is null ? Cut(text, existing) : Replace(text, existing, name, value);

        return value is null ? text : Add(text, element, name, value);
    }

    private static string Cut(string text, RazorAttributeInfo attribute)
    {
        var start = attribute.Span.Start;

        while (start > 0 && text[start - 1] is ' ' or '\t')
            start--;

        return text.Remove(start, attribute.Span.End - start);
    }

    private static string Replace(string text, RazorAttributeInfo attribute, string name, string value) => text
        .Remove(attribute.Span.Start, attribute.Span.Length)
        .Insert(attribute.Span.Start, Pair(name, value));

    private static string Add(string text, RazorElement element, string name, string value)
    {
        var point = element.Span.End - (element.SelfClosing ? 2 : 1);
        var separated = point > 0 && text[point - 1] is ' ' or '\t' ? Pair(name, value) : " " + Pair(name, value);
        var spaced = element.SelfClosing ? separated + " " : separated;

        return text.Insert(point, spaced);
    }

    private static string Pair(string name, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}=\"{value}\"");

    private static Result<string> Insert(RazorDocument document, RazorElement element, string markup, string position)
    {
        var text = document.Text;
        var closing = RazorMarkup.CloseOf(text, element);
        var inner = ChildIndent(document, element);

        return position switch
        {
            "first" => Result.Ok(text.Insert(element.Span.End, "\n" + inner + markup)),
            "before" => Result.Ok(text.Insert(element.Span.Start, markup + "\n" + Indent(text, element))),
            "after" => Result.Ok(text.Insert(closing, "\n" + Indent(text, element) + markup)),
            _ => Last(document, element, markup, closing, inner),
        };
    }

    private static Result<string> Last(
        RazorDocument document,
        RazorElement element,
        string markup,
        int closing,
        string inner)
    {
        if (element.SelfClosing)
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"<{element.TagName} /> is self-closing and has no content"),
                "target its parent, or give it a body first with razor_set_attribute"));
        }

        var text = document.Text;
        var end = closing - element.TagName.Length - 3;

        return Result.Ok(text.Insert(
            Math.Max(end, element.Span.End),
            "\n" + inner + markup + "\n" + Indent(text, element)));
    }

    private static string ChildIndent(RazorDocument document, RazorElement element)
    {
        var child = document.Elements.FirstOrDefault(candidate =>
            candidate.Path.StartsWith(element.Path + "/", StringComparison.Ordinal)
            && !candidate.Path[(element.Path.Length + 1)..].Contains('/', StringComparison.Ordinal));

        return child is null ? Indent(document.Text, element) + "    " : Indent(document.Text, child);
    }

    private static Result<string> Remove(string text, RazorElement element)
    {
        var closing = RazorMarkup.CloseOf(text, element);

        return Result.Ok(text.Remove(element.Span.Start, closing - element.Span.Start));
    }

    private static string Indent(string text, RazorElement element)
    {
        var start = element.Span.Start;

        while (start > 0 && text[start - 1] is not '\n')
            start--;

        var end = start;

        while (end < text.Length && text[end] is ' ' or '\t')
            end++;

        return text[start..end];
    }

    private static Result<string> Directive(RazorDocument document, string name, string? value, bool remove)
    {
        var existing = document.Directives.FirstOrDefault(directive => string.Equals(directive.Name, name, StringComparison.Ordinal));

        if (remove)
            return existing is null ? Missing(name) : Result.Ok(CutLine(document.Text, existing));

        if (value is not { Length: > 0 })
            return Result.Fail<string>(Errors.Blank("value"));

        var line = string.Create(CultureInfo.InvariantCulture, $"@{name} {value}");

        return existing is null
            ? Result.Ok(Prepend(document, line))
            : Result.Ok(document.Text.Remove(existing.Span.Start, existing.Span.Length).Insert(existing.Span.Start, line));
    }

    private static Result<string> Missing(string name) => Result.Fail<string>(Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"this file declares no @{name} directive"),
        "call razor_outline to see the directives it does declare"));

    private static string CutLine(string text, RazorDirective directive)
    {
        var end = directive.Span.End;

        while (end < text.Length && text[end] is '\r' or '\n')
            end++;

        return text.Remove(directive.Span.Start, end - directive.Span.Start);
    }

    private static string Prepend(RazorDocument document, string line)
    {
        var last = document.Directives.Count is 0 ? null : document.Directives[^1];
        var point = last is null ? Start(document.Text) : last.Span.End;

        return document.Text.Insert(point, last is null ? line + "\n" : "\n" + line);
    }

    private static int Start(string text) => text.Length > 0 && text[0] is '﻿' ? 1 : 0;
}
