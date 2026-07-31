using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class XamlEditService
{
    public static Result<string> SetProperty(
        LoadedWorkspace workspace,
        string path,
        string target,
        string property,
        string value,
        bool dryRun) =>
        Apply(workspace, path, "xaml_set_property", target, (text, span) => Rewrite(text, span, property, value), dryRun);

    private static Result<string> Apply(
        LoadedWorkspace workspace,
        string path,
        string tool,
        string target,
        Func<string, TagSpan, Result<string>> change,
        bool dryRun)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        var full = resolved.Value!;
        var loaded = XamlDocument.Load(full);

        if (!loaded.IsOk)
            return Result.Fail<string>(loaded.Error!);

        var located = Locate(loaded.Value!, target);

        return located.IsOk
            ? Write(tool, full, PositionFormat.Relative(workspace.Root, full), located.Value, change, dryRun)
            : Result.Fail<string>(located.Error!);
    }

    private static Result<string> Write(
        string tool,
        string full,
        string relative,
        int line,
        Func<string, TagSpan, Result<string>> change,
        bool dryRun)
    {
        var before = File.ReadAllText(full);
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
            AtomicWrite.Text(full, after);

        return Result.Ok(Describe(tool, relative, before, after, dryRun));
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

    private static string Describe(string tool, string relative, string before, string after, bool dryRun)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied");

        response.Summary(1, 1, "files changed");
        response.Line(UnifiedDiff.Between(relative, before, after));
        response.Line(string.Create(CultureInfo.InvariantCulture, $"changedLines={UnifiedDiff.ChangedLines(before, after)}"));

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
