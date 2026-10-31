using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class ArgumentValues
{
    public static Result<IReadOnlyDictionary<string, ArgumentSyntax>> Parse(string arguments, ParameterListSyntax parameters, IMethodSymbol method)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return Result.Ok<IReadOnlyDictionary<string, ArgumentSyntax>>(new Dictionary<string, ArgumentSyntax>(StringComparer.Ordinal));

        var parsed = SyntaxFactory.ParseArgumentList("(" + arguments + ")");

        return parsed.ContainsDiagnostics
            ? Result.Fail<IReadOnlyDictionary<string, ArgumentSyntax>>(Errors.Invalid(
                "arguments= did not parse",
                "pass the value of each added parameter by name, e.g. 'retries: 3, label: \"x\"'"))
            : Collected(parsed.Arguments, parameters, method);
    }

    private static Result<IReadOnlyDictionary<string, ArgumentSyntax>> Collected(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ParameterListSyntax parameters,
        IMethodSymbol method)
    {
        var values = new Dictionary<string, ArgumentSyntax>(arguments.Count, StringComparer.Ordinal);

        foreach (var argument in arguments)
        {
            var name = argument.NameColon?.Name.Identifier.ValueText;

            if (name is null || !Added(name, parameters, method) || !values.TryAdd(name, argument.WithNameColon(null).WithoutTrivia()))
            {
                return Result.Fail<IReadOnlyDictionary<string, ArgumentSyntax>>(Errors.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"arguments= entry '{argument.ToString()}' does not name a parameter the new list adds, once"),
                    "name each added parameter once, e.g. 'retries: 3'; a kept parameter keeps every caller's own argument"));
            }
        }

        return Result.Ok<IReadOnlyDictionary<string, ArgumentSyntax>>(values);
    }

    private static bool Added(string name, ParameterListSyntax parameters, IMethodSymbol method) =>
        parameters.Parameters.Any(parameter => parameter.Identifier.ValueText == name)
        && method.Parameters.All(parameter => parameter.Name != name);
}
