using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class CognitiveComplexity
{
    public static int Of(SyntaxNode member)
    {
        var walker = new Walker(NameOf(member));

        foreach (var child in Bodies(member))
            walker.Visit(child);

        return walker.Score;
    }

    private static IEnumerable<SyntaxNode> Bodies(SyntaxNode member) => member switch
    {
        BaseMethodDeclarationSyntax method => Present(method.Body, method.ExpressionBody),
        BasePropertyDeclarationSyntax property => Accessors(property),
        AnonymousFunctionExpressionSyntax lambda when lambda.Body is { } body => [body],
        LocalFunctionStatementSyntax local => Present(local.Body, local.ExpressionBody),
        _ => [member]
    };

    private static IEnumerable<SyntaxNode> Accessors(BasePropertyDeclarationSyntax property)
    {
        if (property is PropertyDeclarationSyntax { ExpressionBody: { } arrow })
            yield return arrow;

        foreach (var accessor in property.AccessorList?.Accessors ?? default)
        {
            if (accessor.Body is { } block)
                yield return block;

            if (accessor.ExpressionBody is { } expression)
                yield return expression;
        }
    }

    private static IEnumerable<SyntaxNode> Present(SyntaxNode? body, SyntaxNode? expression)
    {
        if (body is not null)
            yield return body;

        if (expression is not null)
            yield return expression;
    }

    private static string? NameOf(SyntaxNode member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        LocalFunctionStatementSyntax local => local.Identifier.ValueText,
        _ => null
    };

    private sealed class Walker(string? enclosing) : CSharpSyntaxWalker
    {
        private int nesting;

        public int Score { get; private set; }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            Score += node.Parent is ElseClauseSyntax ? 1 : 1 + nesting;
            Visit(node.Condition);
            Deeper(node.Statement, node.Parent is not ElseClauseSyntax);
            Otherwise(node.Else);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node) => Structure(node.Condition, node.Statement);

        public override void VisitDoStatement(DoStatementSyntax node) => Structure(node.Condition, node.Statement);

        public override void VisitForStatement(ForStatementSyntax node)
        {
            Score += 1 + nesting;
            Visit(node.Declaration);

            foreach (var initializer in node.Initializers)
                Visit(initializer);

            Visit(node.Condition);

            foreach (var incrementor in node.Incrementors)
                Visit(incrementor);

            Deeper(node.Statement, true);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node) => Structure(node.Expression, node.Statement);

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node) => Structure(node.Expression, node.Statement);

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            Score += 1 + nesting;
            Visit(node.Expression);
            Deeper(node.Sections, true);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            Score += 1 + nesting;

            if (node.Filter is { } filter)
            {
                Score += 1;
                Visit(filter.FilterExpression);
            }

            Deeper(node.Block, true);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (StartsRun(node))
                Score += 1;

            base.VisitBinaryExpression(node);
        }

        public override void VisitGotoStatement(GotoStatementSyntax node) => Flat(node);

        public override void VisitBreakStatement(BreakStatementSyntax node) => Flat(node);

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Nest(node);

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Nest(node);

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => Nest(node);

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsRecursive(node))
                Score += 1;

            base.VisitInvocationExpression(node);
        }

        private bool IsRecursive(InvocationExpressionSyntax node) =>
            enclosing is { Length: > 0 }
                && node.Expression is IdentifierNameSyntax identifier
                && string.Equals(identifier.Identifier.ValueText, enclosing, StringComparison.Ordinal);

        private static bool StartsRun(BinaryExpressionSyntax node) =>
            (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression)) && (node.Parent is not BinaryExpressionSyntax parent || !parent.IsKind(node.Kind()));

        private void Structure(SyntaxNode? condition, SyntaxNode body)
        {
            Score += 1 + nesting;
            Visit(condition);
            Deeper(body, true);
        }

        private void Otherwise(ElseClauseSyntax? clause)
        {
            if (clause is null)
                return;

            if (clause.Statement is IfStatementSyntax)
            {
                Visit(clause.Statement);

                return;
            }

            Score += 1;
            Deeper(clause.Statement, false);
        }

        private void Deeper(SyntaxNode? body, bool deeper) => Deeper(body is null ? [] : [body], deeper);

        private void Deeper(IEnumerable<SyntaxNode> bodies, bool deeper)
        {
            if (deeper)
                nesting++;

            foreach (var body in bodies)
                Visit(body);

            if (deeper)
                nesting--;
        }

        private void Flat(SyntaxNode node)
        {
            Score += 1;
            DefaultVisit(node);
        }

        private void Nest(SyntaxNode node)
        {
            nesting++;
            DefaultVisit(node);
            nesting--;
        }
    }
}
