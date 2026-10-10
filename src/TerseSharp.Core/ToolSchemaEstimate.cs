using System.Text;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct InstalledSchema(int Basis, int Frame);

public static class ToolSchemaEstimate
{
    public const int TokenCap = 1024;

    private const int ToolFrame = 40;

    private const int ParameterFrame = 40;

    private static readonly Dictionary<string, InstalledSchema> Unpublished = [];

    private static Func<IReadOnlyDictionary<string, InstalledSchema>>? published;

    private static Func<string, int> decorated = static _ => 0;

    public static void Publish(Func<IReadOnlyDictionary<string, InstalledSchema>> installed, Func<string, int> decoration)
    {
        Volatile.Write(ref decorated, decoration);
        Volatile.Write(ref published, installed);
    }

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

    public static int Tokens(DeclaredTool tool, IReadOnlyDictionary<string, InstalledSchema> installed, int decoration) =>
        SkillBudget.Estimated(tool.Basis + decoration + (installed.TryGetValue(tool.Name, out var schema) ? schema.Frame : ToolFrame + (tool.Parameters * ParameterFrame)));

    public static async Task<string> TrailerAsync(Document? before, Document after, CancellationToken cancellationToken)
    {
        var edited = await ToolSchemaSource.DeclaredAsync(after, cancellationToken).ConfigureAwait(false);

        if (edited.Count is 0)
            return string.Empty;

        var original = before is null ? (IReadOnlyList<DeclaredTool>)[] : await ToolSchemaSource.DeclaredAsync(before, cancellationToken).ConfigureAwait(false);

        return Trailer(edited, original, Volatile.Read(ref published)?.Invoke() ?? Unpublished, Volatile.Read(ref decorated));
    }

    public static string Trailer(IReadOnlyList<DeclaredTool> edited, IReadOnlyList<DeclaredTool> original, IReadOnlyDictionary<string, InstalledSchema> installed, Func<string, int> decoration)
    {
        StringBuilder? builder = null;

        foreach (var tool in edited)
        {
            if (Unchanged(tool, original))
                continue;

            var tokens = Tokens(tool, installed, decoration(tool.Name));
            builder ??= new StringBuilder(64);
            builder.Append(CultureInfo.InvariantCulture, $"  schema {tool.Name}={tokens} tokens (cap {TokenCap}, {(tokens > TokenCap ? "over" : "left")}={Math.Abs(tokens - TokenCap)})");
        }

        return builder?.ToString() ?? string.Empty;
    }

    private static bool Unchanged(DeclaredTool tool, IReadOnlyList<DeclaredTool> original)
    {
        foreach (var candidate in original)
        {
            if (candidate == tool)
                return true;
        }

        return false;
    }

    private static Flag? Flagged(DeclaredTool tool, IReadOnlyDictionary<string, InstalledSchema> installed, int decoration)
    {
        var known = installed.TryGetValue(tool.Name, out var schema);
        var tokens = Tokens(tool, installed, decoration);
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
