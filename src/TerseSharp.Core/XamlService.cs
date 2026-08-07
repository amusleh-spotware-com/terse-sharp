using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class XamlService
{
    public static Result<string> Outline(LoadedWorkspace workspace, string path, int depth, string filter)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var shown = document.Elements().Where(element => Depth(element.Path) <= depth).Where(Selector(filter)).ToArray();
            var total = document.Elements().Count();
            var response = new ResponseBuilder("xaml_outline", relative);

            response.Summary(shown.Length, total, "elements", "depth= or filter=named|keyed");
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
        var graph = workspace.Indexes.Xaml();
        var declarations = graph.Of(key);
        var response = new ResponseBuilder("xaml_resolve", key);

        response.Summary(declarations.Count, declarations.Count, "declarations");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"scanned={graph.FileCount} files"));
        Unread(response, graph);

        if (declarations.Count is 0)
        {
            response.Line(string.Create(CultureInfo.InvariantCulture, $"'{key}' is declared in no XAML file under the workspace root"));
            Implicit(response, graph, key);
        }

        foreach (var declaration in declarations.OrderBy(Rank).ThenBy(declaration => declaration.File, StringComparer.Ordinal))
            response.Line(ResolveRecord(declaration));

        return Result.Ok(response.ToString());
    }

    public static Result<string> CodeBehind(LoadedWorkspace workspace, string path)
    {
        return Read(workspace, path, (document, relative) =>
        {
            var handlers = XamlCodeBehind.Handlers(document).ToArray();
            var response = new ResponseBuilder("xaml_codebehind", relative);

            response.Summary(handlers.Length, handlers.Length, "handlers");
            response.Note("class=" + (XamlCodeBehind.ClassOf(document) ?? "-"));

            foreach (var handler in handlers)
            {
                response.Line(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{relative}:{handler.Line}  EXACT  {handler.Element}.{handler.Event}  {handler.Method}"));
            }

            return response.ToString();
        });
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
        var located = Located(workspace, path);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var graph = workspace.Indexes.Xaml();

        return graph.Record(located.Value!) is { Failure: null } indexed
            ? Result.Ok(Rendered(indexed, graph))
            : Reread(workspace, located.Value!, graph);
    }

    public static async Task<Result<string>> ValidateAllAsync(
        LoadedWorkspace workspace,
        int maxResults,
        bool includeUnused,
        CancellationToken cancellationToken)
    {
        var graph = workspace.Indexes.Xaml();
        var unused = includeUnused
            ? await XamlDeadCode.UnusedAsync(workspace.Root, graph, cancellationToken).ConfigureAwait(false)
            : [];

        var issues = graph.Files.SelectMany(file => Collect(file, graph)).Concat(unused).ToArray();
        var response = new ResponseBuilder("xaml_validate", "solution");

        response.Summary(ResultCap.Shown(issues.Length, maxResults), issues.Length, "issues", "maxResults= or scope=file with path=");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"scanned={graph.FileCount} files"));

        foreach (var issue in issues.Capped(maxResults))
            response.Line(issue);

        return Result.Ok(response.ToString());
    }

    private static string Rendered(XamlFileRecord file, XamlResourceGraph graph)
    {
        var issues = Issues(file, graph).ToArray();
        var response = new ResponseBuilder("xaml_validate", file.Relative);

        response.Summary(issues.Length, issues.Length, "issues");
        response.Note("dialect=" + file.Dialect);
        Unread(response, graph);

        foreach (var issue in issues)
            response.Line(issue);

        return response.ToString();
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
        var graph = workspace.Indexes.Xaml();
        var hits = graph.Files.SelectMany(file => Found(file, query, kind)).ToArray();
        var response = new ResponseBuilder("xaml_find", query);

        response.Summary(ResultCap.Shown(hits.Length, maxResults), hits.Length, "matches", "kind= or maxResults=");

        foreach (var hit in hits.Capped(maxResults))
            response.Line(hit);

        return Result.Ok(response.ToString());
    }

    private static IEnumerable<string> Collect(XamlFileRecord file, XamlResourceGraph graph) =>
        file.Failure is { } failure
            ? [Issue(file.Relative, 1, "XAML000", failure)]
            : Issues(file, graph);

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

    private static IEnumerable<string> Found(XamlFileRecord file, string query, string kind) => file
        .Elements
        .Where(element => Matches(element, query, kind))
        .Select(element => string.Create(
            CultureInfo.InvariantCulture,
            $"{file.Relative}:{element.Line}  HEURISTIC  {element.TypeName}  {element.Path}"));

    private static bool Matches(XamlElementFact element, string query, string kind) => kind.ToLowerInvariant() switch
    {
        "name" => string.Equals(element.Name, query, StringComparison.Ordinal),
        "resource" or "key" => string.Equals(element.Key, query, StringComparison.Ordinal),
        "uid" => string.Equals(element.Uid, query, StringComparison.Ordinal),
        "binding" => element.Attributes.Any(attribute => attribute.Value.Contains(query, StringComparison.Ordinal)),
        _ => element.TypeName.Equals(query, StringComparison.Ordinal),
    };

    private static IEnumerable<string> Issues(XamlFileRecord file, XamlResourceGraph graph)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in file.Elements)
        {
            if (element.Key is { } key && !keys.Add(key))
                yield return Issue(file.Relative, element.Line, "XAML001", $"duplicate x:Key '{key}'");

            if (element.Name is { } name && !names.Add(name))
                yield return Issue(file.Relative, element.Line, "XAML002", $"duplicate x:Name '{name}'");
        }

        foreach (var reference in MissingResources(file, graph))
            yield return Issue(file.Relative, reference.Line, "XAML003", $"unresolved resource '{reference.Key}', declared in no XAML file under the workspace root");
    }

    private static IEnumerable<XamlResourceReference> MissingResources(XamlFileRecord file, XamlResourceGraph graph) =>
        graph.SkippedCount > 0 ? [] : file.References.Where(reference => !graph.Declares(reference.Key));

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

    private static Result<string> Located(LoadedWorkspace workspace, string path)
    {
        var resolved = PathGuard.Resolve(workspace, path);

        if (!resolved.IsOk)
            return resolved;

        return XamlDocument.IsXaml(resolved.Value!)
            ? resolved
            : Result.Fail<string>(Errors.Invalid($"'{path}' is not a XAML file", "pass a .xaml, .axaml or .paml file"));
    }

    private static Result<(XamlDocument Document, string Relative)> Open(LoadedWorkspace workspace, string path)
    {
        var located = Located(workspace, path);

        if (!located.IsOk)
            return Result.Fail<(XamlDocument, string)>(located.Error!);

        var document = workspace.Indexes.Markup(located.Value!);

        return document.IsOk
            ? Result.Ok((document.Value!, PositionFormat.Relative(workspace.Root, located.Value!)))
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

    private static Result<string> Reread(LoadedWorkspace workspace, string fullPath, XamlResourceGraph graph)
    {
        var opened = workspace.Indexes.Markup(fullPath);

        return opened.IsOk
            ? Result.Ok(Rendered(XamlResourceGraph.Scan(fullPath, workspace.Root, workspace.Indexes.Documents), graph))
            : Result.Fail<string>(opened.Error!);
    }

    private static void Implicit(ResponseBuilder response, XamlResourceGraph graph, string key)
    {
        var styles = graph.Styles
            .Where(style => style.Key is null && string.Equals(style.TargetType, key, StringComparison.Ordinal))
            .ToArray();

        if (styles.Length is 0)
            return;

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{styles.Length} implicit style(s) target '{key}'. Which one wins depends on the dialect's resource lookup order, which this index does not model, so none is named the winner - call xaml_styles({key}) for the BasedOn chains:"));

        foreach (var style in styles)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{style.File}:{style.Line}  HEURISTIC  implicit Style TargetType={style.TargetType} scope={style.Scope}"));
        }
    }
}
