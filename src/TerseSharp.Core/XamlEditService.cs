using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class XamlEditService
{
    public static Task<Result<string>> SetProperty(
        LoadedWorkspace workspace,
        string path,
        string target,
        string property,
        string value,
        bool dryRun,
        bool verbose) =>
        Apply(workspace, path, "xaml_set_property", target, (text, span) => Rewrite(text, span, property, value), dryRun, verbose);

    public static Task<Result<string>> RemoveElement(LoadedWorkspace workspace, string path, string target, bool dryRun, bool verbose) =>
        Apply(workspace, path, "xaml_remove_element", target, Cut, dryRun, verbose);

    public static Task<Result<string>> AddElement(
        LoadedWorkspace workspace,
        string path,
        string target,
        string markup,
        string position,
        bool dryRun,
        bool verbose) =>
        RejectPosition(position) is { } refusal
            ? Task.FromResult(refusal)
            : Apply(workspace, path, "xaml_add_element", target, (text, span) => Nest(text, span, markup, position), dryRun, verbose);

    private static Result<string> Cut(string text, TagSpan span)
    {
        var full = Whole(text, span);

        return full is null
            ? Result.Fail<string>(Errors.Invalid(
                "the element's closing tag could not be located",
                "remove it with edit_text, or target a self-closing element"))
            : Result.Ok(text.Remove(full.Value.Start, full.Value.Length));
    }

    private static Result<string> Nest(string text, TagSpan span, string markup, string position)
    {
        if (text[span.Start..span.End].EndsWith("/>", StringComparison.Ordinal))
        {
            return Result.Fail<string>(Errors.Invalid(
                "the target element is self-closing and has no content to add to",
                "give it a child by editing the markup, or target its parent"));
        }

        var at = InsertionPoint(text, span, position);

        return at < 0
            ? Result.Fail<string>(Errors.Invalid(
                "the target element has no matching closing tag, so position=last cannot be resolved",
                "pass position=first, or repair the markup"))
            : Result.Ok(text.Insert(at, "\n" + Indent(text, span.Start) + "  " + markup.Trim()));
    }

    private static int InsertionPoint(string text, TagSpan span, string position)
    {
        if (position is "first")
            return span.End;

        var whole = Whole(text, span);

        return whole is null ? -1 : whole.Value.End - (Name(text, span).Length + 3);
    }

    public static Result<string>? RejectPosition(string position) => position is "first" or "last"
        ? null
        : Result.Fail<string>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"position='{position}' is not known"),
            "pass position=first or position=last"));

    private static TagSpan? Whole(string text, TagSpan opening)
    {
        if (text[(opening.End - 2)..opening.End] is "/>")
            return opening;

        var name = Name(text, opening);
        var closing = MatchingClose(text, opening.End, name);

        return closing < 0 ? null : new TagSpan(opening.Start, closing + name.Length + 3 - opening.Start);
    }

    private static int MatchingClose(string text, int from, string name)
    {
        var open = string.Create(CultureInfo.InvariantCulture, $"<{name}");
        var close = string.Create(CultureInfo.InvariantCulture, $"</{name}>");
        var depth = 0;
        var index = from;

        while (index < text.Length)
        {
            var nextOpen = NextTag(text, index, open);
            var nextClose = text.IndexOf(close, index, StringComparison.Ordinal);

            if (nextClose < 0)
                return -1;

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                index = nextOpen + open.Length;
                continue;
            }

            if (depth is 0)
                return nextClose;

            depth--;
            index = nextClose + close.Length;
        }

        return -1;
    }

    private static int NextTag(string text, int from, string open)
    {
        for (var index = text.IndexOf(open, from, StringComparison.Ordinal); index >= 0;)
        {
            var after = index + open.Length;

            if (after >= text.Length || Boundary(text[after]))
                return index;

            index = text.IndexOf(open, after, StringComparison.Ordinal);
        }

        return -1;
    }

    private static bool Boundary(char character) => char.IsWhiteSpace(character) || character is '>' or '/';

    private static string Name(string text, TagSpan opening) => text[(opening.Start + 1)..opening.End]
        .TrimEnd('>', '/')
        .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault() ?? string.Empty;

    private static string Indent(string text, int start)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(start - 1, 0)) + 1;

        return new string(' ', start - lineStart);
    }

    private static async Task<Result<string>> Apply(
        LoadedWorkspace workspace,
        string path,
        string tool,
        string target,
        Func<string, TagSpan, Result<string>> change,
        bool dryRun,
        bool verbose)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;
        var loaded = XamlDocument.Load(full);

        if (!loaded.IsOk)
            return Result.Fail<string>(loaded.Error!);

        var located = Locate(loaded.Value!, target);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var written = await Write(tool, full, PositionFormat.Relative(workspace.Root, full), located.Value, change, dryRun, verbose).ConfigureAwait(false);

        if (written.IsOk && !dryRun)
        {
            workspace.Sync.Notice(full);
            workspace.Sync.Bumped(ChangeKind.Xaml);
        }

        return written;
    }
    private static async Task<Result<string>> Write(
        string tool,
        string full,
        string relative,
        int line,
        Func<string, TagSpan, Result<string>> change,
        bool dryRun,
        bool verbose)
    {
        var before = await File.ReadAllTextAsync(full).ConfigureAwait(false);
        var span = TagSpan.At(before, line);

        if (span is null)
            return Result.Fail<string>(Errors.Invalid($"line {line} does not start an element", "call xaml_outline for the element paths"));

        var changed = change(before, span.Value);

        if (!changed.IsOk)
            return Result.Fail<string>(changed.Error!);

        var after = changed.Value!;

        if (WellFormed(after) is { } malformed)
            return Result.Fail<string>(malformed);

        if (!dryRun)
            await AtomicWrite.TextAsync(full, after).ConfigureAwait(false);

        return Result.Ok(Describe(tool, relative, before, after, dryRun, verbose));
    }

    private static TerseError? WellFormed(string text)
    {
        try
        {
            System.Xml.Linq.XDocument.Parse(text);

            return null;
        }
        catch (System.Xml.XmlException exception)
        {
            return Errors.Invalid($"the edit would produce malformed XAML: {exception.Message}", "check the value you passed");
        }
    }

    private static Result<int> Locate(XamlDocument document, string target)
    {
        var matches = document.Elements().Where(element => Matches(element, target)).ToArray();

        return matches switch
        {
            [var only] => Result.Ok(only.Line),
            [] => Result.Fail<int>(Errors.Invalid($"'{target}' matched no element", "pass an element path from xaml_outline, #Name or key=Key")),
            _ => Result.Fail<int>(Errors.Invalid(
                $"'{target}' matched {matches.Length} elements",
                "pass the element path from xaml_outline, which is unique")),
        };
    }

    private static bool Matches(XamlElementInfo element, string target) => target switch
    {
        ['#', .. var name] => string.Equals(element.Name, name, StringComparison.Ordinal),
        ['k', 'e', 'y', '=', .. var key] => string.Equals(element.Key, key, StringComparison.Ordinal),
        _ => string.Equals(element.Path, target, StringComparison.Ordinal),
    };

    private static Result<string> Rewrite(string text, TagSpan span, string property, string value)
    {
        var tag = text[span.Start..span.End];
        var existing = Attribute(property).Match(tag);
        var replaced = existing.Success
            ? tag.Remove(existing.Index, existing.Length).Insert(existing.Index, Attribute(property, value))
            : Insert(tag, Attribute(property, value));

        return Result.Ok(text.Remove(span.Start, span.Length).Insert(span.Start, replaced));
    }

    private static string Insert(string tag, string attribute)
    {
        var close = tag.EndsWith("/>", StringComparison.Ordinal) ? tag.Length - 2 : tag.Length - 1;

        return tag.Insert(close, attribute + " ");
    }

    private static string Attribute(string property, string value) =>
        string.Create(CultureInfo.InvariantCulture, $" {property}=\"{value}\"");

    private static Regex Attribute(string property) =>
        new(@"\s" + Regex.Escape(property) + @"\s*=\s*""[^""]*""", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static string Describe(string tool, string relative, string before, string after, bool dryRun, bool verbose)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied");
        var changed = UnifiedDiff.ChangedLines(before, after);

        response.Summary(1, 1, "files changed");

        if (!dryRun && !verbose)
            return response.Line(string.Create(CultureInfo.InvariantCulture, $"{relative}  changedLines={changed}")).Note("(verbose=true for the diff)").ToString();

        response.Line(UnifiedDiff.Between(relative, before, after));
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={changed}"));

        return response.ToString();
    }

    private readonly record struct TagSpan(int Start, int Length)
    {
        public int End => Start + Length;

        public static TagSpan? At(string text, int line)
        {
            var offset = Offset(text, line);
            var open = offset < 0 ? -1 : text.IndexOf('<', offset);
            var close = open < 0 ? -1 : text.IndexOf('>', open);

            return close < 0 ? null : new TagSpan(open, close - open + 1);
        }

        private static int Offset(string text, int line)
        {
            var offset = 0;

            for (var current = 1; current < line; current++)
            {
                offset = text.IndexOf('\n', offset);

                if (offset < 0)
                    return -1;

                offset++;
            }

            return offset;
        }
    }
}
