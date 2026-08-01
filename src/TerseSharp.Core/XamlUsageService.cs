using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct XamlUsage(string File, int Line, string Kind, string Confidence, string Text, string Replacement);

public static class XamlUsageService
{
    public static IReadOnlyList<XamlUsage> Find(LoadedWorkspace workspace, ISymbol symbol, string newName)
    {
        var graph = workspace.Indexes.Xaml();
        var usages = new List<XamlUsage>();

        foreach (var file in graph.Files.Where(file => Mentions(file, symbol)))
            Collect(usages, graph.Document(file), file.Relative, symbol, newName);

        return usages;
    }

    private static void Collect(
        List<XamlUsage> usages,
        XamlDocument? document,
        string relative,
        ISymbol symbol,
        string newName)
    {
        if (document is null)
            return;

        usages.AddRange(Handlers(document, relative, symbol, newName));
        usages.AddRange(Bindings(document, relative, symbol, newName));
        usages.AddRange(Classes(document, relative, symbol, newName));
    }

    private static IEnumerable<XamlUsage> Handlers(XamlDocument document, string relative, ISymbol symbol, string newName)
    {
        if (symbol is not IMethodSymbol || !OwnsCodeBehind(document, symbol))
            yield break;

        foreach (var handler in XamlCodeBehind.Handlers(document).Where(handler => Same(handler.Method, symbol.Name)))
        {
            yield return new XamlUsage(
                relative,
                handler.Line,
                handler.Event + " handler",
                "EXACT",
                handler.Method,
                newName);
        }
    }

    private static IEnumerable<XamlUsage> Bindings(XamlDocument document, string relative, ISymbol symbol, string newName)
    {
        if (symbol.Kind is not (SymbolKind.Property or SymbolKind.Field))
            yield break;

        foreach (var site in XamlBindingService.Sites(document))
        {
            var path = XamlBindingService.PathOf(site.Expression);

            if (path is null || !path.Split('.').Any(segment => Same(segment, symbol.Name)))
                continue;

            yield return new XamlUsage(
                relative,
                site.Line,
                "binding",
                DeclaresContext(site, symbol) ? "EXACT" : "HEURISTIC",
                site.Expression,
                Rewrite(site.Expression, symbol.Name, newName));
        }
    }

    private static IEnumerable<XamlUsage> Classes(XamlDocument document, string relative, ISymbol symbol, string newName)
    {
        if (symbol is not INamedTypeSymbol || XamlCodeBehind.ClassOf(document) is not { } declared)
            yield break;

        if (Same(Simple(declared), symbol.Name))
            yield return new XamlUsage(relative, 1, "x:Class", "EXACT", declared, Rewrite(declared, symbol.Name, newName));
    }

    private static bool OwnsCodeBehind(XamlDocument document, ISymbol symbol) =>
        XamlCodeBehind.ClassOf(document) is { } declared
        && symbol.ContainingType is { } type
        && Same(Simple(declared), type.Name);

    private static bool DeclaresContext(XamlBindingSite site, ISymbol symbol) =>
        XamlBindingService.ContextTypeName(site.Element) is { } context
        && symbol.ContainingType is { } type
        && Same(Simple(context), type.Name);

    private static ReadOnlySpan<char> Simple(ReadOnlySpan<char> qualified)
    {
        var separator = qualified.LastIndexOfAny('.', ':');

        return separator < 0 ? qualified : qualified[(separator + 1)..];
    }

    private static bool Same(ReadOnlySpan<char> left, ReadOnlySpan<char> right) => left.SequenceEqual(right);

    private static string Rewrite(string text, string oldName, string newName) =>
        string.Join(newName, text.Split(oldName));

    private static bool Mentions(XamlFileRecord file, ISymbol symbol) =>
        file.Failure is not null || file.Mentions.Contains(symbol.Name);
}
