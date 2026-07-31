using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static partial class XamlService
{
    public static Result<string> Outline(LoadedWorkspace workspace, string path, int depth, string filter)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var shown = document.Elements().Where(element => Depth(element.Path) <= depth).Where(Selector(filter)).ToArray();
            var total = document.Elements().Count();
            var response = new ResponseBuilder("xaml_outline", relative);

            response.Summary(shown.Length, total, "elements");
            response.Note("dialect=" + document.Dialect);

            foreach (var element in shown)
                response.Line(Describe(element));

            return response.ToString();
        });
    }

    public static Result<string> Names(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var named = document.Elements().Where(element => element.Name is not null || element.Uid is not null).ToArray();
            var response = new ResponseBuilder("xaml_names", relative);

            response.Summary(named.Length, named.Length, "names");

            foreach (var element in named)
                response.Line(NameRecord(relative, element));

            return response.ToString();
        });
    }

    public static Result<string> Resources(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var keyed = document.Elements().Where(element => element.Key is not null).ToArray();
            var response = new ResponseBuilder("xaml_resources", relative);

            response.Summary(keyed.Length, keyed.Length, "resources");

            foreach (var element in keyed)
            {
                response.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{relative}:{element.Line}  EXACT  {element.Key}  {element.TypeName}"));
            }

            return response.ToString();
        });
    }

    public static Result<string> Resolve(LoadedWorkspace workspace, string key)
    {
        var graph = XamlResourceGraph.Build(workspace.Root);
        var declarations = graph.Of(key);
        var response = new ResponseBuilder("xaml_resolve", key);

        response.Summary(declarations.Count, declarations.Count, "declarations");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"scanned={graph.FileCount} files"));
        Unread(response, graph);

        if (declarations.Count is 0)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"'{key}' is declared in no XAML file under the workspace root"));

        foreach (var declaration in declarations.OrderBy(Rank).ThenBy(declaration => declaration.File, StringComparer.Ordinal))
            response.Line(ResolveRecord(declaration));

        return Result.Ok(response.ToString());
    }

    public static Result<string> Bindings(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var sites = XamlBindingService.Sites(document).ToArray();
            var response = new ResponseBuilder("xaml_bindings", relative);

            response.Summary(sites.Length, sites.Length, "bindings");
            response.Note("dialect=" + document.Dialect);

            foreach (var site in sites)
                response.Line(BindingRecord(relative, site, "HEURISTIC", string.Empty));

            return response.ToString();
        });
    }

    public static async Task<Result<string>> ValidateBindingsAsync(
        LoadedWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var loaded = Open(workspace, path);

        if (!loaded.IsOk)
            return Result.Fail<string>(loaded.Error!);

        var (document, relative) = loaded.Value;
        var sites = XamlBindingService.Sites(document).ToArray();
        var response = new ResponseBuilder("xaml_bindings", relative);

        response.Summary(sites.Length, sites.Length, "bindings");
        response.Note("dialect=" + document.Dialect);

        foreach (var site in sites)
            response.Line(await CheckAsync(workspace, document, relative, site, cancellationToken).ConfigureAwait(false));

        return Result.Ok(response.ToString());
    }

    public static Result<string> Validate(LoadedWorkspace workspace, string path)
    {
        var opened = Open(workspace, path);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        var graph = XamlResourceGraph.Build(workspace.Root);
        var (document, relative) = opened.Value;
        var issues = Issues(document, relative, graph).ToArray();
        var response = new ResponseBuilder("xaml_validate", relative);

        response.Summary(issues.Length, issues.Length, "issues");
        response.Note("dialect=" + document.Dialect);
        Unread(response, graph);

        foreach (var issue in issues)
            response.Line(issue);

        return Result.Ok(response.ToString());
    }

    public static Result<string> ValidateAll(LoadedWorkspace workspace, int maxResults)
    {
        var graph = XamlResourceGraph.Build(workspace.Root);
        var issues = graph.Files.SelectMany(file => Collect(file, graph)).ToArray();
        var response = new ResponseBuilder("xaml_validate", "solution");

        response.Summary(Math.Min(maxResults, issues.Length), issues.Length, "issues");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"scanned={graph.FileCount} files"));

        foreach (var issue in issues.Take(maxResults))
            response.Line(issue);

        return Result.Ok(response.ToString());
    }

    private static void Unread(ResponseBuilder response, XamlResourceGraph graph)
    {
        if (graph.SkippedCount > 0)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"WARNING {graph.SkippedCount} XAML file(s) could not be parsed, so unresolved-resource checking is off"));
        }
    }

    public static Result<string> Find(LoadedWorkspace workspace, string query, string kind, int maxResults)
    {
        var hits = new List<string>();

        foreach (var file in XamlFiles.Enumerate(workspace.Root))
            Scan(file, workspace.Root, query, kind, hits);

        var response = new ResponseBuilder("xaml_find", query);

        response.Summary(Math.Min(maxResults, hits.Count), hits.Count, "matches");

        foreach (var hit in hits.Take(maxResults))
            response.Line(hit);

        return Result.Ok(response.ToString());
    }

    private static IEnumerable<string> Collect(XamlIndexedFile file, XamlResourceGraph graph) =>
        file.Document is null
            ? [Issue(file.Relative, 1, "XAML000", file.Failure ?? "could not be read")]
            : Issues(file.Document, file.Relative, graph);

    private static async Task<string> CheckAsync(
        LoadedWorkspace workspace,
        XamlDocument document,
        string relative,
        XamlBindingSite site,
        CancellationToken cancellationToken)
    {
        var contextName = XamlBindingService.ContextTypeName(site.Element);

        if (contextName is null)
            return BindingRecord(relative, site, "HEURISTIC", "UNRESOLVED_CONTEXT no x:DataType or d:DataContext in scope");

        var type = await XamlBindingService
            .ResolveTypeAsync(workspace, document, contextName, cancellationToken)
            .ConfigureAwait(false);

        return type is null
            ? BindingRecord(relative, site, "HEURISTIC", $"UNRESOLVED_CONTEXT '{contextName}' is not a type in this solution")
            : BindingRecord(relative, site, "EXACT", Verdict(type, site));
    }

    private static string Verdict(INamedTypeSymbol context, XamlBindingSite site)
    {
        var path = XamlBindingService.PathOf(site.Expression);

        if (path is null)
            return string.Create(CultureInfo.InvariantCulture, $"UNSUPPORTED no property path on {context.Name}");

        return path.Length is 0
            ? string.Create(CultureInfo.InvariantCulture, $"OK binds {context.ToDisplayString()} itself")
            : Walk(context, path);
    }

    private static string Walk(INamedTypeSymbol context, string path)
    {
        var current = (INamedTypeSymbol?)context;

        foreach (var segment in path.Split('.'))
        {
            if (current is null)
                return string.Create(CultureInfo.InvariantCulture, $"UNSUPPORTED '{path}' leaves the source tree");

            var members = XamlBindingService.Members(current, segment);

            if (members.Count is 0)
                return Missing(current, segment, path);

            current = XamlBindingService.TypeOf(members[0]) as INamedTypeSymbol;
        }

        return string.Create(CultureInfo.InvariantCulture, $"OK {path} on {context.ToDisplayString()}");
    }

    private static string Missing(INamedTypeSymbol type, string segment, string path)
    {
        var nearest = XamlBindingService.Nearest(type, segment);
        var hint = nearest is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"; nearest '{nearest}'");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"ERROR no member '{segment}' of '{path}' on {type.ToDisplayString()}{hint}");
    }

    private static string BindingRecord(string relative, XamlBindingSite site, string confidence, string verdict) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{relative}:{site.Line}  {confidence}  {site.Element.TypeName}.{site.Attribute.Name.LocalName}  {site.Expression}").TrimEnd()
        + (verdict.Length is 0 ? string.Empty : "  " + verdict);

    private static string NameRecord(string relative, XamlElementInfo element)
    {
        var uid = element.Uid is null ? string.Empty : "  uid=" + element.Uid;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{relative}:{element.Line}  EXACT  {element.Name ?? "-"}  {element.TypeName}  {element.Path}{uid}");
    }

    private static string ResolveRecord(XamlResourceDeclaration declaration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{declaration.File}:{declaration.Line}  HEURISTIC  {declaration.TypeName}  scope={declaration.Scope}");

    private static int Rank(XamlResourceDeclaration declaration) => declaration.Scope switch
    {
        "local" => 0,
        "app" => 1,
        _ => 2,
    };

    private static Func<XamlElementInfo, bool> Selector(string filter) => filter.ToLowerInvariant() switch
    {
        "named" => element => element.Name is not null,
        "keyed" => element => element.Key is not null,
        _ => _ => true,
    };

    private static void Scan(string file, string root, string query, string kind, List<string> hits)
    {
        var loaded = XamlDocument.Load(file);

        if (!loaded.IsOk)
            return;

        var relative = PositionFormat.Relative(root, file);

        foreach (var element in loaded.Value!.Elements().Where(element => Matches(element, query, kind)))
        {
            hits.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{relative}:{element.Line}  HEURISTIC  {element.TypeName}  {element.Path}"));
        }
    }

    private static bool Matches(XamlElementInfo element, string query, string kind) => kind.ToLowerInvariant() switch
    {
        "name" => string.Equals(element.Name, query, StringComparison.Ordinal),
        "resource" or "key" => string.Equals(element.Key, query, StringComparison.Ordinal),
        "uid" => string.Equals(element.Uid, query, StringComparison.Ordinal),
        "binding" => element.Element.Attributes().Any(attribute => attribute.Value.Contains(query, StringComparison.Ordinal)),
        _ => element.TypeName.Equals(query, StringComparison.Ordinal),
    };

    private static IEnumerable<string> Issues(XamlDocument document, string relative, XamlResourceGraph graph)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in document.Elements())
        {
            if (element.Key is { } key && !keys.Add(key))
                yield return Issue(relative, element.Line, "XAML001", $"duplicate x:Key '{key}'");

            if (element.Name is { } name && !names.Add(name))
                yield return Issue(relative, element.Line, "XAML002", $"duplicate x:Name '{name}'");
        }

        foreach (var (Key, Line) in MissingResources(document, graph))
            yield return Issue(relative, Line, "XAML003", $"unresolved resource '{Key}', declared in no XAML file under the workspace root");
    }

    private static IEnumerable<(string Key, int Line)> MissingResources(XamlDocument document, XamlResourceGraph graph)
    {
        if (graph.SkippedCount > 0)
            yield break;

        foreach (var element in document.Elements())
        {
            foreach (var attribute in element.Element.Attributes())
            {
                var match = ResourceReference().Match(attribute.Value);

                if (match.Success && !graph.Declares(match.Groups[1].Value))
                    yield return (match.Groups[1].Value, XamlDocument.Line(attribute));
            }
        }
    }

    private static string Issue(string relative, int line, string id, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"{relative}:{line}  {id}  {message}");

    private static string Describe(XamlElementInfo element)
    {
        var name = element.Name is null ? string.Empty : " #" + element.Name;
        var key = element.Key is null ? string.Empty : " key=" + element.Key;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{new string(' ', Depth(element.Path) * 2)}{element.TypeName}{name}{key}  :{element.Line}");
    }

    private static int Depth(string path) => path.Count(character => character is '/');

    private static Result<(XamlDocument Document, string Relative)> Open(LoadedWorkspace workspace, string path)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return Result.Fail<(XamlDocument, string)>(resolved.Error!);

        if (!XamlDocument.IsXaml(resolved.Value!))
        {
            return Result.Fail<(XamlDocument, string)>(
                Errors.Invalid($"'{path}' is not a XAML file", "pass a .xaml, .axaml or .paml file"));
        }

        var document = XamlDocument.Load(resolved.Value!);

        return document.IsOk
            ? Result.Ok((document.Value!, PositionFormat.Relative(workspace.Root, resolved.Value!)))
            : Result.Fail<(XamlDocument, string)>(document.Error!);
    }

    private static Result<string> Read(
        LoadedWorkspace workspace,
        string path,
        Func<XamlDocument, string, string> render)
    {
        var opened = Open(workspace, path);

        return opened.IsOk
            ? Result.Ok(render(opened.Value.Document, opened.Value.Relative))
            : Result.Fail<string>(opened.Error!);
    }

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource|ThemeResource)\s+([^}\s]+)\s*\}")]
    private static partial Regex ResourceReference();
}
