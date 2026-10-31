using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class ArgumentOrder
{
    public static ArgumentListSyntax? Rearranged(ArgumentListSyntax original, ArgumentListSyntax current, SignatureChange change, int receiver)
    {
        var bound = Bound(original, change.OldNames, receiver);

        if (bound is null || !ReceiverKept(change, receiver) || Renamed(change, bound, receiver))
            return null;

        var arguments = Arguments(original, current, change, bound, receiver);
        var rebuilt = current.WithArguments(SyntaxFactory.SeparatedList(arguments, ParameterLayout.Commas(original.Arguments, arguments.Count)));

        return string.Equals(rebuilt.ToFullString(), current.ToFullString(), StringComparison.Ordinal) ? current : rebuilt;
    }

    private static Dictionary<string, int>? Bound(ArgumentListSyntax arguments, IReadOnlyList<string> oldNames, int receiver)
    {
        var bound = new Dictionary<string, int>(arguments.Arguments.Count, StringComparer.Ordinal);

        for (var index = 0; index < arguments.Arguments.Count; index++)
        {
            var position = index + receiver;
            var name = arguments.Arguments[index].NameColon?.Name.Identifier.ValueText ?? (position < oldNames.Count ? oldNames[position] : null);

            if (name is null || !bound.TryAdd(name, index))
                return null;
        }

        return bound;
    }

    private static bool ReceiverKept(SignatureChange change, int receiver) =>
        receiver == 0 || FirstName(change.Parameters) == (change.OldNames is [var first, ..] ? first : null);

    private static string? FirstName(ParameterListSyntax parameters) =>
        parameters.Parameters.Count > 0 ? parameters.Parameters[0].Identifier.ValueText : null;

    private static bool Renamed(SignatureChange change, Dictionary<string, int> bound, int receiver) =>
        bound.Keys.Any(name => !change.Parameters.Parameters.Any(parameter => parameter.Identifier.ValueText == name)) && Unsupplied(change, bound, receiver);

    private static bool Unsupplied(SignatureChange change, Dictionary<string, int> bound, int receiver) =>
        change.Parameters.Parameters.Skip(receiver).Any(parameter => Added(change, bound, parameter.Identifier.ValueText));

    private static List<ArgumentSyntax> Arguments(
        ArgumentListSyntax original,
        ArgumentListSyntax current,
        SignatureChange change,
        Dictionary<string, int> bound,
        int receiver)
    {
        var parameters = change.Parameters.Parameters;
        var arguments = new List<ArgumentSyntax>(parameters.Count);
        var named = false;

        for (var index = receiver; index < parameters.Count; index++)
        {
            var argument = Argument(original, current, change, bound, parameters[index].Identifier.ValueText);
            named |= argument is null;

            if (argument is not null)
                arguments.Add(named ? Named(argument, parameters[index].Identifier) : argument);
        }

        return arguments;
    }

    private static ArgumentSyntax? Argument(
        ArgumentListSyntax original,
        ArgumentListSyntax current,
        SignatureChange change,
        Dictionary<string, int> bound,
        string name) =>
        bound.TryGetValue(name, out var index)
            ? current.Arguments[index]
            : change.Values.TryGetValue(name, out var value)
                ? value.WithLeadingTrivia(original.Arguments.Count == 0 ? default : original.Arguments[^1].GetLeadingTrivia())
                : null;

    private static ArgumentSyntax Named(ArgumentSyntax argument, SyntaxToken identifier) =>
        argument.NameColon is not null
            ? argument
            : argument.WithoutLeadingTrivia()
                .WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(identifier.WithoutTrivia())).WithTrailingTrivia(SyntaxFactory.Space))
                .WithLeadingTrivia(argument.GetLeadingTrivia());

    private static bool Added(SignatureChange change, Dictionary<string, int> bound, string name) =>
        !bound.ContainsKey(name) && !change.Values.ContainsKey(name) && !change.OldNames.Contains(name, StringComparer.Ordinal);
}
