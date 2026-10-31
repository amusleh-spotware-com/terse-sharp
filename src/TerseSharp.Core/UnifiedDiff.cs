using System.Text;

namespace TerseSharp.Core;

public static class UnifiedDiff
{
    private const long MaxAlignmentCells = 2_000_000;

    private const int MaxSplitDepth = 24;

    public static string Between(string path, string before, string after) => Report(path, before, after).Text;

    public static DiffReport Report(string path, string before, string after)
    {
        var beforeLines = Split(before);
        var afterLines = Split(after);
        var blocks = Blocks(beforeLines, afterLines);
        var text = new StringBuilder(256);

        text.Append(CultureInfo.InvariantCulture, $"--- {path}\n+++ {path}\n");

        AppendHunks(text, beforeLines, afterLines, blocks);

        return new DiffReport(text.ToString(), Counted(blocks));
    }

    public static int ChangedLines(string before, string after) => Counted(Blocks(Split(before), Split(after)));

    private static int Counted(List<Block> blocks)
    {
        var changed = 0;

        foreach (var block in blocks)
            changed += Math.Max(block.BeforeCount, block.AfterCount);

        return changed;
    }

    private static void AppendHunks(StringBuilder text, string[] before, string[] after, List<Block> blocks)
    {
        foreach (var block in blocks)
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"@@ -{block.BeforeStart + 1},{block.BeforeCount} +{block.AfterStart + 1},{block.AfterCount} @@\n");

            for (var index = 0; index < block.BeforeCount; index++)
                text.Append('-').Append(before[block.BeforeStart + index]).Append('\n');

