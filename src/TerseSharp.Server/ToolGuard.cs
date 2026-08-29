using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public sealed record GuardVerdict(bool Denied, string Reason, string? Routing = null, string? Replaces = null, string? Rewrite = null);

public static class ToolGuard
{
    private static readonly string[] Extensions =
        [".cs", ".razor", ".cshtml", ".csproj", ".props", ".targets", ".sln", ".slnx", ".slnf", ".xaml", ".axaml", ".paml", ".resx", ".resw"];

    private static readonly string[] RazorSuffixes = [".razor", ".cshtml", ".razor.css", ".razor.js"];

    private static readonly string[] TextCommands =
    [
        "grep", "rg", "cat", "head", "tail", "sed", "awk", "findstr", "type",
        "find", "fd", "ls", "dir", "tree", "wc", "nl",
        "get-childitem", "gci", "get-content", "gc", "select-string", "sls",
    ];

    public static GuardVerdict Inspect(string tool, JsonObject input, string? cwd = null, ToolOverrides? overrides = null) =>
            Respected(Routed(tool, input, cwd), overrides);

    private static GuardVerdict Routed(string tool, JsonObject input, string? cwd) => tool switch
    {
        "Read" or "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => OnPath(tool, Text(input, "file_path")),
        "Glob" => OnPath(tool, Text(input, "pattern")),
        "Grep" => OnGrep(input),
        "Bash" => OnBash(Text(input, "command"), cwd),
        _ => Allowed,
    };

    private static GuardVerdict Respected(GuardVerdict verdict, ToolOverrides? overrides) =>
            verdict.Denied && overrides is { Configured: true } configured && !Replaceable(verdict, configured)
                ? verdict with { Denied = false }
                : verdict;

    private static bool Replaceable(GuardVerdict verdict, ToolOverrides overrides)
    {
        var named = 0;

        foreach (var tool in ToolGroups.Tools)
        {
            if (!Names(verdict, tool))
                continue;

            named++;

            if (overrides.Decision(tool) is not false)
                return true;
        }

        return named is 0;
    }

    private static bool Names(GuardVerdict verdict, string tool) =>
            verdict.Replaces is { Length: > 0 } clause && Mentions(clause.AsSpan(), tool.AsSpan());

    private static bool Mentions(ReadOnlySpan<char> clause, ReadOnlySpan<char> tool)
    {
        var offset = 0;

        while (clause[offset..].IndexOf(tool, StringComparison.Ordinal) is var found and >= 0)
        {
            if (Bounded(clause, offset + found, tool.Length))
                return true;

            offset += found + tool.Length;
        }

        return false;
    }

    private static bool Bounded(ReadOnlySpan<char> clause, int start, int length) =>
        (start is 0 || !IsWord(clause[start - 1]))
            && (start + length == clause.Length || !IsWord(clause[start + length]));

    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character is '_';

