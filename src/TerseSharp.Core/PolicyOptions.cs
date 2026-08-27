using System.Collections.Frozen;
using System.Collections.Immutable;

namespace TerseSharp.Core;

public sealed record PolicyOptions(
    bool Enabled,
    bool AllowOverride,
    int CognitiveThreshold,
    FrozenDictionary<PolicyRule, PolicyLimit> Rules,
    ImmutableArray<string> MeaninglessSuffixes,
    FrozenDictionary<NamingKind, NamingPattern> Naming,
    ImmutableArray<string> Ignored,
    string? Failure,
    string? Path)
{
    public static PolicyOptions Off { get; } = new(
        false,
        true,
        PolicyRules.CognitiveThreshold,
        FrozenDictionary<PolicyRule, PolicyLimit>.Empty,
        [],
        FrozenDictionary<NamingKind, NamingPattern>.Empty,
        [],
        null,
        null);

    public static PolicyOptions Defaults { get; } = Off with
    {
        Enabled = true,
        Rules = PolicyRules.All.ToFrozenDictionary(info => info.Rule, info => new PolicyLimit(info.Action, info.Default)),
        MeaninglessSuffixes = [.. PolicyRules.MeaninglessSuffixes],
        Naming = NamingDefaults.Patterns,
    };

    public bool Active => Enabled && Rules.Any(entry => entry.Value.Action is not PolicyAction.Off);

    public PolicyLimit Limit(PolicyRule rule) =>
        Rules.TryGetValue(rule, out var limit) ? limit : new PolicyLimit(PolicyAction.Off, PolicyRules.Of(rule).Default);

    public bool Enforces(PolicyRule rule) => Enabled && Limit(rule).Action is not PolicyAction.Off;

    public int CognitiveLimit()
    {
        var percent = Limit(PolicyRule.CognitiveComplexity).Value;

        return (int)Math.Floor(CognitiveThreshold * (percent / 100.0));
    }

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"policy enabled=true rules={Rules.Count(entry => entry.Value.Action is not PolicyAction.Off)} cognitiveThreshold={CognitiveThreshold} allowOverride={AllowOverride}");
}
