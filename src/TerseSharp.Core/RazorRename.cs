using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class RazorRename
{
    public static async Task<Result<string>?> TryAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        string newName,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        if (symbol is not INamedTypeSymbol type)
            return null;

        var path = await RazorGeneratedMap.PathOfAsync(workspace, type, cancellationToken).ConfigureAwait(false);

        if (path is null || !workspace.Contains(path))
            return null;

        var moves = Moves(path, newName).ToArray();

        if (moves.FirstOrDefault(Occupied) is { } clash)
        {
            return Result.Fail<string>(Errors.EditConflict(string.Create(
                CultureInfo.InvariantCulture,
                $"'{PositionFormat.Relative(workspace.Root, clash.To)}' already exists")));
        }

        var rewrites = await RewritesAsync(workspace, type, newName, moves, cancellationToken).ConfigureAwait(false);

        return Result.Ok(options.DryRun
            ? Render(workspace, "dryRun", moves, rewrites)
            : Apply(workspace, moves, rewrites));
    }

    private static bool Occupied(Move move) =>
        File.Exists(move.To) && !PathBoundary.SameFile(move.From, move.To);

    private static IEnumerable<Move> Moves(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var extension = Path.GetExtension(path);

        yield return new Move(path, Path.Combine(directory, newName + extension));

        foreach (var suffix in new[] { ".cs", ".css", ".js" })
        {
            if (File.Exists(path + suffix))
                yield return new Move(path + suffix, Path.Combine(directory, newName + extension + suffix));
        }
    }

    private static async Task<IReadOnlyList<Rewrite>> RewritesAsync(
        LoadedWorkspace workspace,
        INamedTypeSymbol type,
        string newName,
        IReadOnlyList<Move> moves,
        CancellationToken cancellationToken)
    {
        var usages = await RazorUsageService.MarkupAsync(workspace, type, cancellationToken).ConfigureAwait(false);
        var files = usages
            .Where(usage => usage.Confidence is Confidence.Exact)
            .Select(usage => Path.Combine(workspace.Root, usage.Path))
            .Concat(moves.Where(move => move.From.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Select(move => move.From))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return [.. files.Select(file => Rewritten(file, type.Name, newName)).OfType<Rewrite>()];
    }

    private static Rewrite? Rewritten(string file, string oldName, string newName)
    {
        var text = Read(file);

        if (text is null)
            return null;

        var updated = text
            .Replace("<" + oldName + " ", "<" + newName + " ", StringComparison.Ordinal)
            .Replace("<" + oldName + ">", "<" + newName + ">", StringComparison.Ordinal)
            .Replace("<" + oldName + "/", "<" + newName + "/", StringComparison.Ordinal)
            .Replace("</" + oldName + ">", "</" + newName + ">", StringComparison.Ordinal)
            .Replace("class " + oldName + "\r\n", "class " + newName + "\r\n", StringComparison.Ordinal)
            .Replace("class " + oldName + "\n", "class " + newName + "\n", StringComparison.Ordinal)
            .Replace("class " + oldName + " ", "class " + newName + " ", StringComparison.Ordinal)
            .Replace("class " + oldName + "<", "class " + newName + "<", StringComparison.Ordinal)
            .Replace("class " + oldName + ":", "class " + newName + ":", StringComparison.Ordinal);

        return string.Equals(text, updated, StringComparison.Ordinal) ? null : new Rewrite(file, text, updated);
    }

    private static string? Read(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Apply(LoadedWorkspace workspace, IReadOnlyList<Move> moves, IReadOnlyList<Rewrite> rewrites)
    {
        var done = new List<Move>(moves.Count);

        return Apply(workspace, moves, rewrites, done);
    }

    private static string Apply(
        LoadedWorkspace workspace,
        IReadOnlyList<Move> moves,
        IReadOnlyList<Rewrite> rewrites,
        List<Move> done)
    {
        foreach (var rewrite in rewrites)
        {
            AtomicWrite.Text(rewrite.Path, rewrite.After);
            RazorIndex.Invalidate(rewrite.Path);
        }

        foreach (var move in moves)
        {
            Relocate(move, done);
            RazorIndex.Invalidate(move.From);
        }

        return Render(workspace, "applied", moves, rewrites);
    }

    private static void Relocate(Move move, List<Move> done)
    {
        try
        {
            File.Move(move.From, move.To);
            done.Add(move);
        }
        catch (IOException)
        {
            Undo(done);

            throw;
        }
    }

    private static void Undo(List<Move> done)
    {
        foreach (var move in done.AsEnumerable().Reverse().Where(move => File.Exists(move.To)))
            File.Move(move.To, move.From, overwrite: true);
    }

    private static string Render(
        LoadedWorkspace workspace,
        string state,
        IReadOnlyList<Move> moves,
        IReadOnlyList<Rewrite> rewrites)
    {
        var response = new ResponseBuilder("rename_symbol", state);

        response.Summary(moves.Count + rewrites.Count, moves.Count + rewrites.Count, "files changed");
        response.Note("razor component: the class name follows the file name, so the file is renamed with it");
        response.Note("workspace=stale - call load_workspace again before asking for its symbols");

        foreach (var move in moves)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"moved {PositionFormat.Relative(workspace.Root, move.From)} -> {PositionFormat.Relative(workspace.Root, move.To)}"));
        }

        foreach (var rewrite in rewrites)
        {
            var relative = PositionFormat.Relative(workspace.Root, rewrite.Path);

            response.Line(UnifiedDiff.Between(relative, rewrite.Before, rewrite.After));
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"changedLines={UnifiedDiff.ChangedLines(rewrite.Before, rewrite.After)}"));
        }

        return response.ToString();
    }

    private sealed record Move(string From, string To);

    private sealed record Rewrite(string Path, string Before, string After);
}
