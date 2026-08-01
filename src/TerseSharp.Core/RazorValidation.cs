using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record RazorFinding(string Code, string Path, int Line, string Subject, string Verdict, string Detail)
{
    public string Render() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Code}  {Path}:{Line}  {Subject}  {Verdict}  {Detail}");
}

public static class RazorValidation
{
    public static async Task<Result<string>> RunAsync(
        LoadedWorkspace workspace,
        string? path,
        string scope,
        string? rules,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var targets = Targets(workspace, path, scope);

        if (targets.Count is 0)
            return Result.Fail<string>(Errors.Invalid("no Razor file matched the request", "pass a .razor path, or scope=solution"));

        var findings = new List<RazorFinding>();
        var contexts = new List<RazorContext>(targets.Count);

        foreach (var target in targets)
            await CollectAsync(workspace, target, contexts, findings, cancellationToken).ConfigureAwait(false);

        findings.AddRange(Routes(contexts));
        findings.AddRange(OrphanStyles(workspace, scope));

        return Result.Ok(Render(workspace, path, scope, Selected(findings, rules), maxResults));
    }

    private static IReadOnlyList<string> Targets(LoadedWorkspace workspace, string? path, string scope) =>
        string.Equals(scope, "solution", StringComparison.OrdinalIgnoreCase)
            ? [.. RazorFiles.Enumerate(workspace.Root)]
            : Single(workspace, path);

    private static IReadOnlyList<string> Single(LoadedWorkspace workspace, string? path)
    {
        var resolved = path is { Length: > 0 } ? PathGuard.Resolve(workspace, path) : Result.Fail<string>(Errors.Blank("path"));

        return resolved.IsOk && RazorDocument.IsRazor(resolved.Value!) ? [resolved.Value!] : [];
    }

    private static async Task CollectAsync(
        LoadedWorkspace workspace,
        string file,
        List<RazorContext> contexts,
        List<RazorFinding> findings,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, file, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return;

        var context = opened.Value!;

        contexts.Add(context);
        findings.AddRange(Parse(context));
        findings.AddRange(Components(context));
        findings.AddRange(Bindings(context));
        findings.AddRange(RouteParameters(context));
        findings.AddRange(await InjectionsAsync(context, cancellationToken).ConfigureAwait(false));
    }

    private static IEnumerable<RazorFinding> Parse(RazorContext context) => context.Document.Issues
        .Select(issue => new RazorFinding(
            "RZR010",
            context.Relative,
            LineOf(issue),
            "markup",
            "MALFORMED",
            issue[(issue.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim() + " - the Razor compiler will reject this file"));

    private static int LineOf(string issue) =>
        int.TryParse(issue.Split(':')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line) ? line : 1;

    private static IEnumerable<RazorFinding> Components(RazorContext context)
    {
        if (!context.GeneratorRan)
        {
            yield return new RazorFinding(
                "RZR000",
                context.Relative,
                1,
                "razor semantics",
                "UNAVAILABLE",
                "the Razor source generator produced nothing for this project, so component and parameter rules were not run");

            yield break;
        }

        foreach (var element in context.Document.Elements.Where(element => element.LooksLikeComponent))
        {
            var type = context.Resolve(element.TagName);

            if (type is null || !RazorSemantics.IsComponent(type))
            {
                yield return Unknown(context, element);

                continue;
            }

            foreach (var finding in Parameters(context, element, RazorSemantics.Describe(type)))
                yield return finding;
        }
    }

    private static RazorFinding Unknown(RazorContext context, RazorElement element) => new(
        "RZR001",
        context.Relative,
        element.Line,
        element.TagName,
        "UNKNOWN_COMPONENT",
        "resolves to no component - it renders as a plain HTML tag; add the @using its namespace needs");

    private static IEnumerable<RazorFinding> Parameters(RazorContext context, RazorElement element, RazorComponent component)
    {
        foreach (var attribute in element.Attributes.Where(attribute => Assignable(attribute.Name)))
        {
            if (component.Parameter(attribute.Name) is null && !component.Parameters.Any(parameter => parameter.CaptureUnmatched))
            {
                yield return new RazorFinding(
                    "RZR002",
                    context.Relative,
                    attribute.Line,
                    element.TagName + "." + attribute.Name,
                    "UNKNOWN_PARAMETER",
                    component.Type.Name + " has no [Parameter] with that name - InvalidOperationException at render");
            }
        }

        foreach (var missing in Missing(element, component))
        {
            yield return new RazorFinding(
                "RZR003",
                context.Relative,
                element.Line,
                element.TagName + "." + missing.Name,
                "MISSING_REQUIRED",
                "[EditorRequired] parameter is never set at this usage");
        }
    }

    private static IEnumerable<RazorParameter> Missing(RazorElement element, RazorComponent component) => component
        .Parameters
        .Where(parameter => parameter.Required && element.Attribute(parameter.Name) is null);

    private static bool Assignable(string attributeName) =>
        !attributeName.StartsWith('@') && !attributeName.Contains('-', StringComparison.Ordinal);

    private static IEnumerable<RazorFinding> Bindings(RazorContext context)
    {
        if (context.Self is null)
            yield break;

        foreach (var site in RazorBindingService.Sites(context.Document).Where(site => site.Kind is "bind" or "ref"))
        {
            if (Member(context.Self, site.Attribute.Expression) is not { } member)
                continue;

            if (site.Kind is "bind" && member is IPropertySymbol { SetMethod: null })
            {
                yield return new RazorFinding(
                    "RZR004",
                    context.Relative,
                    site.Attribute.Line,
                    site.Element.TagName + " " + site.Attribute.Name,
                    "NO_SETTER",
                    member.Name + " has no setter - the binding silently does nothing");
            }

            if (site.Kind is "ref" && Mismatched(context, site.Element, member))
            {
                yield return new RazorFinding(
                    "RZR007",
                    context.Relative,
                    site.Attribute.Line,
                    site.Element.TagName + " @ref",
                    "REF_TYPE",
                    member.Name + " cannot hold a " + site.Element.TagName);
            }
        }
    }

    private static bool Mismatched(RazorContext context, RazorElement element, ISymbol member)
    {
        var captured = member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        var type = context.Resolve(element.TagName);

        return captured is not null && type is not null && !Compatible(captured, type);
    }

    private static bool Compatible(ITypeSymbol captured, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(captured, type)
        || type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract, captured))
        || Bases(type).Any(mid => SymbolEqualityComparer.Default.Equals(mid, captured));

    private static IEnumerable<ITypeSymbol> Bases(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    private static ISymbol? Member(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name.Trim()).FirstOrDefault() is { } found)
                return found;
        }

