using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static partial class XamlDeadCode
{
    public static async Task<IReadOnlyList<string>> UnusedAsync(
        string root,
        XamlResourceGraph graph,
        CancellationToken cancellationToken)
    {
        var referenced = References(graph);
        var literals = await LiteralsAsync(root, cancellationToken).ConfigureAwait(false);

        return
        [
            .. graph.Files
                .SelectMany(Declarations)
                .Where(declaration => !referenced.Contains(declaration.Value) && !literals.Contains(declaration.Value))
                .Select(Issue),
        ];
    }

    private static IEnumerable<Declaration> Declarations(XamlFileRecord file)
    {
        foreach (var element in file.Elements)
        {
            if (element.Key is { } key)
                yield return new Declaration(file.Relative, element.Line, "XAML004", "x:Key", key);

            if (element.Name is { } name)
                yield return new Declaration(file.Relative, element.Line, "XAML005", "x:Name", name);
        }
    }

    private static HashSet<string> References(XamlResourceGraph graph)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in graph.Files.SelectMany(file => file.Elements).SelectMany(element => element.Attributes))
            Collect(found, attribute.Value, attribute.Name);

        return found;
    }

    private static void Collect(HashSet<string> found, string value, string attribute)
    {
        foreach (Match match in XamlResourceGraph.ResourceReference().Matches(value))
            found.Add(match.Groups[1].Value);

        if (attribute is "ElementName" or "TargetName" or "Source" or "BasedOn")
            found.Add(value.TrimStart('{').TrimEnd('}').Trim());

        foreach (Match match in BindingElementName().Matches(value))
            found.Add(match.Groups[1].Value);
    }

    private static async Task<HashSet<string>> LiteralsAsync(string root, CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var lookup = found.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var file in SourceFiles(root))
        {
            var text = await ReadAsync(file, cancellationToken).ConfigureAwait(false);

            foreach (var match in QuotedLiteral().EnumerateMatches(text))
                lookup.Add(text.AsSpan(match.Index, match.Length));
        }

        return found;
    }

    private static IEnumerable<string> SourceFiles(string root) => WorkspaceFiles
        .Enumerate(root, file => SourceFile.IsCSharp(file) && !GeneratedCode.IsGenerated(root, file));

    private static async Task<string> ReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string Issue(Declaration declaration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{declaration.File}:{declaration.Line}  {declaration.Code}  HEURISTIC  unused {declaration.Kind} '{declaration.Value}', matched by no XAML attribute and no C# identifier or literal");

    private readonly record struct Declaration(string File, int Line, string Code, string Kind, string Value);

    [GeneratedRegex(@"ElementName\s*=\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex BindingElementName();

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex QuotedLiteral();
}