    public static string Render(GuardVerdict verdict)
    {
        if (!verdict.Denied)
            return "{}";

        var hook = verdict.Rewrite is { Length: > 0 } rewrite ? Rewriting(verdict, rewrite) : Denying(verdict);

        return new JsonObject { ["hookSpecificOutput"] = hook }.ToJsonString();
    }

    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var verdict = await DecideAsync(payload, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(Render(verdict)).ConfigureAwait(false);
        await LogAsync(payload, verdict, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static async Task<GuardVerdict> DecideAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject root || Text(root, "tool_name") is not { } tool)
                return Allowed;

            var cwd = Text(root, "cwd");
            var overrides = await ToolSettings.LoadAsync(cwd ?? Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);

            return Inspect(tool, root["tool_input"] as JsonObject ?? [], cwd, overrides);
        }
        catch (JsonException)
        {
            return Allowed;
        }
    }

    private static readonly GuardVerdict Allowed = new(false, string.Empty);

    private static GuardVerdict OnPath(string tool, string? path) => path is not null && Covered(path)
            ? new GuardVerdict(true, Reason(tool, path), PathRouting(tool, path), Replacement(tool, path))
            : Allowed;

    private static GuardVerdict OnGrep(JsonObject input)
    {
        var scope = string.Join(' ', new[] { Text(input, "glob"), Text(input, "path"), Text(input, "type") }.OfType<string>());

        return Covered(scope) || DotNetType(Text(input, "type"))
            ? new GuardVerdict(true, Reason("Grep", scope.Trim()), GrepRouting(scope.Trim(), Text(input, "pattern")), Replacement("Grep", scope.Trim()))
            : Allowed;
    }

    private static bool DotNetType(string? type) =>
        type is "cs" or "csharp" or "xaml" or "razor" or "cshtml";

    private static GuardVerdict OnBash(string? command, string? cwd)
    {
        if (command is null)
            return Allowed;

        var replaced = Compound(command, cwd);

        return replaced.Denied || !Sleeping(command) ? replaced : Napping();
    }

    private static string? Replaced(string segment, string? cwd)
    {
        var tokens = Command(segment);
        var driver = Path.GetFileNameWithoutExtension(tokens.FirstOrDefault() ?? string.Empty);

        if (driver.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
            return "build";

        if (driver.Equals("git", StringComparison.OrdinalIgnoreCase))
            return Git(tokens, cwd);

        if (!driver.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        return DotNetSubcommand(tokens) switch
        {
            "build" or "msbuild" => "build",
            "test" or "vstest" => "test",
            "format" => Formatting(tokens),
            "clean" => "clean",
            "list" => Listing(tokens),
            _ => null,
        };
    }

    private static string Formatting(string[] tokens) => Scan(tokens, Array.IndexOf(tokens, "format") + 1, []) switch
    {
        "analyzers" => "format-analyzers",
        "style" => "format-style",
        _ => "format",
    };

    private static string BuildReason(string segment, string subcommand) => string.Create(
        CultureInfo.InvariantCulture,
        $"TerseSharp guard: '{Trim(segment.Trim())}' is replaced by the terse-sharp MCP - {BuildReplacement(subcommand)}. {Rationale(subcommand)}{Remember}");

    private static string BuildReplacement(string subcommand) => subcommand switch
    {
        "build" => "use build",
        "format" => "use format for whitespace and cleanup fix=all for the analyzer code fixes; format verify=true and cleanup verify=true replace --verify-no-changes",
        "format-analyzers" => "use cleanup fix=analyzers, or cleanup verify=true fix=analyzers for --verify-no-changes - that verifies exactly what this command checks",
        "format-style" => "use cleanup fix=style, or cleanup verify=true fix=style for --verify-no-changes - that verifies exactly what this command checks",
        "clean" => "use clean",
        "list-package" => "use package_list, with vulnerable=true or outdated=true for the resolved-graph answers",
        "status" => "use changed_files, with untracked=false for --untracked-files=no, or changed_files root=<that directory> when it is not the loaded workspace",
        "diff" => "use diff_symbols, then diff_text only for the hunk text it cannot show; for a directory that is not loaded, diff_text root=<that directory>",
        "diff-cached" => "use diff_symbols staged=true, then diff_text staged=true for the hunk text it cannot show - or changed_files staged=true for the --name-only and --stat answer, one line per file",
        "ls-files" => "use find_files tracked=true",
        "log" => "use history, which takes path=, baseRef=, contains= for the pickaxe and message= for the subject grep",
        "show" => "use history commit=<sha>, which answers the subject and one line per file with added and deleted counts",
        "show-file" => "use read_text ref=<ref> path=<path>, or get_file_outline ref=<ref> path=<path> for a .cs file",
        "tag" => "use history tags=true, which lists every tag newest-first with the commit it names",
        "describe" => "use history describe=true, which answers the nearest tag, the commits since it, the short sha and whether the tree is dirty, as one line",
        _ => "use run_tests, rerun_failed or list_tests",
    };

    private static string Rationale(string subcommand) => subcommand switch
    {
        "format" or "format-analyzers" or "format-style" or "clean" => "Shelling out rewrites or deletes files outside the compile gate and returns raw CLI output; the tool returns a diff or freed-byte counters, rolls back an edit that breaks the build, names every diagnostic no fixer covers, and answers a verify in one line instead of a per-file listing.",
        "list-package" => "package_list answers the declared references from the project file with no restore at all, and vulnerable=true or outdated=true runs the same resolved-graph audit through the shared child-process runner, workspace-relative and without the CLI's table framing.",
        "status" => "changed_files answers the whole working tree as one line per file - path, added and deleted counts, status letter - and takes baseRef=, staged= and untracked=, so the end-of-task review costs a listing instead of a diff.",
        "diff" => "A raw diff is the most expensive answer in a session; diff_symbols maps every hunk onto the declaration containing it and answers with symbol ids, and both take baseRef= and return workspace-relative paths.",
        "diff-cached" => "diff_symbols, diff_text and changed_files all take staged=true and read the index rather than the working tree, which is the question a pre-commit check asks - the first two answer the declarations and the hunk text, the third one bounded line per file.",
        "ls-files" => "find_files tracked=true lists the tracked files a glob selects, workspace-relative and with the build output already excluded, so telling a checked-in fixture from a scratch file needs no pipe through grep. Only the bare listing is replaced: git ls-files with any option is left alone.",
        "log" or "show" => "history answers the same commits workspace-relative and bounded, with the pickaxe and the subject grep as parameters instead of flags. Only git blame and index or history mutation stay on the shell.",
        "show-file" => "read_text ref= gives a revision's text the same numbering gutter, line ranges, tail=, section= and maxChars budget as the working tree, and a whole .cs file answers its outline instead of about three times the tokens.",
        "describe" => "history describe=true answers HEAD's position - nearest tag, commits since it, short sha, dirty flag - as one line through the same runner as every other git answer, so the release-state question needs no shell. Creating, deleting or verifying a tag stays on the shell.",
        "tag" => "history tags=true answers the tag list newest-first with the commit each names, bounded by maxResults and through the same runner as every other git answer. Only the listing is replaced: creating, deleting, signing or verifying a tag stays on the shell.",
        _ => "Shelling out returns raw MSBuild or VSTest output; the tool returns deduplicated diagnostics, or per-failure messages with expected/actual and one source frame.",
    };

    private static string[] Segments(string command)
    {
        var masked = Masked(command);
        var segments = new List<string>();
        var start = 0;
        var index = 0;

        while (index < masked.Length)
        {
            if (Operator(masked.AsSpan(index)) is var width and > 0)
            {
                segments.Add(command[start..index]);
                index += width;
                start = index;

                continue;
            }

            index++;
        }

        segments.Add(command[start..]);
        segments.RemoveAll(segment => segment.Trim().Length is 0);

        return [.. segments];
    }

    private static bool IsTextRead(string segment)
    {
        var first = Command(segment).FirstOrDefault() ?? string.Empty;

        return TextCommands.Contains(Path.GetFileNameWithoutExtension(first), StringComparer.OrdinalIgnoreCase);
    }

    private static bool Covered(string text) => Tokens(text).Any(IsDotNet);

    private static readonly char[] Separators = [' ', '\t', '"', '\'', '=', ',', '(', ')', '\n', '\r'];

    private static readonly char[] Wrappers = ['"', '\'', '`', '(', ')', '{', '}'];

    private static string[] Tokens(string text) =>
        text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsDotNet(string token)
    {
        var trimmed = token.TrimEnd('.', ':', ';');
        var extension = Path.GetExtension(trimmed);

        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || IsRazorFile(trimmed);
    }

    private static bool IsResource(string text) => Tokens(text)
        .Select(token => Path.GetExtension(token.TrimEnd('.', ':', ';')))
        .Any(extension => extension is ".resx" or ".resw");

    private static string Reason(string tool, string target) => string.Create(
        CultureInfo.InvariantCulture,
        $"TerseSharp guard: {tool} on '{Trim(target)}' is C#/.NET source. Use the terse-sharp MCP instead - {Replacement(tool, target)}. Read the tool's remedy: line rather than falling back to a built-in.{Freshness(tool, target)}");

    private static bool IsRazorFile(string token) =>
        RazorSuffixes.Any(suffix => token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsRazor(string text) => Tokens(text)
        .Select(token => token.TrimEnd('.', ':', ';'))
        .Any(IsRazorFile);

    private static string Razor(string tool) => tool switch
    {
        "Read" => "razor_outline, razor_component or razor_codebehind",
        "Grep" => "razor_find, find_usages or search_symbols",
        "Glob" => "find_files",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" =>
            "razor_set_attribute, razor_add_element, razor_remove_element, razor_set_directive or replace_symbol_body",
        _ => "the matching razor_ tool",
    };

    private static string Replacement(string tool, string target) => target switch
    {
        var razor when IsRazor(razor) => Razor(tool),
        var resource when IsResource(resource) => Resource(tool),
        var markup when IsMarkup(markup) => Markup(tool),
        _ => Code(tool, target),
    };

    private static string Resource(string tool) => tool switch
    {
        "Read" => "resx_get, resx_find or resx_validate",
        "Grep" => "resx_find, resx_usages or resx_validate",
        "Glob" or "Bash" => "resx_files, resx_find or resx_validate before find_files - the resx tools answer from one index and keep the family grouping",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => "resx_set, resx_remove or resx_rename",
        _ => "the matching resx_ tool",
    };

    private static string Code(string tool, string target) => tool switch
    {
        "Read" => "get_file_outline, get_symbol_source, xaml_outline or read_text",
        "Grep" => "search_symbols, find_usages, find_implementations, search_text or xaml_find",
        "Glob" => "find_files",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => Writer(target),
        _ => "the matching terse-sharp tool",
    };

    private static string Trim(string target) => target.Length <= 120 ? target : target[..120] + "...";

    private static string? Text(JsonObject input, string name) =>
        input[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Writer(string target)
    {
        if (!IsCSharp(target) || File.Exists(target))
            return Editors;

        return Path.IsPathRooted(target)
            ? Creator
            : Editors + ", or " + Creator + " when the file does not exist yet";
    }

    private const string Editors =
        "replace_symbol_body, replace_symbol, add_member, rename_symbol, xaml_set_property or edit_text";

    private static bool IsCSharp(string text) => Tokens(text)
        .Select(token => Path.GetExtension(token.TrimEnd('.', ':', ';')))
        .Any(extension => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase));

    private static string Freshness(string tool, string target) => IsWrite(tool) && IsCSharp(target)
        ? " A file you create or edit through write_text is picked up automatically - every semantic tool sees it on the next call, with no reload."
        : string.Empty;
    private const string Creator =
        "write_text(path, content, force=true) to create it, then add_member or replace_symbol";

    private static bool IsWrite(string tool) => tool is "Write" or "Edit" or "MultiEdit" or "NotebookEdit";
    private static bool IsMarkup(string text) => Tokens(text)
        .Select(token => token.TrimEnd('.', ':', ';'))
        .Any(token => MarkupExtensions.Contains(Path.GetExtension(token), StringComparer.OrdinalIgnoreCase)
            || MarkupTypes.Contains(token, StringComparer.OrdinalIgnoreCase));

    private static string Markup(string tool) => tool switch
    {
        "Read" => "xaml_outline, xaml_names, xaml_resources, xaml_codebehind or read_text",
        "Grep" => "xaml_find, xaml_resolve, xaml_styles, find_usages or search_text",
        "Glob" or "Bash" => "xaml_find, xaml_resolve or xaml_styles before find_files - globbing XAML is nearly always a search for a key, a name or a style",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" =>
            "xaml_set_property, xaml_add_element, xaml_remove_element or edit_text",
        _ => "the matching xaml_ tool",
    };

    private static readonly string[] MarkupExtensions = [".xaml", ".axaml", ".paml"];

    private static readonly string[] MarkupTypes = ["xaml", "axaml", "paml"];

    private static readonly string[] GitGlobals = ["-C", "-c", "--git-dir", "--work-tree", "--namespace"];

    private const string Remember = " Remember it: do not run this in Bash again - the tool answers it.";

    private static string? Subcommand(string[] tokens, int start) =>
        tokens.Skip(start).FirstOrDefault(token => !token.StartsWith('-'));

    private static string? DotNetSubcommand(string[] tokens)
    {
        var subcommand = Subcommand(tokens, 1);

        return subcommand is "watch" ? Scan(tokens, Array.IndexOf(tokens, "watch") + 1, WatchGlobals) : subcommand;
    }

    private static string? Git(string[] tokens, string? cwd)
    {
        var subcommand = Scan(tokens, 1, GitGlobals);
        var directed = Directed(tokens, cwd, subcommand);

        return subcommand switch
        {
            "status" when IsDotNetTree(directed) => "status",
            "diff" when IsDotNetTree(directed) => Cached(tokens),
            "ls-files" when IsDotNetTree(directed) && Unflagged(tokens, "ls-files") => "ls-files",
            "log" when IsDotNetTree(directed) && !Shaped(tokens) => "log",
            "show" when IsDotNetTree(directed) && !Scripted(tokens) => Showing(tokens),
            "tag" when IsDotNetTree(directed) && TagListing(tokens) => "tag",
            "describe" when IsDotNetTree(directed) && Described(tokens) => "describe",
            _ => null,
        };
    }

    private static bool Scripted(string[] tokens) => Array.Exists(
        tokens,
        token => token.StartsWith("--format", StringComparison.Ordinal)
            || token.StartsWith("--pretty", StringComparison.Ordinal)
            || token is "-s" or "--name-only" or "--name-status");

    private static bool Shaped(string[] tokens) => Scripted(tokens) || Array.Exists(
            tokens,
            token => token.StartsWith("--author", StringComparison.Ordinal)
                || token.StartsWith("-L", StringComparison.Ordinal)
                || token is "-p" or "--patch" or "--stat" or "--numstat" or "--shortstat"
                    or "--follow" or "--graph" or "--reverse");

    private static string Showing(string[] tokens) =>
            Array.Exists(tokens, token => token.Contains(':', StringComparison.Ordinal) && !token.StartsWith('-')) ? "show-file" : "show";

    private static string Directed(string[] tokens, string? cwd, string? subcommand)
    {
        var here = cwd is { Length: > 0 } ? cwd : Environment.CurrentDirectory;
        var root = Changed(tokens, here);
        var operands = subcommand is { Length: > 0 } ? Array.IndexOf(tokens, subcommand) + 1 : tokens.Length;

        return Operand(tokens, root, operands) ?? root;
    }

    private static string Changed(string[] tokens, string cwd)
    {
        var root = cwd;

        for (var index = 1; index < tokens.Length - 1; index++)
        {
            if (tokens[index] is "-C" && Under(root, tokens[index + 1]) is { } target && Directory.Exists(target))
                root = target;
        }

        return root;
    }

    private static string? Operand(string[] tokens, string root, int start)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith('-'))
                continue;

            var candidate = Under(root, tokens[index]);

            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string Under(string root, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(root, path);

    private static readonly string[] SolutionMarkers = ["*.sln", "*.slnx", "*.slnf", "*.csproj", "*.fsproj", "*.vbproj"];

    private static readonly string[] WatchGlobals =
        ["--project", "-p", "--launch-profile", "-lp", "--framework", "-f", "--configuration", "-c", "--property", "--verbosity", "-v"];

    private static string? Scan(string[] tokens, int start, string[] globals)
    {
        for (var index = start; index > 0 && index < tokens.Length; index++)
        {
            if (tokens[index] is "--")
                return null;

            if (globals.Contains(tokens[index], StringComparer.Ordinal))
                index++;
            else if (!tokens[index].StartsWith('-'))
                return tokens[index];
        }

        return null;
    }

    private static bool IsDotNetTree(string? cwd) => Marker(cwd) is not null;

    internal static string? Marker(string? cwd)
    {
        try
        {
            return Walk(cwd is { Length: > 0 } ? cwd : Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? Walk(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Marked(directory) is { } marker)
                return marker;
        }

        return null;
    }

    private static string? Marked(DirectoryInfo directory)
    {
        try
        {
            return directory.Exists
                ? SolutionMarkers.SelectMany(directory.EnumerateFiles).Select(file => file.FullName).FirstOrDefault()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string Bare(string token) => token.Trim(Wrappers);

    private static bool IsAssignment(string token) =>
        !token.StartsWith('-') && token.IndexOf('=', StringComparison.Ordinal) > 0;

    private static string[] Command(string segment)
    {
        var outer = Tokenized(Substituted(segment));

        return outer.Length > 0
            ? outer
            : Tokenized(segment.Replace("$(", " ", StringComparison.Ordinal));
    }

    private static bool Unflagged(string[] tokens, string subcommand)
    {
        var start = Array.IndexOf(tokens, subcommand);

        if (start < 0)
            return false;

        for (var index = start + 1; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith('-'))
                return false;
        }

        return true;
    }

    private static string Call(string tool, string name, string value) => string.Create(
        CultureInfo.InvariantCulture,
        $"{tool} {name}=\"{Trim(value)}\"");

    private static string OutlineTool(string target) => target switch
    {
        var razor when IsRazor(razor) => "razor_outline",
        var resource when IsResource(resource) => "resx_get",
        var markup when IsMarkup(markup) => "xaml_outline",
        var code when IsCSharp(code) => "get_file_outline",
        _ => "read_text",
    };

    private static string EditTool(string target) => target switch
    {
        var razor when IsRazor(razor) => "razor_set_attribute",
        var resource when IsResource(resource) => "resx_set",
        var markup when IsMarkup(markup) => "xaml_set_property",
        var code when IsCSharp(code) => "replace_symbol_body",
        _ => "edit_text",
    };

    private static string PathRouting(string tool, string target) => tool switch
    {
        "Read" => Call(OutlineTool(target), "path", target),
        "Glob" => Call("find_files", "glob", target),
        _ => EditRouting(target),
    };

    private static string EditRouting(string target) => IsCSharp(target) && !IsRazor(target)
        ? Call("get_file_outline", "path", target) + ", then replace_symbol_body symbolId=<a member it lists>"
        : Call(EditTool(target), "path", target);

    private static string SearchTool(string scope) => scope switch
    {
        var razor when IsRazor(razor) => "razor_find",
        var resource when IsResource(resource) => "resx_find",
        var markup when IsMarkup(markup) => "xaml_find",
        _ => "search_text",
    };

    private static string GrepRouting(string scope, string? pattern) =>
        Call(SearchTool(scope), "query", pattern is { Length: > 0 } text ? text : scope);

    private static string BashRouting(string segment)
    {
        foreach (var token in Tokens(segment))
        {
            var bare = Bare(token);

            if (Covered(bare))
                return Call(OutlineTool(bare), "path", bare);
        }

        return Call("search_text", "query", segment);
    }

    private static string BuildRouting(string subcommand) => subcommand switch
    {
        "build" => "build",
        "test" => "run_tests",
        "clean" => "clean",
        "format" => "format, then cleanup fix=all",
        "format-analyzers" => "cleanup fix=analyzers",
        "format-style" => "cleanup fix=style",
        "status" => "changed_files",
        "diff" => "diff_symbols",
        "diff-cached" => "diff_symbols staged=true",
        "ls-files" => "find_files tracked=true",
        "log" => "history",
        "show" => "history",
        "show-file" => "read_text",
        "tag" => "history tags=true",
        "describe" => "history describe=true",
        _ => "run_tests",
    };

    internal static string Entry(string payload, GuardVerdict verdict)
    {
        var root = Parsed(payload);
        var input = root["tool_input"] as JsonObject ?? [];

        return new JsonObject
        {
            ["tool"] = Text(root, "tool_name"),
            ["denied"] = verdict.Denied && verdict.Rewrite is not { Length: > 0 },
            ["rewrite"] = verdict.Rewrite,
            ["standDown"] = !verdict.Denied && verdict.Reason.Length is not 0,
            ["routing"] = verdict.Routing,
            ["reason"] = verdict.Reason is { Length: > 0 } reason ? reason : null,
            ["cwd"] = Text(root, "cwd"),
            ["session"] = Text(root, "session_id"),
            ["transcript"] = Text(root, "transcript_path"),
            ["command"] = Text(input, "command"),
            ["path"] = Text(input, "file_path") ?? Text(input, "pattern"),
        }.ToJsonString();
    }

    private static async Task LogAsync(string payload, GuardVerdict verdict, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable("TERSE_GUARD_LOG") is not { Length: > 0 } path)
            return;

        try
        {
            var line = Encoding.UTF8.GetBytes(Entry(payload, verdict) + "\n");

            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous);

            await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonObject Parsed(string payload)
    {
        try
        {
            return JsonNode.Parse(payload) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static GuardCoverage Coverage(string? cwd)
    {
        var probes = Probes(cwd);
        var detail = string.Join(' ', probes.Select(probe => probe.Name + '=' + (probe.Denied ? "denied" : "allowed")));

        return new GuardCoverage(detail, probes.All(probe => probe.Denied));
    }

    private static (string Name, bool Denied)[] Probes(string? cwd) =>
        [
            ("read-cs", Inspect("Read", Payload("file_path", Path.Combine(cwd ?? ".", "Program.cs")), cwd).Denied),
            ("bash-text", Inspect("Bash", Payload("command", "grep -rn Submit Program.cs"), cwd).Denied),
            ("dotnet-build", Inspect("Bash", Payload("command", "dotnet build"), cwd).Denied),
            ("dotnet-test", Inspect("Bash", Payload("command", "dotnet test"), cwd).Denied),
            ("git-status", Inspect("Bash", Payload("command", "git status"), cwd).Denied),
            ("git-diff", Inspect("Bash", Payload("command", "git diff"), cwd).Denied),
        ];

    private static JsonObject Payload(string name, string value) => new() { [name] = value };

    private static string? Listing(string[] tokens) =>
        Array.Exists(tokens, token => token.Equals("package", StringComparison.OrdinalIgnoreCase)) ? "list-package" : null;

    private static bool Described(string[] tokens) =>
        !Array.Exists(tokens, token => token.StartsWith("--", StringComparison.Ordinal)
            && token is not ("--tags" or "--long" or "--dirty" or "--always"))
        && Subcommand(tokens, Array.IndexOf(tokens, "describe") + 1) is null;

    private static bool TagListing(string[] tokens) =>
        Array.Exists(tokens, token => token is "--list" or "-l") || Subcommand(tokens, Array.IndexOf(tokens, "tag") + 1) is null;

    private static string Masked(string command)
    {
        if (command.AsSpan().IndexOfAny('"', '\'') < 0)
            return Duplications(command);

        var masked = command.ToCharArray();
        var quote = '\0';

        for (var index = 0; index < masked.Length; index++)
        {
            var current = masked[index];

            quote = Quote(quote, current);

            if (quote is not '\0' && current != quote)
                masked[index] = 'x';
        }

        return Duplications(new string(masked));
    }

    private static char Quote(char quote, char current) => (quote, current) switch
    {
        ('\0', '"') or ('\0', '\'') => current,
        _ when quote == current => '\0',
        _ => quote,
    };

    private static int Operator(ReadOnlySpan<char> text) => text switch
    {
        ['&', '&', ..] or ['|', '|', ..] => 2,
        ['|', ..] or [';', ..] or ['\n', ..] => 1,
        _ => 0,
    };

    private static string Nothing(bool compound, string unfenceable) => compound
        ? " This is a compound command and NO part of the command ran" + Because(unfenceable) + ". 'Call this instead' names the tool call for every denied segment, and every segment nothing replaces: chained with && when re-issuing them together is sound, listed one by one when it is not."
        : string.Empty;

    private static string Cached(string[] tokens) =>
            Array.Exists(tokens, token => token is "--cached" or "--staged") ? "diff-cached" : "diff";

    private static readonly string[] SearchCommands =
            ["grep", "rg", "sed", "awk", "findstr", "wc", "select-string", "sls"];
    private static readonly string[] ListCommands =
            ["find", "fd", "ls", "dir", "tree", "get-childitem", "gci"];

    private static string TextKind(string segment)
    {
        var command = Command(segment);
        var name = Path.GetFileNameWithoutExtension(command.FirstOrDefault() ?? string.Empty);

        return name switch
        {
            _ when Rewrites(name, command) => "Edit",
            _ when SearchCommands.Contains(name, StringComparer.OrdinalIgnoreCase) => "Grep",
            _ when ListCommands.Contains(name, StringComparer.OrdinalIgnoreCase) => "Glob",
            _ => "Read",
        };
    }

    private static bool Rewrites(string name, string[] command) =>
        name.Equals("sed", StringComparison.OrdinalIgnoreCase)
            && Array.Exists(command, token => token.StartsWith("-i", StringComparison.Ordinal) || token.Equals("--in-place", StringComparison.Ordinal));

    private static GuardVerdict Denial(string segment, string? cwd, bool compound, string unfenceable = "")
    {
        var direct = Direct(segment, cwd, compound, unfenceable);

        return direct.Denied || !segment.Contains("$(", StringComparison.Ordinal)
            ? direct
            : Direct(segment.Replace("$(", " ", StringComparison.Ordinal), cwd, compound, unfenceable);
    }

    private static string Reissue(List<string> calls, List<string> allowed, bool anded)
    {
        var made = string.Join("  then  ", calls);

        if (allowed.Count is 0)
            return made;

        return anded
            ? made + "  then re-issue the allowed remainder in Bash: " + string.Join(" && ", allowed)
            : made + "  |  not replaced, re-issue each in Bash as it stands: " + string.Join("  |  ", allowed);
    }

    private static readonly string[] SleepCommands = ["sleep", "start-sleep"];
    private static readonly string[] LoopKeywords = ["while", "until", "for", "foreach"];

    private static bool IsSleep(string token) =>
        SleepCommands.Contains(Path.GetFileNameWithoutExtension(Bare(token)), StringComparer.OrdinalIgnoreCase);

    private static bool Sleeping(string command)
    {
        var masked = Masked(command);

        return !Array.Exists(Tokens(masked), token => LoopKeywords.Contains(Bare(token), StringComparer.OrdinalIgnoreCase))
            && Array.Exists(Segments(masked), IsSleepCall);
    }

    private static GuardVerdict Napping() => new(true, SleepReason);

    private const string SleepReason = "TerseSharp guard: a bare 'sleep' is not how you wait - across one measured week it declared 25 307 seconds and cost 7.0 h of real wall clock in 156 calls, largest single 580 s. Background work notifies you: Bash(run_in_background: true) and Agent(run_in_background: true) both re-invoke you when they finish, and TaskList/TaskGet/TaskOutput answer status in one call. If you need the result to continue and have nothing else to do, END THE TURN - stopping is free, sleeping is billed. The only sleep this guard allows is the sub-second pause inside a loop that also detects the process dying: while :; do kill -0 \"$PID\" || break; sleep 1; done";
    private const string Priced = " Measured over one week, the shell text tools cost 2 369 Bash calls and 18.1 h - 51.5% of all Bash wall time - at a 13.2% error rate; the terse-sharp answer is one call.";

    private static GuardVerdict Compound(string command, string? cwd)
    {
        var segments = Segments(command);

        return segments.Length > 1 && Fenced(command) && Splitting(command, cwd) is { } stripped
            ? stripped
            : Blocking(segments, command, cwd);
    }

    private static bool IsSleepCall(string segment) =>
        IsSleep(Command(segment).FirstOrDefault() ?? string.Empty);

    private static bool OnlyAnded(string command) =>
        Masked(command).Replace("&&", "  ", StringComparison.Ordinal).AsSpan().IndexOfAny("&|;\n") < 0;

    private readonly record struct Pipeline(string Lead, string Text);

    private readonly record struct Judgement(Pipeline Pipeline, GuardVerdict Verdict);

    private static readonly SearchValues<char> Hazards = SearchValues.Create("(){}`<>$#");
    private static readonly string[] ShellKeywords =
        [
            "for", "foreach", "while", "until", "do", "done", "if", "then",
        "elif", "else", "fi", "case", "esac", "select", "function",
    ];

    private static bool Fenced(string command) => Unfenceable(command).Length is 0;

    private static bool Backgrounded(ReadOnlySpan<char> masked)
    {
        for (var index = masked.IndexOf('&'); index >= 0; index = Ampersand(masked, index))
        {
            if (!masked[index..].StartsWith("&&", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int Ampersand(ReadOnlySpan<char> masked, int index)
    {
        var next = masked[(index + 2)..].IndexOf('&');

        return next < 0 ? -1 : index + 2 + next;
    }

    private static bool Keyworded(string masked) =>
            Array.Exists(Tokens(masked), token => ShellKeywords.Contains(Bare(token), StringComparer.OrdinalIgnoreCase));

    private static int Divider(ReadOnlySpan<char> text) => text switch
    {
        ['&', '&', ..] => 2,
        [';', ..] or ['\n', ..] => 1,
        _ => 0,
    };

    private static (int Index, int Width) NextDivider(ReadOnlySpan<char> masked, int start)
    {
        for (var index = start; index < masked.Length; index++)
        {
            if (Divider(masked[index..]) is var width and > 0)
                return (index, width);
        }

        return (masked.Length, 0);
    }

    private static List<Pipeline> Pipelines(string command)
    {
        var masked = Masked(command).AsSpan();
        var pipelines = new List<Pipeline>();
        var lead = string.Empty;
        var start = 0;

        while (NextDivider(masked, start) is var (index, width) && width > 0)
        {
            pipelines.Add(new Pipeline(lead, command[start..index]));
            lead = command.Substring(index, width);
            start = index + width;
        }

        pipelines.Add(new Pipeline(lead, command[start..]));

        return pipelines;
    }

    private static GuardVerdict Judged(string pipeline, string? cwd)
    {
        foreach (var segment in Segments(pipeline))
        {
            var verdict = Denial(segment, cwd, false);

            if (verdict.Denied)
                return verdict;
        }

        return Allowed;
    }

    private static string Rejoined(List<Pipeline> kept)
    {
        var capacity = 0;

        foreach (var pipeline in kept)
            capacity += pipeline.Lead.Length + pipeline.Text.Length;

        var builder = new StringBuilder(capacity);

        foreach (var pipeline in kept)
            builder.Append(builder.Length is 0 ? string.Empty : pipeline.Lead).Append(pipeline.Text);

        return builder.ToString().Trim();
    }

    private static string Stripped(List<Judgement> dropped, int total)
    {
        var names = string.Join(" and ", dropped.ConvertAll(entry => "'" + entry.Pipeline.Text.Trim() + "'"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"TerseSharp guard: this is a batch, so the rest of it RAN - only {dropped.Count} of its {total} parts were stripped out, {names}, because the terse-sharp MCP answers them. Exactly what is quoted there produced no output, a whole pipeline at a time; everything else ran, and what followed the stripped part is no longer gated on it. Do not re-issue the quoted commands in Bash.");
    }

    private static GuardVerdict? Splitting(string command, string? cwd)
    {
        var pipelines = Pipelines(command);

        if (!Uniform(pipelines))
            return null;

        var judged = pipelines
            .FindAll(pipeline => pipeline.Text.Trim().Length > 0)
            .ConvertAll(pipeline => new Judgement(pipeline, Judged(pipeline.Text, cwd)));
        var kept = judged.FindAll(entry => !entry.Verdict.Denied);
        var dropped = judged.FindAll(entry => entry.Verdict.Denied);

        if (dropped.Count is 0 || kept.Count is 0)
            return null;

        return dropped[0].Verdict with
        {
            Reason = Stripped(dropped, judged.Count),
            Routing = Routes(dropped),
            Rewrite = Rejoined(kept.ConvertAll(entry => entry.Pipeline)),
        };
    }

    private static GuardVerdict Blocking(string[] segments, string command, string? cwd)
    {
        var compound = segments.Length > 1;
        var unfenceable = compound ? Unfenceable(command) : string.Empty;
        var allowed = new List<string>(segments.Length);
        var calls = new List<string>();
        GuardVerdict? refused = null;

        foreach (var segment in segments)
        {
            var verdict = Denial(segment, cwd, compound, unfenceable);

            if (!verdict.Denied)
            {
                allowed.Add(segment.Trim());

                continue;
            }

            refused ??= verdict;

            if (verdict.Routing is { Length: > 0 } routing && !calls.Contains(routing, StringComparer.Ordinal))
                calls.Add(routing);
        }

        return refused is null ? Allowed : refused with { Routing = Reissue(calls, allowed, OnlyAnded(command)) };
    }

    private static JsonObject Denying(GuardVerdict verdict)
    {
        var hook = new JsonObject
        {
            ["hookEventName"] = "PreToolUse",
            ["permissionDecision"] = "deny",
            ["permissionDecisionReason"] = verdict.Reason,
        };

        if (verdict.Routing is { Length: > 0 } routing)
            hook["additionalContext"] = "Call this instead: " + routing;

        return hook;
    }

    private static JsonObject Rewriting(GuardVerdict verdict, string rewrite) => new()
    {
        ["hookEventName"] = "PreToolUse",
        ["updatedInput"] = new JsonObject { ["command"] = rewrite },
        ["additionalContext"] = verdict.Reason + Calling(verdict.Routing),
    };

    private static string Calling(string? routing) =>
            routing is { Length: > 0 } ? " Call this instead: " + routing : string.Empty;

    private static bool Escaping(ReadOnlySpan<char> command)
    {
        var index = command.IndexOf('\\');

        while (index >= 0)
        {
            if (index + 1 == command.Length || Escapable.Contains(command[index + 1]))
                return true;

            var next = command[(index + 2)..].IndexOf('\\');

            index = next < 0 ? -1 : index + 2 + next;
        }

        return false;
    }

    private static readonly SearchValues<char> Escapable = SearchValues.Create("\"'`\\;&|<>$(){}# \t\r\n");

    private static bool Uniform(List<Pipeline> pipelines) =>
            pipelines.Select(pipeline => pipeline.Lead)
                .Where(lead => lead.Length is not 0)
                .Distinct(StringComparer.Ordinal)
                .Count() is <= 1;

    private static string Routes(List<Judgement> dropped) =>
            string.Join("  then  ", dropped.Select(entry => entry.Verdict.Routing).OfType<string>().Distinct(StringComparer.Ordinal));

    private static string Around(string command, int at, string what)
    {
        var start = Math.Max(0, at - 16);
        var end = Math.Min(command.Length, at + 16);

        return string.Create(CultureInfo.InvariantCulture, $"{what} at offset {at}, in \"{command.AsSpan(start, end - start)}\"");
    }

    private static string Unfenceable(string command)
    {
        var masked = Masked(command);

        if (masked.AsSpan().IndexOfAny(Hazards) is var hazard and >= 0)
            return Around(command, hazard, "'" + masked[hazard] + "'");

        if (masked.IndexOf("||", StringComparison.Ordinal) is var either and >= 0)
            return Around(command, either, "'||'");

        if (Escaping(command.AsSpan()))
            return "a backslash escape";

        if (Backgrounded(masked.AsSpan()))
            return "a background '&'";

        return Keyworded(masked) ? "a shell keyword" : string.Empty;
    }

    private static string Because(string unfenceable) => unfenceable.Length is 0
        ? string.Empty
        : ", because it carries " + unfenceable + ", which cannot be rewritten soundly - re-issue that ONE segment on its own instead of re-deriving the whole command";

    private static readonly SearchValues<char> PathMarks = SearchValues.Create("/\\");
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    private static string BuildRouting(string subcommand, string segment) => BuildRouting(subcommand) + GitArguments(subcommand, segment);

    private static string GitArguments(string subcommand, string segment) => subcommand switch
    {
        "log" => LogArguments(segment),
        "show" => CommitArgument(segment),
        "status" or "diff" or "diff-cached" => DiffArguments(segment),
        _ => string.Empty,
    };

    private static string LogArguments(string segment)
    {
        var count = 0;
        string? path = null;
        string? contains = null;
        string? message = null;
        var previous = string.Empty;
        var separated = false;

        foreach (var token in Tokens(segment))
        {
            count = Counted(token, previous, count);
            path ??= PathOperand(token, separated);
            contains ??= Contained(token, previous);
            message ??= Messaged(token, previous);
            separated |= token is "--";
            previous = token;
        }

        return Appended("maxResults", count > 0 ? count.ToString(CultureInfo.InvariantCulture) : null)
            + Appended("path", path)
            + Appended("contains", contains)
            + Appended("message", message);
    }

    private static string DiffArguments(string segment)
    {
        string? path = null;
        string? baseRef = null;
        var separated = false;

        foreach (var token in Tokens(segment))
        {
            path ??= PathOperand(token, separated);
            baseRef ??= RefOperand(token);
            separated |= token is "--";
        }

        return Appended("baseRef", baseRef) + Appended("path", path);
    }

    private static string CommitArgument(string segment)
    {
        foreach (var token in Tokens(segment))
        {
            if (IsSha(token))
                return " commit=" + token;
        }

        return string.Empty;
    }

    private static string Appended(string name, string? value) =>
        value is { Length: > 0 } ? string.Create(CultureInfo.InvariantCulture, $" {name}={value}") : string.Empty;

    private static int Counted(string token, string previous, int current)
    {
        if (current > 0)
            return current;

        if (previous is "--max-count" or "-n")
            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var after) ? after : current;

        var digits = token.StartsWith('-') && token.Length > 1 ? token.AsSpan(1) : default;

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : current;
    }

    private static string? PathOperand(string token, bool separated) =>
            !token.StartsWith('-') && !token.Contains("..", StringComparison.Ordinal) && (separated || HasExtension(token.AsSpan()))
                ? token
                : null;

    private static string? RefOperand(string token) =>
        token.Contains("..", StringComparison.Ordinal) || token.StartsWith("HEAD", StringComparison.Ordinal) ? token : null;

    private static string? Prefixed(string token, string prefix) =>
        token.StartsWith(prefix, StringComparison.Ordinal) && token.Length > prefix.Length ? token[prefix.Length..] : null;

    private static bool IsSha(string token) =>
        token.Length is >= 7 and <= 40 && !token.AsSpan().ContainsAnyExcept(HexDigits);

    private static string[] Tokenized(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = 0;

        while (start < tokens.Length && (Bare(tokens[start]).Length is 0 || IsAssignment(Bare(tokens[start]))))
            start++;

        if (start >= tokens.Length)
            return [];

        var command = new string[tokens.Length - start];

        for (var index = 0; index < command.Length; index++)
            command[index] = Bare(tokens[start + index]);

        return command;
    }

    private static string Substituted(string segment)
    {
        if (!segment.Contains("$(", StringComparison.Ordinal))
            return segment;

        var masked = segment.ToCharArray();
        var depth = 0;

        for (var index = 0; index < masked.Length; index++)
            depth = Masking(masked, index, depth);

        return new string(masked);
    }

    private static int Masking(char[] masked, int index, int depth)
    {
        if (depth is 0)
            return Opens(masked, index) ? Started(masked, index) : 0;

        var next = depth + (masked[index] is '(' ? 1 : masked[index] is ')' ? -1 : 0);

        masked[index] = ' ';

        return next;
    }

    private static bool Opens(char[] masked, int index) =>
        masked[index] is '$' && index + 1 < masked.Length && masked[index + 1] is '(';

    private static int Started(char[] masked, int index)
    {
        masked[index] = ' ';
        masked[index + 1] = ' ';

        return 1;
    }

    private static string Duplications(string command)
    {
        if (!command.Contains(">&", StringComparison.Ordinal))
            return command;

        var masked = command.ToCharArray();

        for (var index = 0; index + 2 < masked.Length; index++)
            MaskDuplication(masked, index);

        return new string(masked);
    }

    private static void MaskDuplication(char[] masked, int index)
    {
        if (masked[index] is not '>' || masked[index + 1] is not '&' || !char.IsAsciiDigit(masked[index + 2]))
            return;

        masked[index] = 'x';
        masked[index + 1] = 'x';
        masked[index + 2] = 'x';

        if (index > 0 && char.IsAsciiDigit(masked[index - 1]))
            masked[index - 1] = 'x';
    }

    private static string? Contained(string token, string previous) =>
            previous is "-S" ? token : Prefixed(token, "-S");

    private static string? Messaged(string token, string previous) =>
            previous is "--grep" ? token : Prefixed(token, "--grep=");

    private static bool Operanded(string segment)
    {
        var command = Command(segment);
        var name = command.Length > 0 ? Path.GetFileNameWithoutExtension(command[0]) : string.Empty;
        var listing = ListCommands.Contains(name, StringComparer.OrdinalIgnoreCase);
        var patterns = PatternCommands.Contains(name, StringComparer.OrdinalIgnoreCase) ? 1 : 0;

        for (var index = 1; index < command.Length; index++)
        {
            if (command[index].StartsWith('-') || IsCount(command[index]) || patterns-- > 0)
                continue;

            if (listing || IsPathLike(command[index]))
                return true;
        }

        return false;
    }

    private static bool IsCount(string token) => token.Length > 0 && !token.AsSpan().ContainsAnyExcept(Digits);

    private static readonly SearchValues<char> Digits = SearchValues.Create("0123456789");

    private static bool IsPathLike(string token) =>
            token.AsSpan().ContainsAny(PathMarks) || HasExtension(token.AsSpan());

    private static bool HasExtension(ReadOnlySpan<char> token)
    {
        var dot = token.LastIndexOf('.');

        if (dot <= 0 || dot == token.Length - 1)
            return false;

        var suffix = token[(dot + 1)..];

        return suffix.Length <= 4 && !suffix.ContainsAnyExcept(Letters);
    }

    private static readonly SearchValues<char> Letters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    private static GuardVerdict Direct(string segment, string? cwd, bool compound, string unfenceable)
    {
        if (Replaced(segment, cwd) is { } subcommand)
            return new GuardVerdict(true, BuildReason(segment, subcommand) + Nothing(compound, unfenceable), BuildRouting(subcommand, segment), BuildReplacement(subcommand));

        return (Covered(segment) || (IsDotNetTree(cwd) && Operanded(segment))) && IsTextRead(segment)
            ? new GuardVerdict(true, Reason("Bash", segment.Trim()) + Priced + Nothing(compound, unfenceable), BashRouting(segment.Trim()), Replacement(TextKind(segment), segment.Trim()))
            : Allowed;
    }

    private static readonly string[] PatternCommands = ["grep", "rg", "egrep", "fgrep", "findstr", "select-string", "sls", "awk", "sed"];
}

public readonly record struct GuardCoverage(string Detail, bool Complete);
