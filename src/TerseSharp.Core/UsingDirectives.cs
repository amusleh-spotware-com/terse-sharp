using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TerseSharp.Core;

public static class UsingDirectives
{
    public static SyntaxNode Ensured(SyntaxNode root, ImmutableArray<string> usings)
    {
        if (usings.IsDefaultOrEmpty || root is not CompilationUnitSyntax unit)
            return root;

        var current = unit;

        foreach (var requested in usings)
        {
            if (IsNamespace(requested) && !Declares(current, requested.Trim()))
                current = Added(current, Directive(requested.Trim()));
        }

        return current;
    }

    private static CompilationUnitSyntax Added(CompilationUnitSyntax unit, UsingDirectiveSyntax directive) =>
        unit.Usings.Count is 0
            ? Headed(unit, directive)
            : unit.WithUsings(Inserted(unit.Usings, directive));

    private static CompilationUnitSyntax Headed(CompilationUnitSyntax unit, UsingDirectiveSyntax directive)
    {
        if (unit.Members is not [var first, ..] || !Transferable(first.GetLeadingTrivia()))
            return unit.WithUsings(SyntaxFactory.SingletonList(directive));

        return unit
            .WithMembers(unit.Members.Replace(first, first.WithLeadingTrivia()))
            .WithUsings(SyntaxFactory.SingletonList(directive.WithLeadingTrivia(first.GetLeadingTrivia())));
    }

    private static bool Transferable(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (item.IsDirective)
                return false;
        }

        return true;
    }

    public static bool IsNamespace(ReadOnlySpan<char> name)
    {
        var trimmed = name.Trim();

        if (trimmed.IsEmpty)
            return false;

        foreach (var segment in trimmed.Split('.'))
        {
            if (!IsIdentifier(trimmed[segment]))
                return false;
        }

        return true;
    }

    private static bool IsIdentifier(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty || !(char.IsLetter(segment[0]) || segment[0] is '_' or '@'))
            return false;

        foreach (var character in segment)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '@'))
                return false;
        }

        return true;
    }

    private static bool Declares(CompilationUnitSyntax unit, string name)
    {
        foreach (var directive in unit.Usings)
        {
            if (string.Equals(Named(directive), name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static SyntaxList<UsingDirectiveSyntax> Inserted(SyntaxList<UsingDirectiveSyntax> usings, UsingDirectiveSyntax directive)
    {
        var at = Position(usings, directive);

        if (at > 0)
            return usings.Insert(at, directive);

        var first = usings[0];

        return Transferable(first.GetLeadingTrivia())
            ? usings.Replace(first, first.WithLeadingTrivia()).Insert(0, directive.WithLeadingTrivia(first.GetLeadingTrivia()))
            : usings.Insert(1, directive);
    }

    private static int Position(SyntaxList<UsingDirectiveSyntax> usings, UsingDirectiveSyntax directive)
    {
        for (var index = 0; index < usings.Count; index++)
        {
            if (Precedes(directive, usings[index]))
                return index;
        }

        return usings.Count;
    }

    private static bool Precedes(UsingDirectiveSyntax left, UsingDirectiveSyntax right) =>
        IsSystem(left) == IsSystem(right)
            ? string.CompareOrdinal(Named(left), Named(right)) < 0
            : IsSystem(left);

    private static UsingDirectiveSyntax Directive(string name) =>
        SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(name))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

    private static bool IsSystem(UsingDirectiveSyntax directive) =>
        Named(directive) is "System" || Named(directive).StartsWith("System.", StringComparison.Ordinal);

    private static string Named(UsingDirectiveSyntax directive) => directive.Name?.ToString() ?? string.Empty;
}
