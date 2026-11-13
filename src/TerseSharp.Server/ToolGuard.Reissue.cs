namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private static (string Text, Placement[] Placed) Placing(Stage[] judged, string command, string? origin)
    {
        var stages = origin is null ? [] : Detached(Stages(origin));

        return origin is { } source && stages.Length == judged.Length
            ? (source, Placed(stages, source))
            : (command, Placed(judged, command));
    }

    private static List<Placement> Grouped(List<Placement> allowed, Placement[] placed, string text)
    {
        var blocks = Blocks(placed, text);

        if (blocks.Count is 0)
            return allowed;

        var kept = allowed.FindAll(entry => !blocks.Exists(block => Within(entry.Span, block)));

        foreach (var block in blocks)
        {
            if (Whole(block, allowed, placed))
                kept.Add(new Placement(block, false, false));
        }

        kept.Sort((left, right) => left.Span.Start.Value.CompareTo(right.Span.Start.Value));

        return kept;
    }

    private static List<Range> Blocks(Placement[] placed, string text)
    {
        var blocks = new List<Range>();
        var (depth, start) = (0, 0);

        foreach (var placement in placed)
        {
            var opened = depth > 0;

            (start, depth) = (opened ? start : placement.Span.Start.Value, Math.Max(0, depth + Nesting(placement, text)));

            if (opened && depth is 0)
                blocks.Add(start..placement.Span.End.Value);
        }

        if (depth > 0)
            blocks.Add(start..text.Length);

        return blocks;
    }

    private static int Nesting(Placement placement, string text) => placement.Body ? 0 : Nesting(text.AsSpan()[placement.Span]);

    private static int Nesting(ReadOnlySpan<char> stage)
    {
        var word = FirstWord(stage);

        while (word is "do" or "then" or "else" or "elif" or "!")
        {
            stage = stage.TrimStart()[word.Length..];
            word = FirstWord(stage);
        }

        return word switch
        {
            "for" or "while" or "until" or "if" or "select" => 1,
            "done" or "fi" => -1,
            _ => 0,
        };
    }

    private static ReadOnlySpan<char> FirstWord(ReadOnlySpan<char> text)
    {
        var trimmed = text.TrimStart();
        var end = trimmed.IndexOfAnyWhiteSpace();

        return end < 0 ? trimmed : trimmed[..end];
    }

    private static bool Whole(Range block, List<Placement> allowed, Placement[] placed) =>
        Array.TrueForAll(placed, stage => !Within(stage.Span, block) || allowed.Exists(entry => Within(stage.Span, entry.Span)));

    private static bool Within(Range inner, Range outer) =>
        inner.Start.Value >= outer.Start.Value && inner.End.Value <= outer.End.Value;
}

public static partial class ToolGuard
{
    private static void PathExpansions(string token, List<string> operands)
    {
        var expansions = new List<string>(2);
        var balanced = token.AsSpan().Count('{') > token.AsSpan().Count('}') ? token + "}" : token;

        BraceExpansion.Into(balanced, expansions);

        if (!token.Contains('{', StringComparison.Ordinal) && token.Contains(',', StringComparison.Ordinal))
            BraceExpansion.Into("{" + token + "}", expansions);
        operands.AddRange(expansions.FindAll(expansion => expansion is "." || IsPathLike(expansion)));
    }
}

public static partial class ToolGuard
{
    private static GuardVerdict? Spread(string command, string? cwd)
    {
        var unquoted = command;

        foreach (var pipeline in Pipelines(command))
        {
            if (Tokenized(Unkeyworded(pipeline.Text)) is ["for", var name, "in", ..])
                unquoted = unquoted.Replace("\"$" + name + "\"", "$" + name, StringComparison.Ordinal).Replace("\"${" + name + "}\"", "${" + name + "}", StringComparison.Ordinal);
        }

        return Unrolled(unquoted, Joined) is { } spread && Blocking(Stages(spread), spread, cwd, command) is { Denied: true } denied ? denied : null;
    }

    private static bool Rebinding(string command, string name)
    {
        for (var at = command.IndexOf(name, StringComparison.Ordinal); at >= 0; at = command.IndexOf(name, at + name.Length, StringComparison.Ordinal))
        {
            if (Binds(command, at, name.Length))
                return true;
        }

        return Sources(command);
    }
}