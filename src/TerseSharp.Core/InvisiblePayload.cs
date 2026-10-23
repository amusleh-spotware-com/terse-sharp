using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TerseSharp.Core;

public static class InvisiblePayload
{
    private const char Backslash = (char)92;

    private static readonly SearchValues<char> Invisible = SearchValues.Create([(char)0xFEFF, (char)0x200B, (char)0x200C, (char)0x200D, (char)0x2060, (char)0x00AD]);

    public static async Task<string?> NoteAsync(Solution before, Solution after, IReadOnlyList<DocumentId> changed, string root, CancellationToken cancellationToken)
    {
        foreach (var id in changed)
        {
            if (after.GetDocument(id) is not { FilePath: { } path } document)
                continue;

            var now = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var was = before.GetDocument(id) is { } prior ? await prior.GetTextAsync(cancellationToken).ConfigureAwait(false) : SourceText.From(string.Empty);

            if (FirstAdded(was, now) is { } position)
                return Rendered(PositionFormat.Relative(root, path), now, position);
        }

        return null;
    }

    private static int? FirstAdded(SourceText was, SourceText now)
    {
        var shift = 0;

        foreach (var range in now.GetChangeRanges(was))
        {
            var start = range.Span.Start + shift;

            if (Count(was, range.Span.Start, range.Span.Length) < Count(now, start, range.NewLength))
                return First(now, start, range.NewLength);

            shift += range.NewLength - range.Span.Length;
        }

        return null;
    }

    private static int Count(SourceText text, int start, int length)
    {
        var count = 0;

        for (var at = start; at < start + length; at++)
        {
            if (Invisible.Contains(text[at]))
                count++;
        }

        return count;
    }

    private static int First(SourceText text, int start, int length)
    {
        for (var at = start; at < start + length; at++)
        {
            if (Invisible.Contains(text[at]))
                return at;
        }

        return start;
    }

    private static string Rendered(string path, SourceText text, int position)
    {
        var code = ((int)text[position]).ToString("X4", CultureInfo.InvariantCulture);
        var line = text.Lines.GetLineFromPosition(position).LineNumber + 1;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"NOTE invisible U+{code} written at {path}:{line} - the payload carried the character itself; if the source should hold the escape, send {Backslash}{Backslash}u{code} so it arrives as {Backslash}u{code}");
    }
}
