using System.Text;

namespace TerseSharp.Core;

public readonly record struct InstalledSchema(int Basis, int Frame);

public static class ToolSchemaEstimate
{
    public const int TokenCap = 1024;

    private const int ToolFrame = 40;

    private const int ParameterFrame = 40;

    public static string Render(IReadOnlyList<DeclaredTool> declared, IReadOnlyDictionary<string, InstalledSchema> installed, Func<string, int> decoration)
    {
        var flagged = new List<Flag>(declared.Count);

        foreach (var tool in declared)
        {
            if (Flagged(tool, installed, decoration(tool.Name)) is { } flag)
                flagged.Add(flag);
        }

        flagged.Sort(static (left, right) => right.Tokens.CompareTo(left.Tokens));

        return Header(declared.Count, flagged) + Lines(flagged);
    }

    private static Flag? Flagged(DeclaredTool tool, IReadOnlyDictionary<string, InstalledSchema> installed, int decoration)
    {
        var known = installed.TryGetValue(tool.Name, out var schema);
        var tokens = SkillBudget.Estimated(tool.Basis + decoration + (known ? schema.Frame : ToolFrame + (tool.Parameters * ParameterFrame)));
        var changed = !known || schema.Basis != tool.Basis;

        return changed || tokens > TokenCap
            ? new Flag(tool.Name, known ? SkillBudget.Estimated(schema.Basis + schema.Frame + decoration) : null, tokens, changed)
            : null;
    }

    private static string Header(int declared, List<Flag> flagged)
    {
        var changed = 0;
        var over = 0;

        foreach (var flag in flagged)
        {
            changed += flag.Changed ? 1 : 0;
            over += flag.Tokens > TokenCap ? 1 : 0;
        }

        return string.Create(CultureInfo.InvariantCulture, $"source={declared} tools declared in the working tree, schema estimated HEURISTIC: {changed} differ from the running server, {over} over the {TokenCap}-token cap");
    }

    private static string Lines(List<Flag> flagged)
    {
        var builder = new StringBuilder(flagged.Count * 40);

        foreach (var flag in flagged)
            builder.Append(CultureInfo.InvariantCulture, $"\n  {flag.Name} {flag.Was?.ToString(CultureInfo.InvariantCulture) ?? "new"} -> {flag.Tokens}{(flag.Tokens > TokenCap ? " OVER" : string.Empty)}");

        return builder.ToString();
    }

    private readonly record struct Flag(string Name, int? Was, int Tokens, bool Changed);
}
