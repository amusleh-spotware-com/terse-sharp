using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record RazorGenerated(string RazorPath, INamedTypeSymbol Type, Project Project);

public static class RazorGeneratedMap
{
    public static async Task<IReadOnlyList<RazorGenerated>> InProjectAsync(Project project, CancellationToken cancellationToken)
    {
        var generated = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var found = new List<RazorGenerated>();

        foreach (var document in generated.Where(candidate => RazorFiles.IsGenerated(candidate.HintName)))
        {
            if (await DescribeAsync(project, document, cancellationToken).ConfigureAwait(false) is { } entry)
                found.Add(entry);
        }

        return found;
    }

    public static async Task<RazorGenerated?> ForFileAsync(
        LoadedWorkspace workspace,
        string razorPath,
        CancellationToken cancellationToken)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            var generated = await InProjectAsync(project, cancellationToken).ConfigureAwait(false);
            var match = generated.FirstOrDefault(entry => PathBoundary.SameFile(entry.RazorPath, razorPath));

            if (match is not null)
                return match;
        }

        return null;
    }

    public static async Task<string?> PathOfAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            var generated = await InProjectAsync(project, cancellationToken).ConfigureAwait(false);
            var match = generated.FirstOrDefault(entry => SymbolEqualityComparer.Default.Equals(entry.Type, type));

            if (match is not null)
                return match.RazorPath;
        }

        return null;
    }

    public static async Task<bool> GeneratorRanAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        foreach (var project in workspace.Solution.Projects)
        {
            if (await InProjectAsync(project, cancellationToken).ConfigureAwait(false) is { Count: > 0 })
                return true;
        }

        return false;
    }

    public static string? RazorPathOf(string generatedText)
    {
        if (!generatedText.StartsWith("#pragma checksum \"", StringComparison.Ordinal))
            return null;

        var start = "#pragma checksum \"".Length;
        var end = generatedText.IndexOf('"', start);

        return end < 0 ? null : generatedText[start..end];
    }

    private static async Task<RazorGenerated?> DescribeAsync(
        Project project,
        SourceGeneratedDocument document,
        CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var razorPath = RazorPathOf(text.ToString(new Microsoft.CodeAnalysis.Text.TextSpan(0, Math.Min(512, text.Length))));

        if (razorPath is null)
            return null;

        var type = await TypeOfAsync(document, cancellationToken).ConfigureAwait(false);

        return type is null ? null : new RazorGenerated(razorPath, type, project);
    }

    private static async Task<INamedTypeSymbol?> TypeOfAsync(
        SourceGeneratedDocument document,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var declaration = root?.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        return declaration is null || model is null
            ? null
            : model.GetDeclaredSymbol(declaration, cancellationToken);
    }
}
