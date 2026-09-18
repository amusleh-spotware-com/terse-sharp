using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Core;

public static class PolicyDocument
{
    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    public static string Render()
    {
        var root = new JsonObject();

        Complete(root);

        return Text(root);
    }

    public static string? TopUp(string existing)
    {
        try
        {
            if (JsonNode.Parse(existing) is not JsonObject root)
                return null;

            var before = root.ToJsonString();

            if (Complete(root) is null)
                return null;

            return string.Equals(root.ToJsonString(), before, StringComparison.Ordinal) ? null : Text(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject? Complete(JsonObject root)
    {
        if (Section(root, "policy") is not { } policy || Section(policy, "rules") is not { } rules)
            return null;

        policy["cognitiveThreshold"] ??= JsonValue.Create(PolicyRules.CognitiveThreshold);
        policy["allowOverride"] ??= JsonValue.Create(true);

        foreach (var info in PolicyRules.All)
            rules[info.Key] ??= Rule(info);

        return root;
    }

    private static JsonObject Rule(PolicyRuleInfo info) => new()
    {
        ["action"] = JsonValue.Create(Word(info.Action)),
        ["limit"] = JsonValue.Create(info.Default),
    };

    private static JsonObject? Section(JsonObject owner, string name)
    {
        if (owner[name] is { } declared)
            return declared as JsonObject;

        var created = new JsonObject();

        owner[name] = created;

        return created;
    }

    private static string Word(PolicyAction action) => action switch
    {
        PolicyAction.Reject => "reject",
        PolicyAction.Warn => "warn",
        _ => "off",
    };

    private static string Text(JsonObject root) => root.ToJsonString(Layout) + Environment.NewLine;
}
