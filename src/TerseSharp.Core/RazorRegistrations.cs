using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public readonly record struct RazorRegistrationIndex(IReadOnlySet<string> Names, int Unreadable);

public static class RazorRegistrations
{
    private static readonly FrozenSet<string> HostProvided = new[]
    {
        "AntiforgeryStateProvider",
        "ComponentStatePersistenceManager",
        "HttpContext",
        "IConfiguration",
        "IErrorBoundaryLogger",
        "IHostApplicationLifetime",
        "IHostEnvironment",
        "IJSInProcessRuntime",
        "IJSRuntime",
        "IJSUnmarshalledRuntime",
        "ILogger",
        "ILoggerFactory",
        "IOptions",
        "IOptionsMonitor",
        "IOptionsSnapshot",
        "IServiceProvider",
        "IServiceScopeFactory",
        "IWebAssemblyHostEnvironment",
        "IWebHostEnvironment",
        "NavigationManager",
        "PersistentComponentState",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsHostProvided(string name) => HostProvided.Contains(name);

    public static async Task<RazorRegistrationIndex> IndexAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var unreadable = 0;

        foreach (var document in Documents(workspace))
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            if (root is not null)
                unreadable += Collect(names, root);
        }

        return new RazorRegistrationIndex(names, unreadable);
    }

    private static IEnumerable<Document> Documents(LoadedWorkspace workspace) => workspace
        .Solution
        .Projects
        .SelectMany(project => project.Documents)
        .Where(document => document.FilePath is { Length: > 0 } && !GeneratedCode.IsGenerated(workspace.Root, document.FilePath));

    private static int Collect(HashSet<string> names, SyntaxNode root)
    {
        var unreadable = 0;

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(Registers))
        {
            var named = false;

            foreach (var name in Named(invocation))
            {
                names.Add(name);
                named = true;
            }

            if (!named && Opaque(invocation) && invocation.Expression is MemberAccessExpressionSyntax)
                unreadable++;
        }

        return unreadable;
    }

    private static bool Registers(InvocationExpressionSyntax invocation) =>
        Method(invocation) is { Length: > 3 } name
        && (name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("TryAdd", StringComparison.Ordinal));

    private static string? Method(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    private static IEnumerable<string> Named(InvocationExpressionSyntax invocation) =>
        Arguments(invocation).Concat(TypeArguments(invocation));

    private static IEnumerable<string> TypeArguments(InvocationExpressionSyntax invocation) => invocation.Expression
        .DescendantNodesAndSelf()
        .OfType<GenericNameSyntax>()
        .SelectMany(generic => generic.TypeArgumentList.Arguments)
        .Select(Simple);

    private static IEnumerable<string> Arguments(InvocationExpressionSyntax invocation) => invocation.ArgumentList.Arguments
        .Select(argument => argument.Expression)
        .OfType<TypeOfExpressionSyntax>()
        .Select(expression => Simple(expression.Type));

    private static string Simple(TypeSyntax type) => type switch
    {
        GenericNameSyntax generic => generic.Identifier.ValueText,
        QualifiedNameSyntax qualified => Simple(qualified.Right),
        _ => type.ToString(),
    };

    private static bool Opaque(InvocationExpressionSyntax invocation) => invocation.ArgumentList.Arguments
            .All(argument => argument.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax);
}
