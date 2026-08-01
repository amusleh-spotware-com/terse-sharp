using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record RegistrationIndex(
    IReadOnlyList<ServiceRegistration> Registrations,
    IReadOnlyList<ServiceRegistration> Endpoints)
{
    private static readonly string[] LifetimeMethods =
        ["AddSingleton", "AddScoped", "AddTransient", "AddHostedService", "TryAddSingleton", "TryAddScoped", "TryAddTransient", "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient"];

    private static readonly string[] EndpointMethods =
        ["MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "MapControllers", "MapHub", "MapGrpcService", "MapRazorPages"];

    public int Count => Registrations.Count + Endpoints.Count;

    public static async Task<RegistrationIndex> OfAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var found = new Collected([], []);

        foreach (var document in Documents(workspace))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is not null)
                Collect(root, PositionFormat.Relative(workspace.Root, document.FilePath!), found);
        }

        return new RegistrationIndex(found.Registrations, found.Endpoints);
    }

    private static IEnumerable<Document> Documents(LoadedWorkspace workspace) => workspace
        .Solution
        .Projects
        .SelectMany(project => project.Documents)
        .Where(document => document.FilePath is not null && !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static void Collect(SyntaxNode root, string relative, Collected found)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            Route(invocation, relative, found);
    }

    private static void Route(InvocationExpressionSyntax invocation, string relative, Collected found)
    {
        if (Called(invocation) is not { } method)
            return;

        if (LifetimeMethods.Contains(method, StringComparer.Ordinal))
            found.Registrations.Add(Describe(invocation, relative, method));
        else if (EndpointMethods.Contains(method, StringComparer.Ordinal))
            found.Endpoints.Add(Describe(invocation, relative, method));
    }

    private static ServiceRegistration Describe(InvocationExpressionSyntax invocation, string relative, string method) =>
        new(relative, Line(Named(invocation)), method, Condense(invocation.ToString()), Container(invocation));

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

    private sealed record Collected(List<ServiceRegistration> Registrations, List<ServiceRegistration> Endpoints);
}
