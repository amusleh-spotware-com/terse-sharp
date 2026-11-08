using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

internal static class SyntaxShape
{
    private const int BumpDepth = 2;
    private const int ComplexConditionalOperators = 2;

    public static bool IsBranch(SyntaxNode node) => node switch
    {
        IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
            or ForEachVariableStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax
            or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or BinaryPatternSyntax => true,
        SwitchExpressionArmSyntax arm => arm.Pattern is not DiscardPatternSyntax,
        BinaryExpressionSyntax binary => IsLogical(binary) || binary.IsKind(SyntaxKind.CoalesceExpression),
        _ => false,
    };

    public static int Depth(SyntaxNode scope, SyntaxNode boundary) => scope.DescendantNodesAndSelf()
        .Where(Nests)
        .Select(node => node.AncestorsAndSelf().TakeWhile(ancestor => ancestor != boundary).Count(Nests))
        .DefaultIfEmpty(0)
        .Max();

    public static int Bumps(SyntaxNode body) =>
        body is BlockSyntax block ? block.Statements.Count(statement => Depth(statement, block) >= BumpDepth) : 0;

    public static bool HasComplexConditional(SyntaxNode body) =>
        body.DescendantNodesAndSelf().Select(Condition).OfType<ExpressionSyntax>().Any(IsComplexConditional);

    public static int Lines(SyntaxNode node)
    {
        var text = node.SyntaxTree.GetText();
        var span = text.Lines.GetLinePositionSpan(node.Span);
        var count = 0;

        for (var line = span.Start.Line; line <= span.End.Line; line++)
            count += IsBlank(text, text.Lines[line].Span) ? 0 : 1;

        return count;
    }

    private static bool IsLogical(SyntaxNode node) =>
        node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression);

    private static bool Nests(SyntaxNode node) => node switch
    {
        IfStatementSyntax statement => statement.Parent is not ElseClauseSyntax,
        ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax
            or DoStatementSyntax or SwitchStatementSyntax => true,
        _ => false,
    };

    private static ExpressionSyntax? Condition(SyntaxNode node) => node switch
    {
        IfStatementSyntax statement => statement.Condition,
        WhileStatementSyntax statement => statement.Condition,
        DoStatementSyntax statement => statement.Condition,
        ForStatementSyntax statement => statement.Condition,
        ConditionalExpressionSyntax expression => expression.Condition,
        _ => null,
    };

    private static bool IsComplexConditional(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf().Count(IsLogical) >= ComplexConditionalOperators;

    private static bool IsBlank(SourceText text, TextSpan span)
    {
        for (var position = span.Start; position < span.End; position++)
        {
            if (!char.IsWhiteSpace(text[position]))
                return false;
        }

        return true;
    }
}
