using System.Diagnostics.CodeAnalysis;

namespace TerseSharp.Core;

public enum RazorFileKind
{
    Component,
    Page,
    View,
    Layout,
    Imports,
    ViewStart,
}

public readonly record struct RazorSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record RazorDirective(string Name, string Value, int Line, RazorSpan Span);

public sealed record RazorAttributeInfo(string Name, string Value, int Line, RazorSpan Span, RazorSpan ValueSpan)
{
    public bool IsExpression => Value.StartsWith('@');

    public string Expression => Value.TrimStart('@').Trim('(', ')');
}

public sealed record RazorElement(
    string TagName,
    string Path,
    int Line,
    RazorSpan Span,
    bool SelfClosing,
    IReadOnlyList<RazorAttributeInfo> Attributes)
{
    public bool LooksLikeComponent => char.IsUpper(TagName[0]) || TagName.Contains('.', StringComparison.Ordinal);

    public RazorAttributeInfo? Attribute(string name) =>
        Attributes.FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record RazorCodeBlock(string Keyword, int Line, RazorSpan Span, RazorSpan BodySpan);

public sealed record RazorExpression(string Text, int Line, RazorSpan Span);

public sealed class RazorDocument
{
    private static readonly string[] Extensions = [".razor", ".cshtml"];

    private readonly int[] lineStarts;

    internal RazorDocument(string path, string text, RazorParse parse)
    {
        Path = path;
        Text = text;
        Kind = KindOf(path, parse.Directives);
        Directives = parse.Directives;
        Elements = parse.Elements;
        CodeBlocks = parse.CodeBlocks;
        Expressions = parse.Expressions;
        Issues = parse.Issues;
        lineStarts = LineStarts(text);
    }

    public string Path { get; }

    public string Text { get; }

    public RazorFileKind Kind { get; }

    public IReadOnlyList<RazorDirective> Directives { get; }

    public IReadOnlyList<RazorElement> Elements { get; }

    public IReadOnlyList<RazorCodeBlock> CodeBlocks { get; }

    public IReadOnlyList<RazorExpression> Expressions { get; }

    public IReadOnlyList<string> Issues { get; }

    public bool WellFormed => Issues.Count is 0;

    public static bool IsRazor(string path) =>
        Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous Razor index leaf; the enclosing document cache is a synchronous enumerator, so the async ripple cannot terminate here. Removing it means an async index, not a local change.")]
    public static Result<RazorDocument> Load(string fullPath)
    {
        try
        {
            return Result.Ok(Parse(fullPath, File.ReadAllText(fullPath)));
        }
        catch (IOException exception)
        {
            return Result.Fail<RazorDocument>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{fullPath}' could not be read: {exception.Message}"),
                "check the path and retry"));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Fail<RazorDocument>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{fullPath}' could not be read: {exception.Message}"),
                "check the file permissions"));
        }
    }

    public static RazorDocument Parse(string path, string text) => new(path, text, RazorScanner.Scan(text));

    public IEnumerable<string> Usings => Values("using");

    public IEnumerable<string> Routes => Values("page").Select(route => route.Trim('"'));

    public string? Value(string directive) => Values(directive).FirstOrDefault();

    public IEnumerable<string> Values(string directive) => Directives
        .Where(entry => string.Equals(entry.Name, directive, StringComparison.Ordinal))
        .Select(entry => entry.Value);

    public int LineOf(int offset)
    {
        var found = Array.BinarySearch(lineStarts, offset);

        return (found >= 0 ? found : ~found - 1) + 1;
    }

    public int OffsetOf(int line, int character) =>
        lineStarts[Math.Clamp(line, 0, lineStarts.Length - 1)] + character;

    public RazorElement? Locate(string target) => target switch
    {
        ['#', .. var reference] => Elements.FirstOrDefault(element => Referenced(element, reference)),
        _ => Elements.FirstOrDefault(element => string.Equals(element.Path, target, StringComparison.Ordinal))
             ?? Single(target),
    };

    public int Matches(string target) => target.StartsWith('#')
        ? Elements.Count(element => Referenced(element, target[1..]))
        : Elements.Count(element => Named(element, target));

    private RazorElement? Single(string target) =>
        Elements.Where(element => Named(element, target)).Take(2).ToArray() is [var only] ? only : null;

    private static bool Named(RazorElement element, string target) =>
        string.Equals(element.TagName, target, StringComparison.Ordinal)
        || string.Equals(element.Path, target, StringComparison.Ordinal);

    private static bool Referenced(RazorElement element, string reference) =>
        element.Attribute("@ref")?.Expression is { } captured
        && string.Equals(captured, reference, StringComparison.Ordinal);

    private static RazorFileKind KindOf(string path, IReadOnlyList<RazorDirective> directives)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);

        return name switch
        {
            "_Imports" or "_ViewImports" => RazorFileKind.Imports,
            "_ViewStart" => RazorFileKind.ViewStart,
            _ => Layered(path, name, directives),
        };
    }

    private static RazorFileKind Layered(string path, string name, IReadOnlyList<RazorDirective> directives)
    {
        if (name.EndsWith("Layout", StringComparison.Ordinal) || name.StartsWith("_Layout", StringComparison.Ordinal))
            return RazorFileKind.Layout;

        if (!System.IO.Path.GetExtension(path).Equals(".cshtml", StringComparison.OrdinalIgnoreCase))
            return RazorFileKind.Component;

        return directives.Any(directive => string.Equals(directive.Name, "page", StringComparison.Ordinal))
            ? RazorFileKind.Page
            : RazorFileKind.View;
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int>(text.Length / 32 + 1) { 0 };

        for (var index = text.IndexOf('\n', StringComparison.Ordinal); index >= 0; index = text.IndexOf('\n', index + 1))
            starts.Add(index + 1);

        return [.. starts];
    }
}