        return null;
    }

    private static IEnumerable<RazorFinding> RouteParameters(RazorContext context)
    {
        if (context.Self is null)
            yield break;

        foreach (var route in context.Document.Routes)
        {
            foreach (var name in RouteNames(route).Where(name => Member(context.Self, name) is null))
            {
                yield return new RazorFinding(
                    "RZR005",
                    context.Relative,
                    1,
                    route,
                    "ROUTE_PARAMETER",
                    "'" + name + "' has no matching [Parameter] property - the segment is parsed and dropped");
            }
        }
    }

    private static IEnumerable<string> RouteNames(string route) => route
        .Split('{', StringSplitOptions.RemoveEmptyEntries)
        .Where(segment => segment.Contains('}', StringComparison.Ordinal))
        .Select(segment => segment[..segment.IndexOf('}', StringComparison.Ordinal)])
        .Select(segment => segment.Split(':', '?', '*')[0].Trim())
        .Where(name => name.Length > 0);

    private static async Task<IEnumerable<RazorFinding>> InjectionsAsync(RazorContext context, CancellationToken cancellationToken)
    {
        var injected = context.Document.Values("inject").Select(Injected).Where(name => name.Length > 0).ToArray();

        if (injected.Length is 0)
            return [];

        var registered = await RazorRegistrations.NamesAsync(context.Workspace, cancellationToken).ConfigureAwait(false);

        return injected
            .Where(name => !registered.Contains(name))
            .Select(name => new RazorFinding(
                "RZR009",
                context.Relative,
                LineOfInject(context, name),
                name,
                "NOT_REGISTERED",
                "HEURISTIC no DI registration found - InvalidOperationException at first render"));
    }

    private static int LineOfInject(RazorContext context, string name) => context.Document.Directives
        .Where(directive => string.Equals(directive.Name, "inject", StringComparison.Ordinal))
        .Where(directive => directive.Value.Contains(name, StringComparison.Ordinal))
        .Select(directive => directive.Line)
        .FirstOrDefault(1);

    private static string Injected(string declaration) => declaration
        .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(string.Empty)
        .Split('<')[0];

    private static IEnumerable<RazorFinding> Routes(IReadOnlyList<RazorContext> contexts) => contexts
        .SelectMany(context => context.Document.Routes.Select(route => (context, route)))
        .GroupBy(entry => entry.route, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .SelectMany(group => group.Skip(1).Select(entry => new RazorFinding(
            "RZR006",
            entry.context.Relative,
            1,
            entry.route,
            "DUPLICATE_ROUTE",
            "also declared by " + group.First().context.Relative + " - AmbiguousMatchException on navigation")));

    private static IEnumerable<RazorFinding> OrphanStyles(LoadedWorkspace workspace, string scope)
    {
        if (!string.Equals(scope, "solution", StringComparison.OrdinalIgnoreCase))
            return [];

        return WorkspaceFiles
            .Enumerate(workspace.Root, file => file.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
            .Where(file => !File.Exists(file[..^4]))
            .Select(file => new RazorFinding(
                "RZR008",
                PositionFormat.Relative(workspace.Root, file),
                1,
                Path.GetFileName(file),
                "ORPHAN_STYLES",
                "no component owns this scoped stylesheet - it is shipped and never applied"));
    }

    private static IReadOnlyList<RazorFinding> Selected(List<RazorFinding> findings, string? rules)
    {
        var wanted = rules is { Length: > 0 }
            ? rules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return
        [
            .. findings
                .Where(finding => wanted.Length is 0 || wanted.Contains(finding.Code, StringComparer.OrdinalIgnoreCase))
                .OrderBy(finding => finding.Path, StringComparer.Ordinal)
                .ThenBy(finding => finding.Line),
        ];
    }

    private static string Render(
        LoadedWorkspace workspace,
        string? path,
        string scope,
        IReadOnlyList<RazorFinding> findings,
        int maxResults)
    {
        var argument = string.Equals(scope, "solution", StringComparison.OrdinalIgnoreCase)
            ? "solution"
            : PositionFormat.Relative(workspace.Root, path ?? string.Empty);

        var response = new ResponseBuilder("razor_validate", argument);

        response.Summary(Math.Min(findings.Count, maxResults), findings.Count, "findings", "rules=");

        foreach (var finding in findings.Take(maxResults))
            response.Line(finding.Render());

        return response.ToString();
    }
}
