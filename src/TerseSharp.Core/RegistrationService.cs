using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record ServiceRegistration(string File, int Line, string Method, string Text, string Container);

public static class RegistrationService
{
    private static readonly string[] Lifetimes =
        ["AddSingleton", "AddScoped", "AddTransient", "AddHostedService", "TryAddSingleton", "TryAddScoped", "TryAddTransient", "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient"];

    private static readonly string[] Endpoints =
        ["MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "MapControllers", "MapHub", "MapGrpcService", "MapRazorPages"];

    public static async Task<string> RegistrationsAsync(
        LoadedWorkspace workspace,
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var found = await CollectAsync(workspace, Lifetimes, query, cancellationToken).ConfigureAwait(false);
        var response = new ResponseBuilder("find_registrations", query);

        response.Summary(Math.Min(maxResults, found.Count), found.Count, "registrations", "a more specific type name");

        if (found.Count is 0)
            response.Note("no AddSingleton/AddScoped/AddTransient call mentions this type; it may be registered by assembly scanning, by a container module, or not at all");

        foreach (var registration in found.Take(maxResults))
            response.Line(Describe(registration));

        return response.ToString();
    }

    public static async Task<string> EndpointsAsync(
        LoadedWorkspace workspace,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var found = await CollectAsync(workspace, Endpoints, string.Empty, cancellationToken).ConfigureAwait(false);
        var routes = RazorRoutes(workspace);
        var response = new ResponseBuilder("list_endpoints", "solution");

        response.Summary(
            Math.Min(maxResults, found.Count) + routes.Count,
            found.Count + routes.Count,
            "endpoint registrations",
            "maxResults=");

        foreach (var route in routes)
            response.Line(route);

        foreach (var registration in found.Take(maxResults))
            response.Line(Describe(registration));

        return response.ToString();
    }

    private static async Task<List<ServiceRegistration>> CollectAsync(
        LoadedWorkspace workspace,
        string[] methods,
        string query,
        CancellationToken cancellationToken)
    {
        var found = new List<ServiceRegistration>();

        foreach (var document in Documents(workspace))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is not null)
                found.AddRange(In(root, document, workspace.Root, methods, query));
        }

        return found;
    }

    private static IEnumerable<Document> Documents(LoadedWorkspace workspace) => workspace
        .Solution
        .Projects
        .SelectMany(project => project.Documents)
        .Where(document => document.FilePath is not null && !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static IEnumerable<ServiceRegistration> In(
        SyntaxNode root,
        Document document,
        string workspaceRoot,
        string[] methods,
        string query)
    {
        var relative = PositionFormat.Relative(workspaceRoot, document.FilePath!);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = Called(invocation);

            if (method is null || !methods.Contains(method, StringComparer.Ordinal))
                continue;

            var text = Condense(invocation.ToString());

            if (query.Length > 0 && !text.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new ServiceRegistration(relative, Line(Named(invocation)), method, text, Container(invocation));
        }
    }

    private static SyntaxNode Named(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name,
        _ => invocation,
    };

    private static string? Called(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => null,
    };

    private static string Container(SyntaxNode node) =>
        UsageContainer.Of(node.SyntaxTree.GetRoot(), node.Span) ?? "-";

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Condense(string text)
    {
        var single = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return single.Length <= 160 ? single : single[..160] + "...";
    }

    private static IReadOnlyList<string> RazorRoutes(LoadedWorkspace workspace) =>
    [
        .. RazorIndex.Build(workspace.Root)
            .SelectMany(document => document.Directives
                .Where(directive => string.Equals(directive.Name, "page", StringComparison.Ordinal))
                .Select(directive => Route(workspace, document, directive))),
    ];

    private static string Route(LoadedWorkspace workspace, RazorDocument document, RazorDirective directive) => string.Create(
        CultureInfo.InvariantCulture,
        $"{PositionFormat.Relative(workspace.Root, document.Path)}:{directive.Line}  EXACT  @page  {directive.Value.Trim('"')}  in {Path.GetFileNameWithoutExtension(document.Path)}");

    private static string Describe(ServiceRegistration registration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{registration.File}:{registration.Line}  HEURISTIC  {registration.Method}  in {registration.Container}  {registration.Text}");
}
