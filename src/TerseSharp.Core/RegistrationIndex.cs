using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record RegistrationIndex(
    IReadOnlyList<ServiceRegistration> Registrations,
    IReadOnlyList<ServiceRegistration> Endpoints)
{
    private const int MaxCondensed = 160;

    private const int MaxStackCondense = 512;

    private static readonly string[] LifetimeMethods =
        ["AddSingleton", "AddScoped", "AddTransient", "AddHostedService", "TryAddSingleton", "TryAddScoped", "TryAddTransient", "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient"];

    private static readonly string[] EndpointMethods =
        ["MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "MapControllers", "MapHub", "MapGrpcService", "MapRazorPages"];

    public int Count => Registrations.Count + Endpoints.Count;

    public static async Task<RegistrationIndex> OfAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var found = new Collected([], []);
        var helpers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var calls = new List<HelperCall>();

        foreach (var document in Documents(workspace))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is null)
                continue;

            var relative = PositionFormat.Relative(workspace.Root, document.FilePath!);

            Collect(root, relative, found);
            Declare(root, helpers);
            Calls(root, relative, calls);
        }

        Expand(found, helpers, calls);

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
        var source = text.AsSpan();
        var buffer = source.Length <= MaxStackCondense ? stackalloc char[MaxStackCondense] : new char[source.Length];
        var written = Collapse(source, buffer);

        return written <= MaxCondensed
            ? new string(buffer[..written])
            : string.Concat(buffer[..MaxCondensed], "...");
    }

    private sealed record Collected(List<ServiceRegistration> Registrations, List<ServiceRegistration> Endpoints);

    private static void Declare(SyntaxNode root, Dictionary<string, List<string>> helpers)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var name = method.Identifier.ValueText;

            if (!name.StartsWith("Add", StringComparison.Ordinal) || LifetimeMethods.Contains(name, StringComparer.Ordinal))
                continue;

            var inner = method
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => Called(invocation) is { } called && LifetimeMethods.Contains(called, StringComparer.Ordinal))
                .Select(invocation => Condense(invocation.ToString()))
                .ToList();

            if (inner.Count > 0)
                helpers[name] = inner;
        }
    }

    private static void Calls(SyntaxNode root, string relative, List<HelperCall> calls)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (Called(invocation) is not { } method
                || !method.StartsWith("Add", StringComparison.Ordinal)
                || LifetimeMethods.Contains(method, StringComparer.Ordinal)
                || Enclosing(invocation) == method)
            {
                continue;
            }

            calls.Add(new HelperCall(relative, Line(Named(invocation)), method, Container(invocation)));
        }
    }

    private static void Expand(Collected found, Dictionary<string, List<string>> helpers, List<HelperCall> calls)
    {
        foreach (var call in calls)
        {
            if (!helpers.TryGetValue(call.Method, out var inner))
                continue;

            foreach (var registration in inner)
            {
                found.Registrations.Add(new ServiceRegistration(
                    call.Relative,
                    call.Line,
                    call.Method,
                    string.Create(CultureInfo.InvariantCulture, $"{registration}  via {call.Method}()"),
                    call.Container));
            }
        }
    }

    private static string? Enclosing(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;

    private sealed record HelperCall(string Relative, int Line, string Method, string Container);

    private static int Collapse(ReadOnlySpan<char> source, Span<char> buffer)
    {
        var written = 0;
        var gap = true;

        foreach (var character in source)
        {
            if (char.IsWhiteSpace(character))
            {
                gap = true;

                continue;
            }

            if (gap && written > 0 && written < buffer.Length)
                buffer[written++] = ' ';

            if (written < buffer.Length)
                buffer[written++] = character;

            gap = false;
        }

        return written;
    }
}
