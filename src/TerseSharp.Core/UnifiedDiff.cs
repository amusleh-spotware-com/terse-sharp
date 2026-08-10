using System.Text;

namespace TerseSharp.Core;

public static class UnifiedDiff
{
    private const long MaxAlignmentCells = 2_000_000;

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

    private static List<Block> Blocks(string[] before, string[] after)
    {
        var prefix = CommonPrefix(before, after);
        var suffix = CommonSuffix(before, after, prefix);
        var beforeMiddle = before[prefix..(before.Length - suffix)];
        var afterMiddle = after[prefix..(after.Length - suffix)];

        return beforeMiddle.Length is 0 && afterMiddle.Length is 0
            ? []
            : Aligned(beforeMiddle, afterMiddle, prefix, prefix, MaxSplitDepth);
    }

    private static List<Step> Script(string[] before, string[] after, int[,] alignment)
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

    private static Move Next(string[] before, string[] after, int[,] alignment, int i, int j) =>
        Matches(before, after, i, j) ? new(Step.Keep, 1, 1)
        : TakesAdd(before, after, alignment, i, j) ? new(Step.Add, 0, 1)
        : new(Step.Remove, 1, 0);

    private static bool Matches(string[] before, string[] after, int i, int j) =>
        i < before.Length && j < after.Length && string.Equals(before[i], after[j], StringComparison.Ordinal);

    private static bool TakesAdd(string[] before, string[] after, int[,] alignment, int i, int j) =>
        j < after.Length && (i == before.Length || alignment[i, j + 1] >= alignment[i + 1, j]);

    private static List<Block> Grouped(List<Step> steps, int beforeOffset, int afterOffset)
    {
        var blocks = new List<Block>();
        var start = new Position(beforeOffset, afterOffset);
        var at = start;

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

        return blocks;
    }

    private static Block Span(Position start, Position end) => new(
        start.Before,
        end.Before - start.Before,
        start.After,
        end.After - start.After);

    private static int[,] Alignment(string[] before, string[] after)
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

    private static int CommonPrefix(string[] before, string[] after)
    {
        var limit = Math.Min(before.Length, after.Length);
        var index = 0;

        while (index < limit && string.Equals(before[index], after[index], StringComparison.Ordinal))
            index++;

        return index;
    }

    private static int CommonSuffix(string[] before, string[] after, int prefix)
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

    private const int MaxSplitDepth = 24;

    private readonly record struct Anchored(int Before, int After);

    private static Dictionary<string, int> Counts(string[] lines)
    {
        var counts = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);

        foreach (var line in lines)
            counts[line] = counts.GetValueOrDefault(line) + 1;

        return counts;
    }

    private static Dictionary<string, int> Once(string[] lines)
    {
        var index = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);

        for (var at = 0; at < lines.Length; at++)
        {
            if (!index.TryAdd(lines[at], at))
                index[lines[at]] = -1;
        }

        return index;
    }

    private static Anchored? Anchor(string[] before, string[] after)
    {
        var counts = Counts(before);
        var once = Once(after);
        var middle = before.Length / 2;
        var distance = int.MaxValue;
        Anchored? best = null;

        for (var index = 0; index < before.Length; index++)
        {
            var at = once.GetValueOrDefault(before[index], -1);
            var gap = Math.Abs(index - middle);

            if (at < 0 || gap >= distance || counts.GetValueOrDefault(before[index]) is not 1)
                continue;

            (distance, best) = (gap, new Anchored(index, at));
        }

        return best;
    }

    private static List<Block> Aligned(string[] before, string[] after, int beforeStart, int afterStart, int depth)
    {
        if ((long)before.Length * after.Length <= MaxAlignmentCells)
            return Grouped(Script(before, after, Alignment(before, after)), beforeStart, afterStart);

        if (depth is 0 || Anchor(before, after) is not { } anchor)
            return [new Block(beforeStart, before.Length, afterStart, after.Length)];

        var head = Aligned(before[..anchor.Before], after[..anchor.After], beforeStart, afterStart, depth - 1);

        head.AddRange(Aligned(
            before[(anchor.Before + 1)..],
            after[(anchor.After + 1)..],
            beforeStart + anchor.Before + 1,
            afterStart + anchor.After + 1,
            depth - 1));

        return head;
    }
}

public readonly record struct DiffReport(string Text, int ChangedLines);
