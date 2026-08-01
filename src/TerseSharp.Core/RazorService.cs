using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class RazorService
{
    public static async Task<Result<string>> OutlineAsync(
        LoadedWorkspace workspace,
        string path,
        bool elements,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        return Result.Ok(Outline(opened.Value!, elements, maxResults));
    }

    public static async Task<Result<string>> CodeBehindAsync(
        LoadedWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return Result.Fail<string>(opened.Error!);

        return Result.Ok(CodeBehind(opened.Value!));
    }

    public static async Task<Result<string>> ComponentAsync(
        LoadedWorkspace workspace,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Fail<string>(Errors.Blank("name"));

        var scopes = WorkspaceScopes(workspace);

        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            var type = compilation is null ? null : RazorSemantics.Resolve(compilation, scopes, name);

            if (type is not null && RazorSemantics.IsComponent(type))
                return Result.Ok(await DescribeAsync(workspace, type, cancellationToken).ConfigureAwait(false));
        }

        return Result.Fail<string>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"no component named '{name}' resolves in the loaded workspace"),
            "check the spelling, or add the @using its namespace needs; razor_find kind=component lists the ones in use"));
    }

    public static Result<string> Find(LoadedWorkspace workspace, string query, string kind, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result.Fail<string>(Errors.Blank("query"));

        var hits = RazorIndex.Build(workspace.Root).SelectMany(document => Match(workspace, document, query, kind)).ToArray();
        var response = new ResponseBuilder("razor_find", query);

        response.Summary(Math.Min(hits.Length, maxResults), hits.Length, "hits", "kind= or path=");

        foreach (var hit in hits.Take(maxResults))
            response.Line(hit);

        return Result.Ok(response.ToString());
    }

    public static IReadOnlyList<string> WorkspaceScopes(LoadedWorkspace workspace)
    {
        var documents = RazorIndex.Build(workspace.Root);
        var usings = documents.SelectMany(document => document.Usings).Select(entry => entry.Trim());
        var namespaces = workspace.Solution.Projects.Select(project => project.AssemblyName);

        return [.. usings.Concat(namespaces).Concat(Ambient).Where(scope => scope.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static readonly string[] Ambient =
    [
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Web",
        "Microsoft.AspNetCore.Components.Forms",
        "Microsoft.AspNetCore.Components.Routing",
    ];

    private static string Outline(RazorContext context, bool elements, int maxResults)
    {
        var document = context.Document;
        var shown = document.Elements.Where(element => elements || Interesting(element)).ToArray();
        var total = document.Directives.Count + document.Elements.Count;
        var response = new ResponseBuilder("razor_outline", context.Relative);

        response.Summary(
            Math.Min(document.Directives.Count + shown.Length, maxResults),
            total,
            "nodes",
            elements ? "maxResults=" : "maxResults=; plain HTML is hidden, pass elements=true for the whole tree");

        response.Note(Header(context));

        foreach (var directive in document.Directives.Take(maxResults))
            response.Line(DirectiveRecord(directive));

        foreach (var element in shown.Take(Math.Max(maxResults - document.Directives.Count, 0)))
            response.Line(ElementRecord(context, element));

        Members(context, response);

        return response.ToString();
    }

    private static bool Interesting(RazorElement element) =>
        element.LooksLikeComponent
        || element.Attributes.Any(attribute => RazorBindingService.KindOf(attribute.Name) is not null);

    private static void Members(RazorContext context, ResponseBuilder response)
    {
        var members = context.DeclaredMembers.Where(member => context.LineOf(member) > 0).ToArray();

        if (members.Length is 0)
            return;

        response.Note(string.Create(CultureInfo.InvariantCulture, $"@code  {members.Length} members"));

        foreach (var member in members.OrderBy(context.LineOf))
            response.Line(MemberRecord(context, member));
    }

    private static string MemberRecord(RazorContext context, ISymbol member) => string.Create(
        CultureInfo.InvariantCulture,
        $"  {member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}  {Modifiers(member)}  :{context.LineOf(member)}");

    private static string Modifiers(ISymbol member)
    {
        var parameter = member is IPropertySymbol property && RazorSemantics.Describe(property.ContainingType).Parameter(property.Name) is { } found
            ? found.Kind
            : string.Empty;

        return parameter.Length > 0 ? parameter : member.DeclaredAccessibility.ToString().ToLowerInvariant();
    }

    private static string Header(RazorContext context)
    {
        var type = context.Self is null ? "-" : context.Self.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var routes = context.Document.Routes.ToArray() is { Length: > 0 } declared ? string.Join(",", declared) : "-";
        var mode = context.Document.Value("rendermode") ?? "-";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"kind={context.Document.Kind.ToString().ToLowerInvariant()}  type={type}  routes={routes}  rendermode={mode}  imports={context.Imports.Count}{Health(context)}");
    }

    private static string Health(RazorContext context) => context.GeneratorRan
        ? string.Empty
        : "  generator=unavailable - the Razor source generator produced nothing for this project, so component types cannot be resolved; build it, or match its SDK to the terse version";

    private static string DirectiveRecord(RazorDirective directive) => string.Create(
        CultureInfo.InvariantCulture,
        $"@{directive.Name}  :{directive.Line}  {directive.Value}");

    private static string ElementRecord(RazorContext context, RazorElement element)
    {
        var depth = element.Path.Count(character => character is '/');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{new string(' ', depth * 2)}{element.TagName}  :{element.Line}{Tag(context, element)}");
    }

    private static string Tag(RazorContext context, RazorElement element)
    {
        if (!element.LooksLikeComponent)
            return Wired(element);

        var type = context.Resolve(element.TagName);
        var resolved = type is not null && RazorSemantics.IsComponent(type)
            ? "  EXACT " + type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            : "  HEURISTIC unresolved";

        return resolved + Attributes(element);
    }

    private static string Wired(RazorElement element) => element
        .Attributes
        .Any(attribute => RazorBindingService.KindOf(attribute.Name) is not null)
        ? Attributes(element)
        : string.Empty;

    private static string Attributes(RazorElement element) => element.Attributes.Count is 0
        ? string.Empty
        : "  " + string.Join(" ", element.Attributes.Select(attribute => attribute.Name));

    private static string CodeBehind(RazorContext context)
    {
        var assets = RazorFiles.AssetsOf(context.FullPath);
        var response = new ResponseBuilder("razor_codebehind", context.Relative);
        var members = context.DeclaredMembers.ToArray();

        response.Summary(members.Length, members.Length, "members");
        response.Note(Header(context));
        response.Note("codeBehind=" + Show(context, assets.CodeBehind));
        response.Note("scopedCss=" + Show(context, assets.ScopedCss));
        response.Note("javaScript=" + Show(context, assets.JavaScript));

        foreach (var member in members.OrderBy(context.LineOf))
            response.Line(MemberRecord(context, member));

        return response.ToString();
    }

    private static string Show(RazorContext context, string? path) =>
        path is null ? "-" : PositionFormat.Relative(context.Workspace.Root, path);

    private static async Task<string> DescribeAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        var component = RazorSemantics.Describe(type);
        var response = new ResponseBuilder("razor_component", type.Name);

        response.Summary(component.Parameters.Count, component.Parameters.Count, "parameters");
        response.Note(await OriginAsync(workspace, type, cancellationToken).ConfigureAwait(false));

        foreach (var parameter in component.Parameters)
            response.Line(ParameterRecord(parameter));

        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"routes={(component.Routes.Count is 0 ? "-" : string.Join(",", component.Routes))}  captureUnmatched={component.Parameters.Any(parameter => parameter.CaptureUnmatched)}"));

        return response.ToString();
    }

    private static string ParameterRecord(RazorParameter parameter) => string.Create(
        CultureInfo.InvariantCulture,
        $"{parameter.Name}  {parameter.Type}  {parameter.Kind}");

    private static async Task<string> OriginAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        var source = await SourceOfAsync(workspace, type, cancellationToken).ConfigureAwait(false);
        var full = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var origin = source ?? (type.Locations.Any(location => location.IsInMetadata) ? "metadata" : "-");

        return string.Create(CultureInfo.InvariantCulture, $"{full}  EXACT  {origin}  base={type.BaseType?.Name ?? "-"}");
    }

    private static async Task<string?> SourceOfAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            var generated = await RazorGeneratedMap.InProjectAsync(project, cancellationToken).ConfigureAwait(false);
            var match = generated.FirstOrDefault(entry => SymbolEqualityComparer.Default.Equals(entry.Type, type));

            if (match is not null)
                return PositionFormat.Relative(workspace.Root, match.RazorPath);
        }

        return null;
    }

    private static IEnumerable<string> Match(LoadedWorkspace workspace, RazorDocument document, string query, string kind)
    {
        var relative = PositionFormat.Relative(workspace.Root, document.Path);

        return kind switch
        {
            "directive" => document.Directives
                .Where(directive => Contains(directive.Name, query) || Contains(directive.Value, query))
                .Select(directive => Hit(relative, directive.Line, "directive", "@" + directive.Name + " " + directive.Value)),
            "attribute" => document.Elements
                .SelectMany(element => element.Attributes.Select(attribute => (element, attribute)))
                .Where(pair => Contains(pair.attribute.Name, query))
                .Select(pair => Hit(relative, pair.attribute.Line, "attribute", pair.element.TagName + " " + pair.attribute.Name)),
            "expression" => document.Expressions
                .Where(expression => Contains(expression.Text, query))
                .Select(expression => Hit(relative, expression.Line, "expression", expression.Text.Trim())),
            "route" => document.Routes
                .Where(route => Contains(route, query))
                .Select(route => Hit(relative, 1, "route", route)),
            _ => Elements(document, query, kind, relative),
        };
    }

    private static IEnumerable<string> Elements(RazorDocument document, string query, string kind, string relative) => document
        .Elements
        .Where(element => Contains(element.TagName, query) && KindMatches(element, kind))
        .Select(element => Hit(relative, element.Line, kind is "component" ? "component" : "element", element.Path));

    private static bool KindMatches(RazorElement element, string kind) => kind switch
    {
        "component" => element.LooksLikeComponent,
        "element" => !element.LooksLikeComponent,
        _ => true,
    };

    private static bool Contains(string text, string query) =>
        text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Hit(string relative, int line, string kind, string detail) => string.Create(
        CultureInfo.InvariantCulture,
        $"{relative}:{line}  HEURISTIC  {kind}  {detail}");
}
