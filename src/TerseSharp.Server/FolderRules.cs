using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace TerseSharp.Server;

public static class FolderRules
{
    internal const int Budget = 8_000;

    internal const int ContextLimit = 10_000;

    private const int SectionFrame = 11;

    private const string Preface =
        "Folder rules from nested CLAUDE.md files. Claude Code loads these only for its own Read, Edit and Write, so terse injects them for the files this call touched. They bind every read, edit and test below their folder:";

    private const string Refusal =
        "This write lands below folder rules this session has not seen - Claude Code loads nested CLAUDE.md files only for its own Read, Edit and Write. Make the write comply with them, then send the same call again; it will not be stopped twice:";

    private static readonly Lazy<FrozenSet<string>> WriterSet = new(FolderRuleHook.WritingTools);

    private static readonly Reply Injection = new(Preface, ShowUnrecorded: true, rules => new JsonObject
    {
        ["hookEventName"] = "PostToolUse",
        ["additionalContext"] = rules,
    });

    private static readonly Reply Denial = new(Refusal, ShowUnrecorded: false, rules => new JsonObject
    {
        ["hookEventName"] = "PreToolUse",
        ["permissionDecision"] = "deny",
        ["permissionDecisionReason"] = rules,
    }, ShowLost: true);

    internal static IReadOnlySet<string> Writers => WriterSet.Value;

    public static Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken) =>
        RunAsync(input, output, ClientRegistrar.Home(), cancellationToken);

    internal static async Task<int> RunAsync(TextReader input, TextWriter output, string home, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await input.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (FolderRuleHook.Parse(payload) is { } hook && await AnswerAsync(hook, home, cancellationToken).ConfigureAwait(false) is { } answer)
                await output.WriteLineAsync(answer).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return 0;
    }

    private static Task<string?> AnswerAsync(FolderRuleHook hook, string home, CancellationToken cancellationToken) => hook switch
    {
        { Event: "SessionStart" } => Forgotten(hook, home),
        { Event: "PreToolUse", Tool: { } tool } when Writers.Contains(tool) => AnsweredAsync(hook, home, Denial, cancellationToken),
        { Event: "PostToolUse", Tool: not null } => AnsweredAsync(hook, home, Injection, cancellationToken),
        _ => Task.FromResult<string?>(null),
    };

    private static Task<string?> Forgotten(FolderRuleHook hook, string home)
    {
        new FolderRuleLedger(home, hook.Session).Forget();

        return Task.FromResult<string?>(null);
    }

    private static async Task<string?> AnsweredAsync(FolderRuleHook hook, string home, Reply reply, CancellationToken cancellationToken)
    {
        var ledger = new FolderRuleLedger(home, hook.Session);
        var unseen = FolderRulePaths.Rules(hook.Targets(), hook.Mentioned(), hook.Bases(), hook.Cwd).Where(rule => !ledger.Seen(rule)).ToList();

        if (unseen is [])
            return null;

        var planned = Planned(reply.Preface.Length, await ContentsAsync(unseen, cancellationToken).ConfigureAwait(false));
        var shown = ledger.Claim([.. planned.Select(section => section.Rule)], reply.ShowUnrecorded, reply.ShowLost);
        string[] text = [reply.Preface, .. planned.Where(section => shown.Contains(section.Rule)).Select(section => section.Text)];

        return shown.Count is 0 ? null : new JsonObject { ["hookSpecificOutput"] = reply.Output(string.Concat(text)) }.ToJsonString();
    }

    private static async Task<List<(string Rule, string Content)>> ContentsAsync(List<string> rules, CancellationToken cancellationToken)
    {
        var contents = new List<(string Rule, string Content)>(rules.Count);

        foreach (var rule in rules)
            contents.Add((rule, await File.ReadAllTextAsync(rule, cancellationToken).ConfigureAwait(false)));

        return contents;
    }

    private static List<RuleSection> Planned(int used, List<(string Rule, string Content)> contents)
    {
        var planned = new List<RuleSection>(contents.Count);

        foreach (var (rule, content) in contents)
        {
            var text = Section(rule, content, used);

            if (used + text.Length > ContextLimit)
                continue;

            used += text.Length;
            planned.Add(new(rule, text));
        }

        return planned;
    }

    private static string Section(string rule, string content, int used)
    {
        var body = content.AsSpan().TrimEnd();

        return used + SectionFrame + rule.Length + body.Length <= Budget
            ? string.Create(CultureInfo.InvariantCulture, $"\n\n--- {rule} ---\n{body}")
            : string.Create(CultureInfo.InvariantCulture, $"\n\n--- {rule} --- {body.Length} characters, over this hook's {Budget}-character budget - read it now: read_text path=\"{rule}\" verbose=true");
    }

    private sealed record Reply(string Preface, bool ShowUnrecorded, Func<string, JsonObject> Output, bool ShowLost = false);

    private readonly record struct RuleSection(string Rule, string Text);
}
