using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TerseSharp.Core;

public static partial class XamlService
{
    public static Result<string> Outline(LoadedWorkspace workspace, string path, int depth)
    {
        return Read(workspace, path, document =>
        {
            var elements = document.Elements().ToArray();
            var response = new ResponseBuilder("xaml_outline", path);

            response.Summary(elements.Length, elements.Length, "elements");
            response.Note("dialect=" + document.Dialect);

            foreach (var element in elements.Where(element => Depth(element.Path) <= depth))
                response.Line(Describe(element));

            return response.ToString();
        });
    }

    public static Result<string> Names(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, document =>
        {
            var named = document.Elements().Where(element => element.Name is not null).ToArray();
            var response = new ResponseBuilder("xaml_names", path);

            response.Summary(named.Length, named.Length, "names");

            foreach (var element in named)
            {
                response.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{path}:{element.Line}  EXACT  {element.Name}  {element.TypeName}  {element.Path}"));
            }

            return response.ToString();
        });
    }

    public static Result<string> Resources(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, document =>
        {
            var keyed = document.Elements().Where(element => element.Key is not null).ToArray();
            var response = new ResponseBuilder("xaml_resources", path);

            response.Summary(keyed.Length, keyed.Length, "resources");

            foreach (var element in keyed)
            {
                response.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{path}:{element.Line}  EXACT  {element.Key}  {element.TypeName}"));
            }

            return response.ToString();
        });
    }

    public static Result<string> Bindings(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, document =>
        {
            var bindings = Collect(document).ToArray();
            var response = new ResponseBuilder("xaml_bindings", path);

            response.Summary(bindings.Length, bindings.Length, "bindings");

            foreach (var binding in bindings)
                response.Line(binding);

            return response.ToString();
        });
    }

    public static Result<string> Validate(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, document =>
        {
            var issues = Issues(document, path).ToArray();
            var response = new ResponseBuilder("xaml_validate", path);

            response.Summary(issues.Length, issues.Length, "issues");
            response.Note("dialect=" + document.Dialect);

            foreach (var issue in issues)
                response.Line(issue);

            return response.ToString();
        });
    }

    public static Result<string> Find(LoadedWorkspace workspace, string query, string kind, int maxResults)
    {
        var files = Directory
            .EnumerateFiles(workspace.Root, "*", SearchOption.AllDirectories)
            .Where(file => XamlDocument.IsXaml(file) && !Excluded(file, workspace.Root))
            .ToArray();

        var hits = new List<string>();

        foreach (var file in files)
            Scan(file, workspace.Root, query, kind, hits);

        var response = new ResponseBuilder("xaml_find", query);

        response.Summary(Math.Min(maxResults, hits.Count), hits.Count, "matches");

        foreach (var hit in hits.Take(maxResults))
            response.Line(hit);

        return Result.Ok(response.ToString());
    }

    private static void Scan(string file, string root, string query, string kind, List<string> hits)
    {
        var loaded = XamlDocument.Load(file);

        if (!loaded.IsOk)
            return;

        var relative = Path.GetRelativePath(root, file);

        foreach (var element in loaded.Value!.Elements().Where(element => Matches(element, query, kind)))
        {
            hits.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{relative}:{element.Line}  EXACT  {element.TypeName}  {element.Path}"));
        }
    }

    private static bool Matches(XamlElementInfo element, string query, string kind) => kind.ToLowerInvariant() switch
    {
        "name" => string.Equals(element.Name, query, StringComparison.Ordinal),
        "resource" or "key" => string.Equals(element.Key, query, StringComparison.Ordinal),
        "binding" => element.Element.Attributes().Any(attribute => attribute.Value.Contains(query, StringComparison.Ordinal)),
        _ => element.TypeName.Equals(query, StringComparison.Ordinal),
    };

    private static IEnumerable<string> Collect(XamlDocument document)
    {
        foreach (var element in document.Elements())
        {
            foreach (var attribute in element.Element.Attributes())
            {
                var match = BindingExpression().Match(attribute.Value);

                if (match.Success)
                    yield return Describe(document, element, attribute, match);
            }
        }
    }

    private static string Describe(XamlDocument document, XamlElementInfo element, XAttribute attribute, Match match) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(document.Path)}:{XamlDocument.Line(attribute)}  HEURISTIC  {element.TypeName}.{attribute.Name.LocalName}  {match.Value}");

    private static IEnumerable<string> Issues(XamlDocument document, string path)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in document.Elements())
        {
            if (element.Key is { } key && !keys.Add(key))
                yield return Issue(path, element.Line, "XAML001", $"duplicate x:Key '{key}'");

            if (element.Name is { } name && !names.Add(name))
                yield return Issue(path, element.Line, "XAML002", $"duplicate x:Name '{name}'");
        }

        foreach (var (Key, Line) in MissingResources(document))
            yield return Issue(path, Line, "XAML003", $"unresolved StaticResource '{Key}'");
    }

    private static IEnumerable<(string Key, int Line)> MissingResources(XamlDocument document)
    {
        var declared = document.Elements().Select(element => element.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var element in document.Elements())
        {
            foreach (var attribute in element.Element.Attributes())
            {
                var match = StaticResource().Match(attribute.Value);

                if (match.Success && !declared.Contains(match.Groups[1].Value))
                    yield return (match.Groups[1].Value, XamlDocument.Line(attribute));
            }
        }
    }

    private static string Issue(string path, int line, string id, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}:{line}  {id}  {message}");

    private static string Describe(XamlElementInfo element)
    {
        var name = element.Name is null ? string.Empty : " #" + element.Name;
        var key = element.Key is null ? string.Empty : " key=" + element.Key;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{new string(' ', Depth(element.Path) * 2)}{element.TypeName}{name}{key}  :{element.Line}");
    }

    private static int Depth(string path) => path.Count(character => character is '/');

    private static bool Excluded(string file, string root) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or ".git");

    private static Result<string> Read(LoadedWorkspace workspace, string path, Func<XamlDocument, string> render)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        if (!XamlDocument.IsXaml(resolved.Value!))
            return Result.Fail<string>(Errors.Invalid($"'{path}' is not a XAML file", "pass a .xaml, .axaml or .paml file"));

        var document = XamlDocument.Load(resolved.Value!);

        return document.IsOk ? Result.Ok(render(document.Value!)) : Result.Fail<string>(document.Error!);
    }

    [GeneratedRegex(@"\{(?:x:)?(?:Compiled)?Binding[^}]*\}|\{x:Bind[^}]*\}")]
    private static partial Regex BindingExpression();

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource|ThemeResource)\s+([^}\s]+)\s*\}")]
    private static partial Regex StaticResource();
}
