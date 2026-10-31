using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class InactiveMembers
{
    private const int NamesShown = 8;
    private const string OrGreater = "_OR_GREATER";

    public static string? Note(MemberDeclarationSyntax declaration, SemanticModel model, string? contains)
    {
        if (declaration is not TypeDeclarationSyntax { ContainsDirectives: true } type)
            return null;

        var active = type.Members.Select(Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var inactive = Names(type).Where(name => !active.Contains(name) && (contains is not { Length: > 0 } || name.Contains(contains, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.Ordinal).ToList();

        return inactive.Count is 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"  HEURISTIC {inactive.Count} member(s) under an inactive #if not shown{Framework(model)}: {Shown(inactive)} - load_workspace targetFramework=<the framework that defines them> outlines them");
    }

    private static IEnumerable<string> Names(TypeDeclarationSyntax type) =>
        type.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia) && !type.Members.Any(member => member.Span.Contains(trivia.SpanStart)))
            .SelectMany(trivia => Parsed(trivia.ToFullString()));

    private static IEnumerable<string> Parsed(string disabled)
    {
        var unit = CSharpSyntaxTree.ParseText("class Inactive\n{\n" + disabled + "\n}").GetRoot();

        return unit.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault() is { } wrapper
            ? wrapper.Members.SelectMany(Declared)
            : [];
    }

    private static IEnumerable<string> Declared(MemberDeclarationSyntax member) => member switch
    {
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => variable.Identifier.Text),
        _ => Name(member) is { } name ? [name] : [],
    };

    private static string? Name(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        PropertyDeclarationSyntax property => property.Identifier.Text,
        EventDeclarationSyntax @event => @event.Identifier.Text,
        BaseTypeDeclarationSyntax nested => nested.Identifier.Text,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.Text,
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        _ => null,
    };

    private static string Shown(List<string> names) =>
        names.Count > NamesShown
            ? string.Join(", ", names.Take(NamesShown)) + string.Create(CultureInfo.InvariantCulture, $", +{names.Count - NamesShown} more")
            : string.Join(", ", names);

    private static string Framework(SemanticModel model) =>
        model.SyntaxTree.Options.PreprocessorSymbolNames.LastOrDefault(IsFramework) is { } symbol
            ? " (this outline answers for " + symbol + ")"
            : string.Empty;

    private static bool IsFramework(string symbol) =>
        symbol.StartsWith("NET", StringComparison.Ordinal)
        && !symbol.EndsWith(OrGreater, StringComparison.Ordinal)
        && symbol.AsSpan().ContainsAnyInRange('0', '9');
}
