using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerseSharp.Core;

public static class PolicySettings
{
    public static async Task<PolicyOptions> LoadAsync(string directory, CancellationToken cancellationToken)
    {
        if (TerseConfigFile.Find(directory) is not { } path)
            return PolicyOptions.Off;

        try
        {
            var file = new FileInfo(path);

            if (file.Length > TerseConfigFile.MaxBytes)
                return PolicyOptions.Off with { Path = path, Failure = TerseConfigFile.Oversized(file.Length) };

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            return Parse(json) with { Path = path };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PolicyOptions.Off with { Path = path, Failure = exception.Message };
        }
    }

    public static PolicyOptions Parse(string json)
    {
        try
        {
            return Read(JsonNode.Parse(json) as JsonObject);
        }
        catch (JsonException exception)
        {
            return PolicyOptions.Off with { Failure = exception.Message };
        }
    }

    public static string? Notice(PolicyOptions options) => options switch
    {
        { Failure: { } failure } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: {options.Path} policy could not be read - {failure}; policy is off"),
        { Ignored: [_, ..] ignored } => string.Create(
            CultureInfo.InvariantCulture,
            $"terse: {options.Path} policy ignored {string.Join(", ", ignored)} - rules are {PolicyRules.Keys()}, naming kinds are {NamingDefaults.Keys()}"),
        _ => null,
    };

    private static PolicyOptions Read(JsonObject? root)
    {
        if (root?["policy"] is not JsonObject policy)
            return PolicyOptions.Off;

        if (Flag(policy, "enabled") is false)
            return PolicyOptions.Off;

        var ignored = ImmutableArray.CreateBuilder<string>();

        var options = PolicyOptions.Defaults with
        {
            AllowOverride = Flag(policy, "allowOverride") ?? true,
            CognitiveThreshold = Number(policy, "cognitiveThreshold") ?? PolicyRules.CognitiveThreshold,
        };

        return Sections(Uniform(options, policy, ignored), policy, ignored) with { Ignored = ignored.ToImmutable() };
    }

    private static PolicyOptions Sections(PolicyOptions options, JsonObject policy, ImmutableArray<string>.Builder ignored) =>
        WithNaming(WithSuffixes(WithRules(options, policy, ignored), policy, ignored), policy, ignored);

    private static PolicyOptions Uniform(PolicyOptions options, JsonObject policy, ImmutableArray<string>.Builder ignored)
    {
        if (policy["action"] is not { } declared)
            return options;

        if (declared.GetValueKind() is not JsonValueKind.String || ParseAction(declared.GetValue<string>()) is not { } action)
        {
            ignored.Add("action");

            return options;
        }

        return options with { Rules = options.Rules.ToFrozenDictionary(entry => entry.Key, entry => entry.Value with { Action = action }) };
    }

    private static PolicyOptions WithRules(PolicyOptions options, JsonObject policy, ImmutableArray<string>.Builder ignored)
    {
        if (policy["rules"] is not { } declared)
            return options;

        if (declared is not JsonObject entries)
        {
            ignored.Add("rules");

            return options;
        }

        var rules = options.Rules.ToDictionary();

        foreach (var entry in entries)
            Route(entry, rules, options, ignored);

        return options with { Rules = rules.ToFrozenDictionary() };
    }

    private static void Route(
        KeyValuePair<string, JsonNode?> entry,
        Dictionary<PolicyRule, PolicyLimit> rules,
        PolicyOptions options,
        ImmutableArray<string>.Builder ignored)
    {
        if (PolicyRules.ByKey(entry.Key) is not { } info)
        {
            ignored.Add(entry.Key);

            return;
        }

        if (Limit(entry.Value, options.Limit(info.Rule), info) is not { } limit)
        {
            ignored.Add(entry.Key);

            return;
        }

        rules[info.Rule] = limit;
    }

    private static PolicyLimit? Limit(JsonNode? node, PolicyLimit current, PolicyRuleInfo info) => node?.GetValueKind() switch
    {
        JsonValueKind.False => current with { Action = PolicyAction.Off },
        JsonValueKind.True => current with { Action = Enabled(current.Action, info) },
        JsonValueKind.Number when node.AsValue().TryGetValue<int>(out var value) && value >= 0 =>
            new PolicyLimit(Enabled(current.Action, info), value),
        JsonValueKind.Object => Detailed(node.AsObject(), current, info),
        _ => null,
    };

    private static PolicyAction Enabled(PolicyAction action, PolicyRuleInfo info) =>
        action is PolicyAction.Off ? info.Action : action;

    private static PolicyLimit? Detailed(JsonObject declared, PolicyLimit current, PolicyRuleInfo info)
    {
        var action = declared["action"] is { } named
            ? named.GetValueKind() is JsonValueKind.String ? ParseAction(named.GetValue<string>()) : null
            : Enabled(current.Action, info);

        if (action is not { } resolved)
            return null;

        return new PolicyLimit(resolved, Number(declared, "limit") ?? current.Value);
    }

    private static PolicyOptions WithSuffixes(PolicyOptions options, JsonObject policy, ImmutableArray<string>.Builder ignored)
    {
        if (policy["meaninglessSuffixes"] is not { } declared)
            return options;

        if (declared is not JsonArray entries || entries.Any(entry => entry?.GetValueKind() is not JsonValueKind.String))
        {
            ignored.Add("meaninglessSuffixes");

            return options;
        }

        return options with { MeaninglessSuffixes = [.. entries.Select(entry => entry!.GetValue<string>())] };
    }

    private static PolicyOptions WithNaming(PolicyOptions options, JsonObject policy, ImmutableArray<string>.Builder ignored)
    {
        if (policy["naming"] is not { } declared)
            return options;

        if (declared is not JsonObject entries)
        {
            ignored.Add("naming");

            return options;
        }

        var patterns = options.Naming.ToDictionary();

        foreach (var entry in entries)
            Name(entry, patterns, ignored);

        return options with { Naming = patterns.ToFrozenDictionary() };
    }

    private static void Name(
        KeyValuePair<string, JsonNode?> entry,
        Dictionary<NamingKind, NamingPattern> patterns,
        ImmutableArray<string>.Builder ignored)
    {
        if (NamingDefaults.Parse(entry.Key) is not { } kind || entry.Value?.GetValueKind() is not JsonValueKind.String)
        {
            ignored.Add(entry.Key);

            return;
        }

        if (NamingPattern.Create(kind, entry.Value.GetValue<string>()) is not { } pattern)
        {
            ignored.Add(entry.Key);

            return;
        }

        patterns[kind] = pattern;
    }

    private static PolicyAction? ParseAction(string value) => value.ToLowerInvariant() switch
    {
        "reject" => PolicyAction.Reject,
        "warn" => PolicyAction.Warn,
        "off" => PolicyAction.Off,
        _ => null,
    };

    private static bool? Flag(JsonObject declared, string key) =>
        declared[key]?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static int? Number(JsonObject declared, string key) =>
        declared[key] is { } node && node.GetValueKind() is JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value) && value >= 0
            ? value
            : null;
}
