namespace TerseSharp.Core;

public readonly record struct ServiceRegistration(string File, int Line, string Method, string Text, string Container);

public static class RegistrationService
{
    public static async Task<string> RegistrationsAsync(
        LoadedWorkspace workspace,
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var index = await workspace.Indexes.RegistrationsAsync(workspace, cancellationToken).ConfigureAwait(false);
        var found = index.Registrations.Where(registration => Matches(registration, query)).ToArray();
        var response = new ResponseBuilder("find_registrations", query);

        response.Summary(ResultCap.Shown(found.Length, maxResults), found.Length, "registrations", "a more specific type name");

        if (found.Length is 0)
            response.Note("no AddSingleton/AddScoped/AddTransient call mentions this type; it may be registered by assembly scanning, by a container module, or not at all");

        foreach (var registration in found.Capped(maxResults))
            response.Line(Describe(registration));

        return response.ToString();
    }

    public static async Task<string> EndpointsAsync(
        LoadedWorkspace workspace,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var index = await workspace.Indexes.RegistrationsAsync(workspace, cancellationToken).ConfigureAwait(false);
        var found = index.Endpoints;
        var routes = RazorRoutes(workspace);
        var response = new ResponseBuilder("list_endpoints", "solution");

        response.Summary(
            ResultCap.Shown(found.Count, maxResults) + ResultCap.Shown(routes.Count, maxResults),
            found.Count + routes.Count,
            "endpoint registrations",
            "maxResults=");

        foreach (var route in routes.Capped(maxResults))
            response.Line(route);

        foreach (var registration in found.Capped(maxResults))
            response.Line(Describe(registration));

        return response.ToString();
    }
    private static bool Matches(ServiceRegistration registration, string query) =>
        query.Length is 0 || registration.Text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> RazorRoutes(LoadedWorkspace workspace) =>
    [
        .. workspace.Indexes.Razor().Documents
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
