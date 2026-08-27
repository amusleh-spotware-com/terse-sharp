using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class PolicyService
{
    public static IReadOnlyList<PolicyFinding> Inspect(SyntaxNode root, string path, PolicyOptions options)
    {
        if (!options.Active)
            return [];

        var found = new List<PolicyFinding>();
        var scope = new Scope(path, options, found);

        foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            Types(type, scope);

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            Members(member, scope);

        Expressions(root, scope);

        return found;
    }

    private static void Types(BaseTypeDeclarationSyntax type, Scope scope)
    {
        scope.Check(PolicyRule.MeaninglessSuffix, type, Suffix(type, scope));
        scope.Check(PolicyRule.TypeMethods, type, Methods(type, scope));
        scope.Check(PolicyRule.Naming, type, TypeName(type, scope));

        foreach (var parameter in TypeParameters(type))
            scope.Check(PolicyRule.Naming, parameter, Named(parameter.Identifier, NamingKind.TypeParameter, scope));
    }

    private static void Members(MemberDeclarationSyntax member, Scope scope)
    {
        scope.Check(PolicyRule.CognitiveComplexity, member, Cognitive(member, scope));
        scope.Check(PolicyRule.MethodStatements, member, Statements(member, scope));
        scope.Check(PolicyRule.NestingDepth, member, Depth(member, scope));
        scope.Check(PolicyRule.ParameterCount, member, Parameters(member, scope));
        scope.Check(PolicyRule.ConstructorDependencies, member, Dependencies(member, scope));
        scope.Check(PolicyRule.MethodNameLength, member, NameLength(member, scope));
        scope.Check(PolicyRule.AsyncVoid, member, AsyncVoid(member));
        scope.Check(PolicyRule.Naming, member, MemberName(member, scope));
    }

    private static void Expressions(SyntaxNode root, Scope scope)
    {
        foreach (var condition in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
            scope.Check(PolicyRule.ComplexCondition, condition, Condition(condition, scope));

        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            scope.Check(PolicyRule.ChainedReferences, access, Chain(access, scope));

        foreach (var declared in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            scope.Check(PolicyRule.Naming, declared, Local(declared, scope));
    }

    private static Measure? Cognitive(MemberDeclarationSyntax member, Scope scope)
    {
        if (!Bodied(member))
            return null;

        var score = CognitiveComplexity.Of(member);
        var limit = scope.Options.CognitiveLimit();
        var percent = scope.Options.CognitiveThreshold is 0 ? 0 : score * 100 / scope.Options.CognitiveThreshold;

        return score <= limit
            ? null
            : new Measure(
                string.Create(CultureInfo.InvariantCulture, $"cognitive complexity {score} ({percent}% of threshold {scope.Options.CognitiveThreshold})"),
                string.Create(CultureInfo.InvariantCulture, $"{scope.Options.Limit(PolicyRule.CognitiveComplexity).Value}% ({limit})"));
    }

    private static Measure? Statements(MemberDeclarationSyntax member, Scope scope)
    {
        if (Body(member) is not { } body)
            return null;

        var count = body.DescendantNodes(node => node is not AnonymousFunctionExpressionSyntax)
            .Count(node => node is StatementSyntax and not BlockSyntax);

        return Over(count, scope.Limit(PolicyRule.MethodStatements), "statements");
    }

    private static Measure? Depth(MemberDeclarationSyntax member, Scope scope)
    {
        if (Body(member) is not { } body)
            return null;

        var deepest = body.DescendantNodes()
            .OfType<BlockSyntax>()
            .Select(block => block.Ancestors().TakeWhile(node => node != member).Count(Nests))
            .DefaultIfEmpty(0)
            .Max();

        return Over(deepest, scope.Limit(PolicyRule.NestingDepth), "nesting levels");
    }

    private static bool Nests(SyntaxNode node) => node switch
    {
        IfStatementSyntax statement => statement.Parent is not ElseClauseSyntax,
        ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax
            or DoStatementSyntax or SwitchStatementSyntax or TryStatementSyntax or UsingStatementSyntax
            or LockStatementSyntax => true,
        _ => false,
    };

    private static Measure? Parameters(MemberDeclarationSyntax member, Scope scope) =>
        member is BaseMethodDeclarationSyntax { ParameterList.Parameters.Count: var count } and not ConstructorDeclarationSyntax
            ? Over(count, scope.Limit(PolicyRule.ParameterCount), "parameters")
            : null;

    private static Measure? Dependencies(MemberDeclarationSyntax member, Scope scope) => member switch
    {
        ConstructorDeclarationSyntax constructor =>
            Over(constructor.ParameterList.Parameters.Count, scope.Limit(PolicyRule.ConstructorDependencies), "constructor dependencies"),
        TypeDeclarationSyntax { ParameterList: { } primary } =>
            Over(primary.Parameters.Count, scope.Limit(PolicyRule.ConstructorDependencies), "constructor dependencies"),
        _ => null,
    };

    private static Measure? NameLength(MemberDeclarationSyntax member, Scope scope)
    {
        if (member is not MethodDeclarationSyntax method)
            return null;

        var length = method.Identifier.ValueText.Length;
        var minimum = scope.Limit(PolicyRule.MethodNameLength);

        return length >= minimum
            ? null
            : new Measure(
                string.Create(CultureInfo.InvariantCulture, $"method name '{method.Identifier.ValueText}' is {length} characters"),
                string.Create(CultureInfo.InvariantCulture, $"a minimum of {minimum}"));
    }

    private static Measure? AsyncVoid(MemberDeclarationSyntax member) =>
        member is MethodDeclarationSyntax method
            && method.Modifiers.Any(SyntaxKind.AsyncKeyword)
            && method.ReturnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)
                ? new Measure("async void method", "Task or ValueTask")
                : null;

    private static Measure? Suffix(BaseTypeDeclarationSyntax type, Scope scope)
    {
        var name = type.Identifier.ValueText;
        var matched = scope.Options.MeaninglessSuffixes
            .FirstOrDefault(suffix => name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal));

        return matched is null
            ? null
            : new Measure(
                string.Create(CultureInfo.InvariantCulture, $"type name '{name}' ends with '{matched}'"),
                string.Create(CultureInfo.InvariantCulture, $"a name carrying none of {string.Join(", ", scope.Options.MeaninglessSuffixes)}"));
    }

    private static Measure? Methods(BaseTypeDeclarationSyntax type, Scope scope) =>
        type is TypeDeclarationSyntax declaration
            ? Over(declaration.Members.OfType<MethodDeclarationSyntax>().Count(), scope.Limit(PolicyRule.TypeMethods), "methods")
            : null;

    private static Measure? Condition(BinaryExpressionSyntax condition, Scope scope)
    {
        if (!Logical(condition))
            return null;

        if (Outer(condition) is BinaryExpressionSyntax parent && Logical(parent))
            return null;

        var operands = condition.DescendantNodesAndSelf().Count(Logical) + 1;

        return Over(operands, scope.Limit(PolicyRule.ComplexCondition), "condition operands");
    }

    private static bool Logical(SyntaxNode node) =>
        node is BinaryExpressionSyntax binary
            && (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression));

    private static Measure? Chain(MemberAccessExpressionSyntax access, Scope scope)
    {
        if (Outer(access) is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
            return null;

        var links = 0;

        for (SyntaxNode node = access; node is MemberAccessExpressionSyntax member; node = member.Expression)
            links++;

        return Over(links, scope.Limit(PolicyRule.ChainedReferences), "chained references");
    }

    private static Measure? TypeName(BaseTypeDeclarationSyntax type, Scope scope) =>
        Named(type.Identifier, type is InterfaceDeclarationSyntax ? NamingKind.Interface : NamingKind.Type, scope);

    private static Measure? MemberName(MemberDeclarationSyntax member, Scope scope) => member switch
    {
        MethodDeclarationSyntax method => Named(method.Identifier, NamingKind.Method, scope),
        PropertyDeclarationSyntax property => Named(property.Identifier, NamingKind.Property, scope),
        EventDeclarationSyntax declared => Named(declared.Identifier, NamingKind.Event, scope),
        EnumMemberDeclarationSyntax enumerated => Named(enumerated.Identifier, NamingKind.EnumMember, scope),
        _ => null,
    };

    private static Measure? Local(VariableDeclaratorSyntax declared, Scope scope) =>
        Named(declared.Identifier, Kind(declared), scope);

    private static NamingKind Kind(VariableDeclaratorSyntax declared) => declared.Parent switch
    {
        VariableDeclarationSyntax { Parent: EventFieldDeclarationSyntax } => NamingKind.Event,
        VariableDeclarationSyntax { Parent: BaseFieldDeclarationSyntax field } => FieldKind(field),
        _ => NamingKind.Local,
    };

    private static Measure? Named(SyntaxToken identifier, NamingKind kind, Scope scope)
    {
        var name = identifier.ValueText;

        if (name.Length is 0 || !scope.Options.Naming.TryGetValue(kind, out var pattern))
            return null;

        try
        {
            if (pattern.Matcher.IsMatch(name))
                return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return new Measure(
            string.Create(CultureInfo.InvariantCulture, $"{NamingDefaults.Name(kind)} name '{name}'"),
            string.Create(CultureInfo.InvariantCulture, $"the pattern {pattern.Expression}"));
    }

    private static SeparatedSyntaxList<TypeParameterSyntax> TypeParameters(BaseTypeDeclarationSyntax type) =>
            type is TypeDeclarationSyntax { TypeParameterList: { } declared } ? declared.Parameters : default;

    private static Measure? Over(int measured, int limit, string unit) =>
        measured <= limit
            ? null
            : new Measure(
                string.Create(CultureInfo.InvariantCulture, $"{measured} {unit}"),
                string.Create(CultureInfo.InvariantCulture, $"{limit}"));

    private static bool Bodied(MemberDeclarationSyntax member) =>
        member is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax;

    private static SyntaxNode? Body(MemberDeclarationSyntax member) => member switch
    {
        BaseMethodDeclarationSyntax { Body: { } block } => block,
        BaseMethodDeclarationSyntax { ExpressionBody: { } arrow } => arrow,
        _ => null,
    };

    private readonly record struct Measure(string Measured, string Allowed);

    private sealed record Scope(string Path, PolicyOptions Options, List<PolicyFinding> Found)
    {
        public int Limit(PolicyRule rule) => Options.Limit(rule).Value;

        public void Check(PolicyRule rule, SyntaxNode node, Measure? measure)
        {
            if (measure is not { } found || !Options.Enforces(rule))
                return;

            var position = node.GetLocation().GetLineSpan().StartLinePosition;

            Found.Add(new PolicyFinding(
                rule,
                Options.Limit(rule).Action,
                Path,
                position.Line + 1,
                position.Character + 1,
                Declaration(node),
                found.Measured,
                found.Allowed));
        }

        private static string Declaration(SyntaxNode node) => node.AncestorsAndSelf()
            .Select(Name)
            .OfType<string>()
            .Reverse()
            .DefaultIfEmpty("<file>")
            .Aggregate((outer, inner) => string.Create(CultureInfo.InvariantCulture, $"{outer}.{inner}"));

        private static string? Name(SyntaxNode node) => node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            VariableDeclaratorSyntax declared => declared.Identifier.ValueText,
            _ => null,
        };
    }

    private static NamingKind FieldKind(BaseFieldDeclarationSyntax field) =>
            field.Modifiers.Any(SyntaxKind.ConstKeyword)
                || (field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                    ? NamingKind.Constant
                    : NamingKind.Field;

    private static SyntaxNode? Outer(SyntaxNode node)
    {
        var parent = node.Parent;

        while (parent is ParenthesizedExpressionSyntax)
            parent = parent.Parent;

        return parent;
    }
}
