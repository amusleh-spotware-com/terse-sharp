using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed record RazorUsage(string Path, int Line, Confidence Confidence, string Text);

public static class RazorUsageService
{
    public static async Task<IReadOnlyList<RazorUsage>> MarkupAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        if (symbol is not INamedTypeSymbol type)
            return [];

        var candidates = RazorIndex.Build(workspace.Root).Where(document => Mentions(document, type.Name)).ToArray();
        var usages = new List<RazorUsage>();

        foreach (var document in candidates)
            usages.AddRange(await InAsync(workspace, document, type, cancellationToken).ConfigureAwait(false));

        return usages;
    }

    public static async Task<IReadOnlyList<RazorUsage>> DeclarationsAsync(
        LoadedWorkspace workspace,
        string query,
        CancellationToken cancellationToken)
    {
        var declarations = new List<RazorUsage>();

        foreach (var project in workspace.Solution.Projects)
        {
            var generated = await RazorGeneratedMap.InProjectAsync(project, cancellationToken).ConfigureAwait(false);

            declarations.AddRange(generated
                .Where(entry => entry.Type.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(entry => workspace.Contains(entry.RazorPath))
                .Select(entry => new RazorUsage(
                    PositionFormat.Relative(workspace.Root, entry.RazorPath),
                    1,
                    Confidence.Exact,
                    string.Create(CultureInfo.InvariantCulture, $"T:{entry.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}  component {entry.Type.Name}"))));
        }

        return declarations;
    }

    public static string Describe(RazorUsage usage) => string.Create(
        CultureInfo.InvariantCulture,
        $"{usage.Path}:{usage.Line}  {ConfidenceTag.Of(usage.Confidence)}  razor markup  {usage.Text}");

    private static bool Mentions(RazorDocument document, string name) =>
        document.Elements.Any(element => string.Equals(element.TagName, name, StringComparison.Ordinal));

    private static async Task<IEnumerable<RazorUsage>> InAsync(
        LoadedWorkspace workspace,
        RazorDocument document,
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        var opened = await RazorOpen.AtAsync(workspace, document.Path, cancellationToken).ConfigureAwait(false);

        if (!opened.IsOk)
            return [];

        var context = opened.Value!;
        var resolved = context.Resolve(type.Name);
        var confidence = SymbolEqualityComparer.Default.Equals(resolved, type) ? Confidence.Exact : Confidence.Heuristic;

        return document.Elements
            .Where(element => string.Equals(element.TagName, type.Name, StringComparison.Ordinal))
            .Select(element => new RazorUsage(context.Relative, element.Line, confidence, "<" + element.TagName + ">"));
    }
}
