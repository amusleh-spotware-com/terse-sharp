using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public sealed record GuardVerdict(bool Denied, string Reason);

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

    public static GuardVerdict Inspect(string tool, JsonObject input) => tool switch
    {
        "Read" or "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => OnPath(tool, Text(input, "file_path")),
        "Glob" => OnPath(tool, Text(input, "pattern")),
        "Grep" => OnGrep(input),
        "Bash" => OnBash(Text(input, "command")),
        _ => Allowed,
    };

    public static string Render(GuardVerdict verdict) => verdict.Denied
        ? new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = "deny",
                ["permissionDecisionReason"] = verdict.Reason,
            },
        }.ToJsonString()
        : "{}";

    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var verdict = Decide(payload);

        await output.WriteLineAsync(Render(verdict)).ConfigureAwait(false);

        return 0;
    }

    private static GuardVerdict Decide(string payload)
    {
        try
        {
            var root = JsonNode.Parse(payload) as JsonObject;
            var tool = root is null ? null : Text(root, "tool_name");

            return tool is null ? Allowed : Inspect(tool, root!["tool_input"] as JsonObject ?? []);
        }
        catch (JsonException)
        {
            return Allowed;
        }
    }

    private static readonly GuardVerdict Allowed = new(false, string.Empty);

    private static GuardVerdict OnPath(string tool, string? path) =>
        path is not null && Covered(path) ? new GuardVerdict(true, Reason(tool, path)) : Allowed;

    private static GuardVerdict OnGrep(JsonObject input)
    {
        var scope = string.Join(' ', new[] { Text(input, "glob"), Text(input, "path"), Text(input, "type") }.OfType<string>());

        return Covered(scope) || DotNetType(Text(input, "type"))
            ? new GuardVerdict(true, Reason("Grep", scope.Trim()))
            : Allowed;
    }

    private static bool DotNetType(string? type) =>
        type is "cs" or "csharp" or "xaml" or "razor" or "cshtml";

    private static GuardVerdict OnBash(string? command)
    {
        if (command is null)
            return Allowed;

        foreach (var segment in Segments(command))
        {
            if (Replaced(segment) is { } subcommand)
                return new GuardVerdict(true, BuildReason(segment, subcommand));

            if (Covered(segment) && IsTextRead(segment))
                return new GuardVerdict(true, Reason("Bash", segment.Trim()));
        }

        return Allowed;
    }

    private static string? Replaced(string segment)
    {
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var driver = Path.GetFileNameWithoutExtension(tokens.FirstOrDefault() ?? string.Empty);

        if (driver.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
            return "build";

        if (!driver.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        var subcommand = tokens.Skip(1).FirstOrDefault(token => !token.StartsWith('-'));

        return subcommand switch
        {
            "build" or "msbuild" => "build",
            "test" or "vstest" => "test",
            "format" => "format",
            "clean" => "clean",
            _ => null,
        };
    }

    private static string BuildReason(string segment, string subcommand) => string.Create(
        CultureInfo.InvariantCulture,
        $"TerseSharp guard: '{Trim(segment.Trim())}' is replaced by the terse-sharp MCP - {BuildReplacement(subcommand)}. {Rationale(subcommand)}");

    private static string BuildReplacement(string subcommand) => subcommand switch
    {
        "build" => "use build",
        "format" => "use format, cleanup fix=all, or cleanup verify=true for --verify-no-changes",
        "clean" => "use clean",
        _ => "use run_tests, rerun_failed or list_tests",
    };

    private static string Rationale(string subcommand) => subcommand switch
    {
        "format" or "clean" => "Shelling out rewrites or deletes files outside the compile gate and returns raw CLI output; the tool returns a diff or freed-byte counters, rolls back an edit that breaks the build, and names every diagnostic no fixer covers.",
        _ => "Shelling out returns raw MSBuild or VSTest output; the tool returns deduplicated diagnostics, or per-failure messages with expected/actual and one source frame.",
    };

    private static string[] Segments(string command) =>
        command.Split(["&&", "||", "|", ";", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsTextRead(string segment)
    {
        var first = segment.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return TextCommands.Contains(Path.GetFileNameWithoutExtension(first), StringComparer.OrdinalIgnoreCase);
    }

    private static bool Covered(string text) => Tokens(text).Any(IsDotNet);

    private static readonly char[] Separators = [' ', '\t', '"', '\'', '=', ',', '(', ')', '\n', '\r'];

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
}
