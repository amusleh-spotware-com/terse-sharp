using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static partial class XamlDeadCode
{
    public static IEnumerable<string> Unused(string root, XamlResourceGraph graph)
    {
        var referenced = References(graph);
        var literals = Literals(root);

        foreach (var declaration in graph.Files.SelectMany(file => Declarations(file)))
        {
            if (!referenced.Contains(declaration.Value) && !literals.Contains(declaration.Value))
                yield return Issue(declaration);
        }
    }

    private static IEnumerable<Declaration> Declarations(XamlIndexedFile file)
    {
        if (file.Document is null)
            yield break;

        foreach (var element in file.Document.Elements())
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

        foreach (var file in graph.Files.Where(file => file.Document is not null))
        {
            foreach (var attribute in file.Document!.Document.Descendants().SelectMany(element => element.Attributes()))
                Collect(found, attribute.Value, attribute.Name.LocalName);
        }

        return found;
    }

    private static void Collect(HashSet<string> found, string value, string attribute)
    {
        foreach (Match match in ResourceReference().Matches(value))
            found.Add(match.Groups[1].Value);

        if (attribute is "ElementName" or "TargetName" or "Source" or "BasedOn")
            found.Add(value.TrimStart('{').TrimEnd('}').Trim());

        foreach (Match match in BindingElementName().Matches(value))
            found.Add(match.Groups[1].Value);
    }

    private static HashSet<string> Literals(string root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles(root))
        {
            foreach (Match match in QuotedLiteral().Matches(Read(file)))
                found.Add(match.Value);
        }

        return found;
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !GeneratedCode.IsGenerated(root, file));
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Read(string file)
    {
        try
        {
            return File.ReadAllText(file);
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

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource|ThemeResource)\s+([^}\s]+)\s*\}")]
    private static partial Regex ResourceReference();

    [GeneratedRegex(@"ElementName\s*=\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex BindingElementName();

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex QuotedLiteral();
}
