using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public readonly record struct SignatureText(string Parameters, string Arguments);

public sealed record SignatureTarget(Document Declaring, MethodDeclarationSyntax Declaration, IMethodSymbol Method);

public sealed record SignatureChange(
    IReadOnlyList<string> OldNames,
    ParameterListSyntax Parameters,
    IReadOnlyDictionary<string, ArgumentSyntax> Values);
