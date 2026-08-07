using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class GitTools(ToolContext context)
{
    private const int MaxDiffLines = 400;

    [McpServerTool(Name = "changed_files")]
    [Description("Replaces Bash git status and git diff --stat. One line per changed file - path, added and deleted line counts, and the status letter - so the end-of-task review costs a listing instead of a diff. Empty baseRef compares the working tree against HEAD and includes untracked files.")]
    public Task<string> ChangedFiles(
        [Description("Commit, branch or range to compare against, e.g. main or HEAD~3. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            null,
            loaded => ListAsync(loaded, baseRef, NavigationTools.Cap(maxResults, 200), cancellationToken),
            semantic: false,
            cancellationToken);

    [McpServerTool(Name = "diff_symbols")]
    [Description("Replaces Bash git diff. Maps every changed hunk onto the declaration that contains it and answers with symbol ids you can feed straight to get_symbol_source - EXACT when a hunk sits inside one declaration, HEURISTIC with the raw line range when it does not. Use this to decide what to review, then read only the bodies you need.")]
    public Task<string> DiffSymbols(
        [Description("Commit, branch or range to compare against. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Limit to one path or pathspec.")] string? path = null,
        [Description("Max results (200).")] int maxResults = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            loaded => SymbolsAsync(loaded, baseRef, path, NavigationTools.Cap(maxResults, 200), cancellationToken),
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "diff_text")]
    [Description("The raw unified diff, bounded and workspace-relative. Use only after diff_symbols has told you which file you need - the hunk text is the most expensive answer in this server.")]
    public Task<string> DiffText(
        [Description("Commit, branch or range to compare against. Empty compares the working tree against HEAD.")] string? baseRef = null,
        [Description("Limit to one path or pathspec. Strongly recommended.")] string? path = null,
        [Description("Max diff lines returned (400).")] int maxLines = 0,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.WithWorkspaceAsync(
            workspace,
            path,
            loaded => TextAsync(loaded, baseRef, path, NavigationTools.Cap(maxLines, MaxDiffLines), cancellationToken),
            semantic: false,
            cancellationToken);

    private static async Task<string> ListAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var numstat = await GitRunner.ReadAsync(workspace.Root, Arguments(["diff", "--numstat"], baseRef, null), cancellationToken).ConfigureAwait(false);

        if (!numstat.IsOk)
            return numstat.Error!.Render();

        var status = await GitRunner.ReadAsync(workspace.Root, Arguments(["diff", "--name-status"], baseRef, null), cancellationToken).ConfigureAwait(false);

        if (!status.IsOk)
            return status.Error!.Render();

        var untracked = baseRef is { Length: > 0 }
            ? Result.Ok(string.Empty)
            : await GitRunner.ReadAsync(
                workspace.Root,
                ["--no-optional-locks", "ls-files", "--others", "--exclude-standard", "--", "."],
                cancellationToken).ConfigureAwait(false);

        return untracked.IsOk
            ? Render(numstat.Value!, status.Value!, untracked.Value!, maxResults)
            : untracked.Error!.Render();
    }

    private static string Render(string numstat, string nameStatus, string untracked, int maxResults)
    {
        var statuses = DiffParser.NameStatus(nameStatus);
        var files = DiffParser.NumStat(numstat);
        var lines = new List<string>(files.Count + 8);

        foreach (var file in files)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{file.Path}  +{Counted(file.Added)} -{Counted(file.Deleted)}  {statuses.GetValueOrDefault(file.Path, "M")}"));
        }

        foreach (var line in untracked.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace())
                lines.Add(new string(line.Trim()) + "  +? -?  ?");
        }

        var response = new ResponseBuilder("changed_files", string.Empty);

        response.Summary(ResultCap.Shown(lines.Count, maxResults), lines.Count, "files", "baseRef= or maxResults=");

        foreach (var line in lines.Capped(maxResults))
            response.Line(line);

        return response.ToString();
    }

    private static string Counted(int value) => value < 0 ? "?" : value.ToString(CultureInfo.InvariantCulture);

    private static async Task<string> SymbolsAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? path,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var diff = await GitRunner.ReadAsync(
            workspace.Root,
            Arguments(["diff", "--unified=0", "--no-color"], baseRef, path),
            cancellationToken).ConfigureAwait(false);

        return diff.IsOk
            ? await DiffSymbolService.MapAsync(workspace, diff.Value!, maxResults, cancellationToken).ConfigureAwait(false)
            : diff.Error!.Render();
    }

    private static async Task<string> TextAsync(
        LoadedWorkspace workspace,
        string? baseRef,
        string? path,
        int maxLines,
        CancellationToken cancellationToken)
    {
        var diff = await GitRunner.ReadAsync(
            workspace.Root,
            Arguments(["diff", "--no-color"], baseRef, path),
            cancellationToken).ConfigureAwait(false);

        if (!diff.IsOk)
            return diff.Error!.Render();

        var lines = new List<string>(maxLines);
        var total = 0;

        foreach (var line in diff.Value!.AsSpan().EnumerateLines())
        {
            total++;

            if (lines.Count < maxLines)
                lines.Add(new string(line));
        }

        var response = new ResponseBuilder("diff_text", path ?? string.Empty);

        response.Summary(lines.Count, total, "lines", "path= or maxLines=");

        foreach (var line in lines)
            response.Line(line);

        return response.ToString();
    }

    private static string[] Arguments(IReadOnlyList<string> command, string? baseRef, string? path)
    {
        var arguments = new List<string>(command.Count + 7) { "--no-optional-locks" };

        arguments.AddRange(command);
        arguments.AddRange(["--no-renames", "--no-ext-diff", "--relative"]);
        arguments.Add(baseRef is { Length: > 0 } reference ? reference : "HEAD");
        arguments.Add("--");
        arguments.Add(path is { Length: > 0 } pathspec ? pathspec : ".");

        return [.. arguments];
    }
}
