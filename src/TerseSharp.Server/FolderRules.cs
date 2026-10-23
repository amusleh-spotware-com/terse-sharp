using System.Collections.Frozen;
using System.Text;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class FolderRules
{
    internal const int ContextLimit = 10_000;

    internal const int ToolResultLimit = 45_000;

    private const int SectionFrame = 11;

    private const string ReadTool = "read_text";

    private const string Preface =
        "Folder rules from nested CLAUDE.md files. Claude Code loads these only for its own Read, Edit and Write, so terse injects them for the files this call touched. They bind every read, edit and test below their folder:";

    private const string Refusal =
        "This write lands below folder rules this session has not seen - Claude Code loads nested CLAUDE.md files only for its own Read, Edit and Write. Make the write comply with them, then send the same call again";

    private static readonly Lazy<FrozenSet<string>> WriterSet = new(FolderRuleHook.WritingTools);

    private static readonly Reply Injection = new(Preface, rules => new JsonObject
    {
        ["hookEventName"] = "PostToolUse",
        ["additionalContext"] = rules,
    });

    private static readonly Reply Denial = new(Refusal, rules => new JsonObject
    {
        ["hookEventName"] = "PreToolUse",
        ["permissionDecision"] = "deny",
        ["permissionDecisionReason"] = rules,
    }, Hold: true, new("; it will not be stopped twice:", "; it is stopped again until every rule below has arrived whole or been read:"));

    internal static IReadOnlySet<string> Writers => WriterSet.Value;

    public static Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken) =>
        RunAsync(input, output, ClientRegistrar.Home(), cancellationToken);

    internal static async Task<int> RunAsync(TextReader input, TextWriter output, string home, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (FolderRuleHook.Parse(payload) is { } hook && await AnswerAsync(hook, home, cancellationToken).ConfigureAwait(false) is { } answer)
                await DeliverAsync(output, answer).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return 0;
    }

    private static Task<Answer?> AnswerAsync(FolderRuleHook hook, string home, CancellationToken cancellationToken) => hook switch
    {
        { Event: "SessionStart" } => Forgotten(hook, home),
        { Event: "PreToolUse", Tool: { } tool, DryRun: false } when Writers.Contains(tool) => AnsweredAsync(hook, home, Denial, cancellationToken),
        { Event: "PostToolUse", Tool: not null } => AnsweredAsync(hook, home, Injection, cancellationToken),
        _ => Task.FromResult<Answer?>(null),
    };

    private static Task<Answer?> Forgotten(FolderRuleHook hook, string home)
    {
        new FolderRuleLedger(home, hook.Session).Forget();

        return Task.FromResult<Answer?>(null);
    }

    private static async Task<Answer?> AnsweredAsync(FolderRuleHook hook, string home, Reply reply, CancellationToken cancellationToken)
    {
        var ledger = new FolderRuleLedger(home, hook.Session);
        var unseen = Unseen(hook, ledger, reply);

        if (unseen is [])
            return null;

        var contents = await ContentsAsync(unseen, cancellationToken).ConfigureAwait(false);
        var channel = ChannelFor(reply, hook, Whole(reply.Framing, contents));
        var planned = Planned(reply.Framing, channel, contents);
        var shown = Shown(ledger, reply, planned);

        return shown is [] ? null : new Answer(new JsonObject { ["hookSpecificOutput"] = channel.Output(Rendered(reply, shown, Waiting(contents, planned))) }.ToJsonString(), ledger);
    }

    private static async Task<List<(string Rule, string Content)>> ContentsAsync(List<string> rules, CancellationToken cancellationToken)
    {
        var contents = new List<(string Rule, string Content)>(rules.Count);

        foreach (var rule in rules)
            contents.Add((rule, await File.ReadAllTextAsync(rule, cancellationToken).ConfigureAwait(false)));

        return contents;
    }

    private static int Whole(int used, List<(string Rule, string Content)> contents) =>
        used + contents.Sum(item => SectionFrame + item.Rule.Length + item.Content.AsSpan().TrimEnd().Length);

    private static Channel ChannelFor(Reply reply, FolderRuleHook hook, int whole) => (reply.Hold, whole <= ContextLimit, ResultRoom(hook)) switch
    {
        (true, _, _) => new(ToolResultLimit - NoteReserve, Verbatim: true, reply.Output),
        (false, true, _) => new(ContextLimit, Verbatim: false, reply.Output),
        (false, false, ({ } response, >= ContextLimit and var room)) => new(room - NoteReserve, Verbatim: true, rules => Appended(response, rules)),
        _ => new(ContextLimit - NoteReserve, Verbatim: false, reply.Output),
    };

    private static int ResultLength(JsonArray response) =>
        response.Sum(block => block is JsonObject item && item["text"] is JsonValue text && text.TryGetValue(out string? value) ? value.Length : block?.ToJsonString().Length ?? 0);

    private static JsonObject Appended(JsonArray response, string rules)
    {
        JsonNode?[] blocks = [.. response.Select(block => block?.DeepClone()), new JsonObject { ["type"] = "text", ["text"] = rules }];

        return new JsonObject
        {
            ["hookEventName"] = "PostToolUse",
            ["updatedToolOutput"] = new JsonArray(blocks),
        };
    }

    private static List<RuleSection> Planned(int used, Channel channel, List<(string Rule, string Content)> contents)
    {
        var planned = new List<RuleSection>(contents.Count);
        var fresh = channel.Capacity - used;

        foreach (var (rule, content) in contents)
        {
            if (Section(rule, content.AsSpan().TrimEnd(), channel with { Capacity = channel.Capacity - used }, fresh) is not { } section)
                continue;

            used += section.Text.Length;
            planned.Add(section);
        }

        return planned;
    }

    private static RuleSection? Section(string rule, ReadOnlySpan<char> body, Channel room, int fresh) =>
    (SectionFrame + rule.Length + body.Length) switch
    {
        var whole when whole <= room.Capacity => new(rule, string.Create(CultureInfo.InvariantCulture, $"\n\n--- {rule} ---\n{body}"), Whole: true),
        var whole when whole <= fresh || room.Capacity - PointerReserve - (3 * rule.Length) < MinimumRoom => null,
        _ when room.Verbatim => new(rule, Opening(rule, body, room.Capacity - PointerReserve - (3 * rule.Length)), Whole: false),
        _ => new(rule, Digested(rule, body, room.Capacity - PointerReserve - (3 * rule.Length)), Whole: false),
    };

    private static List<RuleSection> Shown(FolderRuleLedger ledger, Reply reply, List<RuleSection> planned)
    {
        var claimed = ledger.Claim([.. planned.Select(section => section.Rule)], reply.ShowUnrecorded, reply.ShowLost, hold: true);
        var shown = planned.FindAll(section => claimed.Contains(section.Rule));

        foreach (var section in shown)
            Record(ledger, section, reply.Hold);

        return shown;
    }

    private static void Record(FolderRuleLedger ledger, RuleSection section, bool hold)
    {
        if (!section.Whole)
            ledger.Partial(section.Rule);
        else if (hold)
            ledger.Complete(section.Rule);
    }

    private sealed record Reply(string Preface, Func<string, JsonObject> Output, bool Hold = false, Closing Closing = default)
    {
        internal bool ShowUnrecorded => !Hold;

        internal bool ShowLost => Hold;

        internal int Framing => Preface.Length + Closing.Length;
    }

    private sealed record Channel(int Capacity, bool Verbatim, Func<string, JsonObject> Output);

    private readonly record struct RuleSection(string Rule, string Text, bool Whole);

    internal const string Variable = "TERSE_FOLDER_RULES";

    internal static bool Enabled() => !string.Equals(Environment.GetEnvironmentVariable(Variable), "0", StringComparison.Ordinal);

    private static async Task DeliverAsync(TextWriter output, Answer answer)
    {
        try
        {
            await output.WriteLineAsync(answer.Text).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            answer.Ledger.Release();
        }
    }

    private sealed record Answer(string Text, FolderRuleLedger Ledger);

    internal static void Settle(string payload, string home)
    {
        try
        {
            if (FolderRuleHook.Parse(payload) is { Event: "PostToolBatch" } hook)
                new FolderRuleLedger(home, hook.Session).Settle();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private const int PointerReserve = 360;
    private static readonly string[] BindingMarkers = ["MUST", "NEVER", "Never", "never", "Do not", "do not", "Don't", "don't", "BANNED", "Banned", "banned", "HARD GATE", "🚫", "ALWAYS", "Always", "always", "FORBIDDEN", "Forbidden", "forbidden", "MANDATORY", "Mandatory", "mandatory", "REQUIRED", "required"];

    private static string Opening(string rule, ReadOnlySpan<char> body, int room)
    {
        var end = OpeningEnd(body, room, out var lines);
        var head = body[..end].TrimEnd();
        var rest = string.Create(CultureInfo.InvariantCulture, $"read_text path=\"{rule}\" startLine={lines + 1}");

        return string.Create(CultureInfo.InvariantCulture, $"\n\n--- {rule} --- {body.Length} characters, too long for one tool result, so lines 1-{lines} follow\n{head}{Directive(rule, rest)}");
    }

    private static int OpeningEnd(ReadOnlySpan<char> body, int room, out int lines)
    {
        var end = 0;
        lines = 0;

        while (end < body.Length)
        {
            var newline = body[end..].IndexOf('\n');
            var next = newline < 0 ? body.Length : end + newline + 1;

            if (next > room)
                break;

            end = next;
            lines++;
        }

        return end;
    }

    private static string Directive(string rule, string read) =>
        string.Create(CultureInfo.InvariantCulture, $"\nYou MUST read the rest of {rule} before reading or changing anything below {Path.GetDirectoryName(rule)} - a terse write below it is denied until you have: {read}");

    private static string Digested(string rule, ReadOnlySpan<char> body, int room)
    {
        var digest = new StringBuilder(Math.Clamp(room, 0, ContextLimit));
        var kept = 0;
        var fenced = false;

        foreach (var line in body.EnumerateLines())
        {
            var trimmed = line.TrimEnd();

            if (Fenced(trimmed, ref fenced) || !Binding(trimmed) || digest.Length + trimmed.Length + 1 > room)
                continue;

            digest.Append('\n').Append(trimmed);
            kept++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"\n\n--- {rule} --- {body.Length} characters, too long to inject whole, so only its {kept} heading and rule line(s) follow{digest}{Directive(rule, string.Create(CultureInfo.InvariantCulture, $"read_text path=\"{rule}\" verbose=true"))}");
    }

    private static bool Binding(ReadOnlySpan<char> line)
    {
        if (line.TrimStart().StartsWith('#'))
            return true;

        foreach (var marker in BindingMarkers)
        {
            if (line.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool Fenced(ReadOnlySpan<char> line, ref bool fenced)
    {
        if (!line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            return fenced;

        fenced = !fenced;

        return true;
    }

    private static (JsonArray? Response, int Room) ResultRoom(FolderRuleHook hook) =>
        hook.Payload["tool_response"] is JsonArray response ? (response, ToolResultLimit - ResultLength(response)) : (null, 0);

    private const int MinimumRoom = 1_000;

    private static string Waiting(List<(string Rule, string Content)> contents, List<RuleSection> planned)
    {
        var waiting = contents.Where(item => !planned.Exists(section => PathBoundary.Comparer.Equals(section.Rule, item.Rule))).Select(item => item.Rule).ToList();
        var more = waiting.Count > NamedWaiting ? ", ..." : string.Empty;

        return waiting is []
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"\n\n{waiting.Count} more folder rule file(s) did not fit here and arrive whole with your next terse call below them: {string.Join(", ", waiting.Take(NamedWaiting))}{more}");
    }

    private const int NoteReserve = 1_000;
    private const int NamedWaiting = 3;

    private static List<string> Unseen(FolderRuleHook hook, FolderRuleLedger ledger, Reply reply)
    {
        ledger.Read(hook is { Tool: ReadTool, ReadsText: true } ? FolderRulePaths.RuleFiles(hook.Targets(), hook.Bases()) : []);

        return [.. FolderRulePaths.Rules(hook.Targets(), hook.Mentioned(), hook.Bases(), hook.Cwd).Where(rule => reply.Hold ? !ledger.Settled(rule) : !ledger.Seen(rule))];
    }

    private readonly record struct Closing(string Settled, string Held)
    {
        internal int Length => Math.Max(Settled?.Length ?? 0, Held?.Length ?? 0);
    }

    private static string Rendered(Reply reply, List<RuleSection> shown, string waiting)
    {
        string[] text = [reply.Preface, shown.TrueForAll(section => section.Whole) && waiting.Length is 0 ? reply.Closing.Settled : reply.Closing.Held, .. shown.Select(section => section.Text), waiting];

        return string.Concat(text);
    }
}
