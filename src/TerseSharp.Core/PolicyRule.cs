namespace TerseSharp.Core;

public enum PolicyAction
{
    Off,
    Warn,
    Reject
}

public enum PolicyRule
{
    CognitiveComplexity,
    MethodStatements,
    TypeMethods,
    ConstructorDependencies,
    ParameterCount,
    MethodNameLength,
    MeaninglessSuffix,
    Naming,
    AsyncVoid,
    ComplexCondition,
    ChainedReferences,
    NestingDepth
}

public sealed record PolicyLimit(PolicyAction Action, int Value);

public sealed record PolicyRuleInfo(PolicyRule Rule, string Id, string Key, int Default, PolicyAction Action, string Remedy);

public static class PolicyRules
{
    public static IReadOnlyList<PolicyRuleInfo> All { get; } =
        [
            new(PolicyRule.CognitiveComplexity, "TERSE100", "cognitiveComplexity", 150, PolicyAction.Reject,
                "split the member - each extracted part must be a real concept with a domain name, not DoThingPart1"),
            new(PolicyRule.MethodStatements, "TERSE101", "methodStatements", 10, PolicyAction.Warn,
                "extract a well-named helper; the statement count is what ReSharper's MaximumMethodStatements counts"),
            new(PolicyRule.TypeMethods, "TERSE102", "typeMethods", 10, PolicyAction.Reject,
                "the type has more than one reason to change - split it"),
            new(PolicyRule.ConstructorDependencies, "TERSE103", "constructorDependencies", 5, PolicyAction.Reject,
                "group the dependencies behind one collaborator, or split the type"),
            new(PolicyRule.ParameterCount, "TERSE104", "parameterCount", 5, PolicyAction.Warn,
                "introduce a parameter object naming the concept the arguments share"),
            new(PolicyRule.MethodNameLength, "TERSE105", "methodNameLength", 3, PolicyAction.Reject,
                "name the method for what it does in the domain language"),
            new(PolicyRule.MeaninglessSuffix, "TERSE106", "meaninglessSuffix", 0, PolicyAction.Reject,
                "rename to state the responsibility instead of the suffix"),
            new(PolicyRule.Naming, "TERSE107", "naming", 0, PolicyAction.Reject,
                "rename to match the configured pattern for that declaration kind"),
            new(PolicyRule.AsyncVoid, "TERSE108", "asyncVoid", 0, PolicyAction.Reject,
                "return Task so the caller can await it and observe its exceptions"),
            new(PolicyRule.ComplexCondition, "TERSE109", "complexCondition", 3, PolicyAction.Warn,
                "name the condition - extract it into a predicate whose name says what it tests"),
            new(PolicyRule.ChainedReferences, "TERSE110", "chainedReferences", 3, PolicyAction.Off,
                "ask the collaborator for what you need instead of reaching through it"),
            new(PolicyRule.NestingDepth, "TERSE111", "nestingDepth", 4, PolicyAction.Reject,
                "invert the condition and return early, or extract the inner block")
        ];

    public const int CognitiveThreshold = 10;

    public static IReadOnlyList<string> MeaninglessSuffixes { get; } = ["Manager", "Processor", "Helper"];

    public static PolicyRuleInfo Of(PolicyRule rule) => All[(int)rule];

    public static PolicyRuleInfo? ByKey(string key) =>
        All.FirstOrDefault(info => string.Equals(info.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string Keys() => string.Join(", ", All.Select(info => info.Key));
}
