namespace TerseSharp.Core;

internal sealed record RazorParse(
    IReadOnlyList<RazorDirective> Directives,
    IReadOnlyList<RazorElement> Elements,
    IReadOnlyList<RazorCodeBlock> CodeBlocks,
    IReadOnlyList<RazorExpression> Expressions,
    IReadOnlyList<string> Issues);

internal sealed class RazorScanner
{
    private static readonly string[] DirectiveNames =
    [
        "page", "using", "inject", "inherits", "implements", "layout", "namespace", "attribute", "typeparam",
        "rendermode", "model", "addTagHelper", "removeTagHelper", "tagHelperPrefix", "preservewhitespace",
    ];

    private static readonly string[] BlockNames = ["code", "functions"];

    private static readonly string[] StatementNames =
    [
        "if", "else", "for", "foreach", "while", "switch", "do", "try", "catch", "finally", "lock", "await", "section",
    ];

    private static readonly string[] VoidElements =
    [
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    ];

    private static readonly string[] OptionalEndElements =
    [
        "p", "li", "td", "tr", "th", "tbody", "thead", "tfoot", "option", "dt", "dd", "colgroup",
    ];

    private readonly string text;
    private readonly List<RazorDirective> directives = [];
    private readonly List<RazorElement> elements = [];
    private readonly List<RazorCodeBlock> blocks = [];
    private readonly List<RazorExpression> expressions = [];
    private readonly List<string> issues = [];
    private readonly List<Frame> stack = [];
    private readonly Dictionary<string, int> rootCounters = new(StringComparer.Ordinal);

    private int index;

    private RazorScanner(string text) => this.text = text;

    public static RazorParse Scan(string text) => new RazorScanner(text).Run();

    private RazorParse Run()
    {
        while (index < text.Length)
            Step();

        foreach (var frame in stack.Where(frame => !OptionalEndElements.Contains(frame.Tag, StringComparer.OrdinalIgnoreCase)))
            issues.Add(Issue(frame.Line, string.Create(CultureInfo.InvariantCulture, $"<{frame.Tag}> is never closed")));

        return new RazorParse(directives, elements, blocks, expressions, issues);
    }

    private void Step()
    {
        if (text[index] is '@')
        {
            Transition();

            return;
        }

        if (text[index] is '<')
        {
            Tag();

            return;
        }

        index++;
    }

    private void Transition()
    {
        var next = index + 1 < text.Length ? text[index + 1] : '\0';

        switch (next)
        {
            case '@': index += 2; return;
            case '*': SkipPast("*@"); return;
            case '(': Expression(); return;
            case '{': Block("{", index, index + 1); return;
            default: Word(); return;
        }
    }

    private void Word()
    {
        var start = index;
        var name = Identifier(index + 1);

        if (name.Length is 0)
        {
            index++;

            return;
        }

        Keyword(name, start);
    }

    private void Keyword(string name, int start)
    {
        if (BlockNames.Contains(name, StringComparer.Ordinal))
        {
            Block(name, start, start + name.Length + 1);

            return;
        }

        if (IsDirective(name, start))
            Directive(name, start);
        else if (StatementNames.Contains(name, StringComparer.Ordinal))
            index = start + name.Length + 1;
        else
            Expression();
    }

    private bool IsDirective(string name, int start) =>
        DirectiveNames.Contains(name, StringComparer.Ordinal)
        && (!string.Equals(name, "using", StringComparison.Ordinal) || !OpensParenthesis(start + name.Length + 1));

    private bool OpensParenthesis(int from)
    {
        var cursor = from;

        while (cursor < text.Length && text[cursor] is ' ' or '\t')
            cursor++;

        return cursor < text.Length && text[cursor] is '(';
    }

    private void Directive(string name, int start)
    {
        var from = start + name.Length + 1;
        var end = LineEnd(from);
        var value = text[from..end].Trim();

        directives.Add(new RazorDirective(name, value, LineAt(start), new RazorSpan(start, end - start)));
        index = end;
    }

    private void Block(string keyword, int start, int from)
    {
        var open = text.IndexOf('{', from);

        if (open < 0)
        {
            index = from;

            return;
        }

        index = open;

        var body = Balanced('{', '}');

        blocks.Add(new RazorCodeBlock(keyword, LineAt(start), new RazorSpan(start, index - start), body));
    }

    private void Expression()
    {
        var start = index;

        index++;

        if (index < text.Length && text[index] is '(')
            Balanced('(', ')');
        else
            Chain();

        expressions.Add(new RazorExpression(text[start..index], LineAt(start), new RazorSpan(start, index - start)));
    }

    private void Chain()
    {
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.'))
            index++;

        if (index < text.Length && text[index] is '(')
            Balanced('(', ')');
    }

    private RazorSpan Balanced(char open, char close)
    {
        var start = index;
        var depth = 0;

        while (index < text.Length)
        {
            depth += text[index] == open ? 1 : 0;
            depth -= text[index] == close ? 1 : 0;
            index++;

            if (depth is 0)
                break;
        }

        return new RazorSpan(start + 1, Math.Max(index - start - 2, 0));
    }

