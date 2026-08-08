using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TerseSharp.Core;

public sealed class CommentStripper : CSharpSyntaxRewriter
{
    public static string Without(SyntaxNode node) => new CommentStripper().Visit(node)?.ToFullString() ?? node.ToFullString();

    public override SyntaxToken VisitToken(SyntaxToken token) => token
        .WithLeadingTrivia(Kept(token.LeadingTrivia, ownLine: true))
        .WithTrailingTrivia(Kept(token.TrailingTrivia, ownLine: false));

    private static SyntaxTriviaList Kept(SyntaxTriviaList trivia, bool ownLine)
    {
        var kept = new List<SyntaxTrivia>(trivia.Count);
        var eatNextEndOfLine = false;

        foreach (var item in trivia)
        {
            if (eatNextEndOfLine && item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                eatNextEndOfLine = false;
                continue;
            }

            eatNextEndOfLine = IsComment(item) && Dropped(kept, ownLine);

            if (!IsComment(item))
                kept.Add(item);
        }

        return SyntaxFactory.TriviaList(kept);
    }

    private static bool Dropped(List<SyntaxTrivia> kept, bool ownLine)
    {
        if (ownLine)
        {
            Unindented(kept);

            return true;
        }

        if (kept.Count is 0 || !kept[^1].IsKind(SyntaxKind.WhitespaceTrivia))
            kept.Add(SyntaxFactory.Space);

        return false;
    }
    private static bool Unindented(List<SyntaxTrivia> kept)
    {
        if (kept.Count is 0 || !kept[^1].IsKind(SyntaxKind.WhitespaceTrivia))
            return false;

        kept.RemoveAt(kept.Count - 1);

        return true;
    }

    private static bool IsComment(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
}
