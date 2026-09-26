using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

internal static class DroppedDeclarations
{
    private const int MaxNamed = 6;

    public static string? Warning(string path, string before, string after)
    {
        if (before.Length is 0 || !SourceFile.IsCSharp(path))
            return null;

        var kept = new HashSet<string>(Declared(after), StringComparer.Ordinal);
        var dropped = Declared(before).Where(name => !kept.Contains(name)).Distinct(StringComparer.Ordinal).ToArray();

        return dropped.Length is 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"WARNING this overwrite drops {dropped.Length} declaration(s) the file declared: {Named(dropped)} - dryRun=true shows the diff, write_text ref=HEAD restores the file");
    }

    public static string Warned(string text, string? warning) => warning is null ? text : text + "\n" + warning;

    private static string Named(string[] dropped) => dropped.Length > MaxNamed
        ? string.Join(", ", dropped.Take(MaxNamed)) + string.Create(CultureInfo.InvariantCulture, $" and {dropped.Length - MaxNamed} more")
        : string.Join(", ", dropped);

    private static IEnumerable<string> Declared(string text) => CSharpSyntaxTree
        .ParseText(text)
        .GetRoot()
        .DescendantNodes()
        .OfType<MemberDeclarationSyntax>()
        .SelectMany(Names);

    private static IEnumerable<string> Names(MemberDeclarationSyntax member) => member switch
    {
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => Qualified(member, variable.Identifier.ValueText)),
        _ => Leaf(member) is { } name ? [Qualified(member, name)] : [],
    };

    private static string? Leaf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText + Parameters(method.ParameterList),
        ConstructorDeclarationSyntax constructor => "#ctor" + Parameters(constructor.ParameterList),
        DestructorDeclarationSyntax => "~Finalize",
        OperatorDeclarationSyntax @operator => "operator " + @operator.OperatorToken.ValueText + Parameters(@operator.ParameterList),
        ConversionOperatorDeclarationSyntax conversion => "operator " + conversion.Type + Parameters(conversion.ParameterList),
        IndexerDeclarationSyntax indexer => "this" + Parameters(indexer.ParameterList),
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        EnumMemberDeclarationSyntax enumMember => enumMember.Identifier.ValueText,
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier.ValueText,
        _ => null,
    };

    private static string Parameters(BaseParameterListSyntax list) =>
            "(" + string.Join(", ", list.Parameters.Select(parameter => parameter.Type?.NormalizeWhitespace().ToString() ?? string.Empty)) + ")";

    private static string Qualified(SyntaxNode member, string name) =>
        member.Parent is BaseTypeDeclarationSyntax container
            ? Qualified(container, container.Identifier.ValueText) + "." + name
            : name;

    public static string? Replaced(SyntaxNode target, IReadOnlyList<SyntaxNode> replacements)
    {
        if (target is not BaseTypeDeclarationSyntax type)
            return null;

        var kept = new HashSet<string>(replacements.OfType<BaseTypeDeclarationSyntax>().SelectMany(Inner).Select(Unsigned), StringComparer.Ordinal);
        var dropped = Inner(type).Where(name => !kept.Contains(Unsigned(name))).Distinct(StringComparer.Ordinal).Select(name => type.Identifier.ValueText + "." + name).ToArray();

        return dropped.Length is 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"WARNING this replace drops {dropped.Length} member(s) the type declared: {Named(dropped)} - a header alone, with no body and no ';', re-heads the type and keeps every member");
    }

    private static IEnumerable<string> Inner(BaseTypeDeclarationSyntax type)
    {
        var prefix = Qualified(type, type.Identifier.ValueText).Length + 1;

        return type.DescendantNodes().OfType<MemberDeclarationSyntax>().SelectMany(Names).Select(name => name[prefix..]);
    }

    private static string Unsigned(string name) => name.IndexOf('(', StringComparison.Ordinal) is var open and >= 0 ? name[..open] : name;
}
