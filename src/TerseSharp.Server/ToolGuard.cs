using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public sealed record GuardVerdict(bool Denied, string Reason);

public static class ToolGuard
{
    private static readonly string[] Extensions =
        [".cs", ".razor", ".csproj", ".props", ".targets", ".sln", ".slnx", ".slnf", ".xaml", ".axaml", ".paml", ".resx", ".resw"];

    private static readonly string[] TextCommands =
        ["grep", "rg", "cat", "head", "tail", "sed", "awk", "findstr", "type"];

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
        type is "cs" or "csharp" or "xaml" or "razor";

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
            _ => null,
        };
    }

    private static string BuildReason(string segment, string subcommand) => string.Create(
        CultureInfo.InvariantCulture,
        $"TerseSharp guard: '{Trim(segment.Trim())}' is replaced by the terse-sharp MCP - {BuildReplacement(subcommand)}. Shelling out returns raw MSBuild or VSTest output; the tool returns deduplicated diagnostics, or per-failure messages with expected/actual and one source frame.");

    private static string BuildReplacement(string subcommand) => subcommand switch
    {
        "build" => "use build",
        _ => "use run_tests, rerun_failed or list_tests",
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

        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsResource(string text) => Tokens(text)
        .Select(token => Path.GetExtension(token.TrimEnd('.', ':', ';')))
        .Any(extension => extension is ".resx" or ".resw");

    private static string Reason(string tool, string target) => string.Create(
        CultureInfo.InvariantCulture,
        $"TerseSharp guard: {tool} on '{Trim(target)}' is C#/.NET source. Use the terse-sharp MCP instead - {Replacement(tool, target)}. Read the tool's remedy: line rather than falling back to a built-in.");

    private static string Replacement(string tool, string target) => IsResource(target)
        ? Resource(tool)
        : Code(tool);

    private static string Resource(string tool) => tool switch
    {
        "Read" => "resx_get, resx_find or resx_validate",
        "Grep" => "resx_find or resx_usages",
        "Glob" => "resx_files or find_files",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => "resx_set, resx_remove or resx_rename",
        _ => "the matching resx_ tool",
    };

    private static string Code(string tool) => tool switch
    {
        "Read" => "get_file_outline, get_symbol_source, xaml_outline or read_text",
        "Grep" => "search_symbols, find_usages, find_implementations, search_text or xaml_find",
        "Glob" => "find_files",
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" =>
            "replace_symbol_body, replace_symbol, add_member, rename_symbol, xaml_set_property or edit_text",
        _ => "the matching terse-sharp tool",
    };

    private static string Trim(string target) => target.Length <= 120 ? target : target[..120] + "...";

    private static string? Text(JsonObject input, string name) =>
        input[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