            for (var index = 0; index < block.AfterCount; index++)
                text.Append('+').Append(after[block.AfterStart + index]).Append('\n');
        }
    }

    private static List<Block> Blocks(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        var prefix = CommonPrefix(before, after);
        var suffix = CommonSuffix(before, after, prefix);
        var blocks = new List<Block>();

        Aligned(
            blocks,
            before[prefix..(before.Length - suffix)],
            after[prefix..(after.Length - suffix)],
            new Frame(new Position(prefix, prefix), MaxSplitDepth));

        return blocks;
    }

    private static void Aligned(List<Block> blocks, ReadOnlySpan<string> before, ReadOnlySpan<string> after, Frame frame)
    {
        if (before.IsEmpty && after.IsEmpty)
            return;

        if ((long)before.Length * after.Length <= MaxAlignmentCells)
            Grouped(blocks, Script(before, after, Alignment(before, after)), frame.Start);
        else if (frame.Depth is 0 || Anchors(before, after) is not { Length: > 0 } anchors)
            blocks.Add(new Block(frame.Start.Before, before.Length, frame.Start.After, after.Length));
        else
            Spanned(blocks, before, after, frame, anchors);
    }

    private static void Spanned(List<Block> blocks, ReadOnlySpan<string> before, ReadOnlySpan<string> after, Frame frame, Anchored[] anchors)
    {
        var at = new Position(0, 0);

        foreach (var anchor in anchors)
        {
            Aligned(blocks, before[at.Before..anchor.Before], after[at.After..anchor.After], frame.Inner(at));
            at = new(anchor.Before + 1, anchor.After + 1);
        }

        Aligned(blocks, before[at.Before..], after[at.After..], frame.Inner(at));
    }

    private static List<Step> Script(ReadOnlySpan<string> before, ReadOnlySpan<string> after, int[,] alignment)
    {
        var steps = new List<Step>(before.Length + after.Length);

        for (int i = 0, j = 0; i < before.Length || j < after.Length;)
        {
            var move = Next(before, after, alignment, i, j);

            steps.Add(move.Step);
            i += move.Before;
            j += move.After;
        }

        return steps;
    }

    private static Move Next(ReadOnlySpan<string> before, ReadOnlySpan<string> after, int[,] alignment, int i, int j) =>
        Matches(before, after, i, j) ? new(Step.Keep, 1, 1)
        : TakesAdd(before, after, alignment, i, j) ? new(Step.Add, 0, 1)
        : new(Step.Remove, 1, 0);

    private static bool Matches(ReadOnlySpan<string> before, ReadOnlySpan<string> after, int i, int j) =>
        i < before.Length && j < after.Length && string.Equals(before[i], after[j], StringComparison.Ordinal);

    private static bool TakesAdd(ReadOnlySpan<string> before, ReadOnlySpan<string> after, int[,] alignment, int i, int j) =>
        j < after.Length && (i == before.Length || alignment[i, j + 1] >= alignment[i + 1, j]);

    private static void Grouped(List<Block> blocks, List<Step> steps, Position origin)
    {
        var start = origin;
        var at = origin;

        foreach (var step in steps)
        {
            if (step is Step.Keep && at != start)
                blocks.Add(Span(start, at));

            at = at.Advance(step);

            if (step is Step.Keep)
                start = at;
        }

        if (at != start)
            blocks.Add(Span(start, at));
    }

    private static Block Span(Position start, Position end) => new(
        start.Before,
        end.Before - start.Before,
        start.After,
        end.After - start.After);

    private static int[,] Alignment(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        var lengths = new int[before.Length + 1, after.Length + 1];

        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(before[i], after[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        return lengths;
    }

    private static int CommonPrefix(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        var limit = Math.Min(before.Length, after.Length);
        var index = 0;

        while (index < limit && string.Equals(before[index], after[index], StringComparison.Ordinal))
            index++;

        return index;
    }

    private static int CommonSuffix(ReadOnlySpan<string> before, ReadOnlySpan<string> after, int prefix)
    {
        var limit = Math.Min(before.Length, after.Length) - prefix;
        var index = 0;

        while (index < limit
            && string.Equals(before[^(index + 1)], after[^(index + 1)], StringComparison.Ordinal))
        {
            index++;
        }

        return index;
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private enum Step
    {
        Keep,
        Remove,
        Add,
    }

    private readonly record struct Move(Step Step, int Before, int After);

    private readonly record struct Block(int BeforeStart, int BeforeCount, int AfterStart, int AfterCount);

    private readonly record struct Position(int Before, int After)
    {
        public Position Advance(Step step) => step switch
        {
            Step.Keep => new(Before + 1, After + 1),
            Step.Remove => new(Before + 1, After),
            _ => new(Before, After + 1),
        };
    }

    private readonly record struct Frame(Position Start, int Depth)
    {
        public Frame Inner(Position offset) => new(new(Start.Before + offset.Before, Start.After + offset.After), Depth - 1);
    }

    private readonly record struct Anchored(int Before, int After);

    private static Dictionary<string, int> Once(ReadOnlySpan<string> lines)
    {
        var index = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);

        for (var at = 0; at < lines.Length; at++)
        {
            if (!index.TryAdd(lines[at], at))
                index[lines[at]] = -1;
        }

        return index;
    }

    private static List<Anchored> Pairs(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        var inBefore = Once(before);
        var inAfter = Once(after);
        var pairs = new List<Anchored>(Math.Min(inBefore.Count, inAfter.Count));

        for (var index = 0; index < before.Length; index++)
        {
            if (inBefore[before[index]] == index && inAfter.GetValueOrDefault(before[index], -1) is >= 0 and var at)
                pairs.Add(new Anchored(index, at));
        }

        return pairs;
    }

    private static Anchored[] Anchors(ReadOnlySpan<string> before, ReadOnlySpan<string> after)
    {
        var pairs = Pairs(before, after);
        var tails = new int[pairs.Count];
        var previous = new int[pairs.Count];
        var length = 0;

        for (var index = 0; index < pairs.Count; index++)
        {
            var slot = Slot(pairs, tails, length, pairs[index].After);

            previous[index] = slot is 0 ? -1 : tails[slot - 1];
            tails[slot] = index;
            length = Math.Max(length, slot + 1);
        }

        return length is 0 ? [] : Chain(pairs, previous, tails[length - 1], length);
    }

    private static int Slot(List<Anchored> pairs, int[] tails, int length, int after)
    {
        var (low, high) = (0, length);

        while (low < high)
        {
            var middle = (low + high) >>> 1;
            (low, high) = pairs[tails[middle]].After < after ? (middle + 1, high) : (low, middle);
        }

        return low;
    }

    private static Anchored[] Chain(List<Anchored> pairs, int[] previous, int last, int length)
    {
        var chain = new Anchored[length];
        var at = last;

        for (var index = length - 1; index >= 0; index--)
        {
            chain[index] = pairs[at];
            at = previous[at];
        }

        return chain;
    }

    public static string Around(string before, string after, int context, int maxLines)
    {
        if (context <= 0)
            return string.Empty;

        var afterLines = Split(after);
        var text = new StringBuilder(256);
        var written = 0;
        var covered = 0;
        var clipped = false;

        foreach (var block in Blocks(Split(before), afterLines))
        {
            if (written >= maxLines)
            {
                clipped = true;
                break;
            }

            var (Written, Covered, Clipped) = Window(text, afterLines, block, context, maxLines - written, covered);

            written += Written;
            covered = Covered;
            clipped = clipped || Clipped;
        }

        if (clipped)
            text.Append("... more changed lines than context= can show - pass verbose=true for the whole diff\n");

        return text.ToString().TrimEnd('\n');
    }

    private static (int Written, int Covered, bool Clipped) Window(StringBuilder text, string[] after, Block block, int context, int budget, int covered)
    {
        var start = Math.Max(covered, Math.Max(0, block.AfterStart - context));
        var end = Math.Min(after.Length, block.AfterStart + block.AfterCount + context);
        var written = 0;

        for (var index = start; index < end && written < budget; index++, written++)
            text.Append(CultureInfo.InvariantCulture, $"{index + 1}: {after[index]}\n");

        return (written, Math.Max(covered, start + written), start + written < end);
    }
}

public readonly record struct DiffReport(string Text, int ChangedLines);
