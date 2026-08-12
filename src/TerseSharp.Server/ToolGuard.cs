using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public sealed record GuardVerdict(bool Denied, string Reason, string? Routing = null);

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

    public static GuardVerdict Inspect(string tool, JsonObject input, string? cwd = null) => tool switch
    {
        "Read" or "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => OnPath(tool, Text(input, "file_path")),
        "Glob" => OnPath(tool, Text(input, "pattern")),
        "Grep" => OnGrep(input),
        "Bash" => OnBash(Text(input, "command"), cwd),
        _ => Allowed,
    };

    public static string Render(GuardVerdict verdict)
    {
        if (!verdict.Denied)
            return "{}";

        var hook = new JsonObject
        {
            ["hookEventName"] = "PreToolUse",
            ["permissionDecision"] = "deny",
            ["permissionDecisionReason"] = verdict.Reason,
        };

        if (verdict.Routing is { Length: > 0 } routing)
            hook["additionalContext"] = "Call this instead: " + routing;

        return new JsonObject { ["hookSpecificOutput"] = hook }.ToJsonString();
    }

    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var verdict = Decide(payload);

        await output.WriteLineAsync(Render(verdict)).ConfigureAwait(false);
        await LogAsync(payload, verdict, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    private static GuardVerdict Decide(string payload)
    {
        try
        {
            var root = JsonNode.Parse(payload) as JsonObject;
            var tool = root is null ? null : Text(root, "tool_name");

            return tool is null
                ? Allowed
                : Inspect(tool, root!["tool_input"] as JsonObject ?? [], Text(root, "cwd"));
        }
        catch (JsonException)
        {
            return Allowed;
        }
    }

    private static readonly GuardVerdict Allowed = new(false, string.Empty);

    private static GuardVerdict OnPath(string tool, string? path) => path is not null && Covered(path)
        ? new GuardVerdict(true, Reason(tool, path), PathRouting(tool, path))
        : Allowed;

    private static GuardVerdict OnGrep(JsonObject input)
    {
        var scope = string.Join(' ', new[] { Text(input, "glob"), Text(input, "path"), Text(input, "type") }.OfType<string>());

        return Covered(scope) || DotNetType(Text(input, "type"))
            ? new GuardVerdict(true, Reason("Grep", scope.Trim()), GrepRouting(scope.Trim(), Text(input, "pattern")))
            : Allowed;
    }

    private static bool DotNetType(string? type) =>
        type is "cs" or "csharp" or "xaml" or "razor" or "cshtml";

    private static GuardVerdict OnBash(string? command, string? cwd)
    {
        if (command is null)
            return Allowed;

        foreach (var segment in Segments(command))
        {
            if (Replaced(segment, cwd) is { } subcommand)
                return new GuardVerdict(true, BuildReason(segment, subcommand), BuildRouting(subcommand));

            if (Covered(segment) && IsTextRead(segment))
                return new GuardVerdict(true, Reason("Bash", segment.Trim()), BashRouting(segment.Trim()));
        }

        return Allowed;
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
        "status" => "use changed_files, or changed_files root=<that directory> when it is not the loaded workspace",
        "diff" => "use diff_symbols, then diff_text only for the hunk text it cannot show; for a directory that is not loaded, diff_text root=<that directory>",
        "ls-files" => "use find_files tracked=true",
        "log" => "use history, which takes path=, baseRef=, contains= for the pickaxe and message= for the subject grep",
        "show" => "use history commit=<sha>, which answers the subject and one line per file with added and deleted counts",
        "show-file" => "use read_text ref=<ref> path=<path>, or get_file_outline ref=<ref> path=<path> for a .cs file",
        _ => "use run_tests, rerun_failed or list_tests",
    };

    private static string Rationale(string subcommand) => subcommand switch
    {
        "format" or "format-analyzers" or "format-style" or "clean" => "Shelling out rewrites or deletes files outside the compile gate and returns raw CLI output; the tool returns a diff or freed-byte counters, rolls back an edit that breaks the build, names every diagnostic no fixer covers, and answers a verify in one line instead of a per-file listing.",
        "status" => "changed_files answers the whole working tree as one line per file - path, added and deleted counts, status letter - and takes baseRef=, so the end-of-task review costs a listing instead of a diff.",
        "diff" => "A raw diff is the most expensive answer in a session; diff_symbols maps every hunk onto the declaration containing it and answers with symbol ids, and both take baseRef= and return workspace-relative paths.",
        "ls-files" => "find_files tracked=true lists the tracked files a glob selects, workspace-relative and with the build output already excluded, so telling a checked-in fixture from a scratch file needs no pipe through grep. Only the bare listing is replaced: git ls-files with any option is left alone.",
        "log" or "show" => "history answers the same commits workspace-relative and bounded, with the pickaxe and the subject grep as parameters instead of flags. Only git blame and index or history mutation stay on the shell.",
        "show-file" => "read_text ref= gives a revision's text the same numbering gutter, line ranges, tail=, section= and maxChars budget as the working tree, and a whole .cs file answers its outline instead of about three times the tokens.",
        _ => "Shelling out returns raw MSBuild or VSTest output; the tool returns deduplicated diagnostics, or per-failure messages with expected/actual and one source frame.",
    };

    private static string[] Segments(string command) =>
        command.Split(["&&", "||", "|", ";", "\n"], StringSplitOptions.RemoveEmptyEntries);

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
            "diff" when IsDotNetTree(directed) => "diff",
            "ls-files" when IsDotNetTree(directed) && Unflagged(tokens, "ls-files") => "ls-files",
            "log" when IsDotNetTree(directed) && !Shaped(tokens) => "log",
            "show" when IsDotNetTree(directed) && !Scripted(tokens) => Showing(tokens),
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

    private static bool IsDotNetTree(string? cwd)
    {
        try
        {
            return Walk(cwd is { Length: > 0 } ? cwd : Environment.CurrentDirectory);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Walk(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Marked(directory))
                return true;
        }

        return false;
    }

    private static bool Marked(DirectoryInfo directory)
    {
        try
        {
            return directory.Exists && SolutionMarkers.Any(marker => directory.EnumerateFiles(marker).Any());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string Bare(string token) => token.Trim(Wrappers);


    private static bool IsAssignment(string token) =>
        !token.StartsWith('-') && token.IndexOf('=', StringComparison.Ordinal) > 0;

    private static string[] Command(string segment)
    {
        var opened = segment.Contains("$(", StringComparison.Ordinal)
            ? segment.Replace("$(", " ", StringComparison.Ordinal)
            : segment;
        var tokens = opened.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
        "ls-files" => "find_files tracked=true",
        "log" => "history",
        "show" => "history",
        "show-file" => "read_text",
        _ => "run_tests",
    };

    internal static string Entry(string payload, GuardVerdict verdict)
    {
        var root = Parsed(payload);
        var input = root["tool_input"] as JsonObject ?? [];

        return new JsonObject
        {
            ["tool"] = Text(root, "tool_name"),
            ["denied"] = verdict.Denied,
            ["routing"] = verdict.Routing,
            ["reason"] = verdict.Denied ? verdict.Reason : null,
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
}

public readonly record struct GuardCoverage(string Detail, bool Complete);