    private void Tag()
    {
        if (Starts("<!--"))
            SkipPast("-->");
        else if (Starts("<!"))
            SkipPast(">");
        else if (Starts("</"))
            CloseTag();
        else
            OpenTag();
    }

    private void OpenTag()
    {
        var start = index;
        var name = TagName(index + 1);

        if (name.Length is 0)
        {
            index++;

            return;
        }

        index = start + name.Length + 1;

        var path = PathFor(name);
        var attributes = Attributes();
        var selfClosing = CloseBracket();

        elements.Add(new RazorElement(name, path, LineAt(start), new RazorSpan(start, index - start), selfClosing, attributes));

        if (!selfClosing && !VoidElements.Contains(name, StringComparer.OrdinalIgnoreCase))
            stack.Add(new Frame(name, path, LineAt(start), new Dictionary<string, int>(StringComparer.Ordinal)));
    }

    private void CloseTag()
    {
        var start = index;
        var name = TagName(index + 2);
        var depth = stack.FindLastIndex(frame => string.Equals(frame.Tag, name, StringComparison.OrdinalIgnoreCase));

        index = LineOrTagEnd(start);

        if (depth < 0)
        {
            issues.Add(Issue(LineAt(start), string.Create(CultureInfo.InvariantCulture, $"</{name}> closes nothing")));

            return;
        }

        stack.RemoveRange(depth, stack.Count - depth);
    }

    private int LineOrTagEnd(int start)
    {
        var end = text.IndexOf('>', start);

        return end < 0 ? text.Length : end + 1;
    }

    private List<RazorAttributeInfo> Attributes()
    {
        var attributes = new List<RazorAttributeInfo>();

        while (index < text.Length && text[index] is not '>' and not '/')
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;

                continue;
            }

            if (Attribute() is not { } attribute)
                break;

            attributes.Add(attribute);
        }

        return attributes;
    }

    private RazorAttributeInfo? Attribute()
    {
        var start = index;
        var name = AttributeName();

        if (name.Length is 0)
        {
            index++;

            return null;
        }

        return index < text.Length && text[index] is '='
            ? Assigned(name, start)
            : new RazorAttributeInfo(name, string.Empty, LineAt(start), new RazorSpan(start, index - start), default);
    }

    private RazorAttributeInfo Assigned(string name, int start)
    {
        index++;

        var valueStart = index;
        var value = Value();

        return new RazorAttributeInfo(
            name,
            value,
            LineAt(start),
            new RazorSpan(start, index - start),
            new RazorSpan(valueStart, index - valueStart));
    }

    private string Value()
    {
        if (index >= text.Length)
            return string.Empty;

        return text[index] is '"' or '\'' ? Quoted(text[index]) : Bare();
    }

    private string Quoted(char quote)
    {
        index++;

        var start = index;

        while (index < text.Length && text[index] != quote)
            SkipValueCharacter();

        var value = text[start..Math.Min(index, text.Length)];

        if (index < text.Length)
            index++;

        return value;
    }

    private void SkipValueCharacter()
    {
        if (text[index] is '@' && index + 1 < text.Length && text[index + 1] is '(')
        {
            index++;
            Balanced('(', ')');

            return;
        }

        index++;
    }

    private string Bare()
    {
        var start = index;

        while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not '>')
            index++;

        return text[start..index];
    }

    private bool CloseBracket()
    {
        var selfClosing = index < text.Length && text[index] is '/';

        if (selfClosing)
            index++;

        if (index < text.Length && text[index] is '>')
            index++;

        return selfClosing;
    }

    private string PathFor(string name)
    {
        var counters = stack.Count is 0 ? rootCounters : stack[^1].Counters;
        var seen = counters.GetValueOrDefault(name);

        counters[name] = seen + 1;

        var segment = seen is 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name}[{seen}]");

        return stack.Count is 0 ? segment : stack[^1].Path + "/" + segment;
    }

    private string TagName(int from)
    {
        var end = from;

        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-' or '.' or ':'))
            end++;

        return text[from..end];
    }

    private string AttributeName()
    {
        var start = index;

        while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not '=' and not '>' and not '/')
            index++;

        return text[start..index];
    }

    private string Identifier(int from)
    {
        var end = from;

        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_'))
            end++;

        return text[from..end];
    }

    private int LineEnd(int from)
    {
        var end = text.IndexOf('\n', from);

        return end < 0 ? text.Length : end;
    }

    private bool Starts(string prefix) =>
        text.AsSpan(index).StartsWith(prefix, StringComparison.Ordinal);

    private void SkipPast(string terminator)
    {
        var end = text.IndexOf(terminator, index, StringComparison.Ordinal);

        index = end < 0 ? text.Length : end + terminator.Length;
    }

    private int LineAt(int offset) => text.AsSpan(0, Math.Min(offset, text.Length)).Count('\n') + 1;

    private static string Issue(int line, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"{line}: {message}");

    private sealed record Frame(string Tag, string Path, int Line, Dictionary<string, int> Counters);
}
