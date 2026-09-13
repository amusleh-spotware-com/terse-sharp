using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace TerseSharp.Server;

public static class ExploreSteer
{
    private const int MaxTracked = 256;
    private const int Explored = 32;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Entry> Tracked = new(MaxTracked, StringComparer.Ordinal);

    private record struct Entry(int Tools, bool Steered);

    public static string? Note(string tool, string? symbolId)
    {
        var bit = Bit(tool);

        if (bit is 0 || symbolId is not { Length: > 0 })
            return null;

        lock (Gate)
            return Noted(tool, symbolId, bit);
    }

    public static void Forget()
    {
        lock (Gate)
            Tracked.Clear();
    }

    private static string? Noted(string tool, string symbolId, int bit)
    {
        if (Tracked.Count >= MaxTracked && !Tracked.ContainsKey(symbolId))
            Tracked.Clear();

        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(Tracked, symbolId, out _);
        entry.Tools |= bit;
        entry.Steered |= bit is Explored;

        if (entry.Steered || int.PopCount(entry.Tools) < 2 || tool is not ("get_symbol_source" or "find_usages"))
            return null;

        entry.Steered = true;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"this symbol has now cost two navigation calls - explore_symbol symbolId=\"{symbolId}\" answers signature, usages and implementations in ONE call");
    }

    private static int Bit(string tool) => tool switch
    {
        "get_symbol" => 1,
        "get_symbol_source" => 2,
        "get_type_outline" => 4,
        "find_usages" => 8,
        "find_implementations" => 16,
        "explore_symbol" => Explored,
        _ => 0,
    };

    public static string? Argument(CallToolRequestParams parameters)
    {
        if (parameters.Arguments is not { } arguments)
            return null;

        if (!arguments.TryGetValue("symbolId", out var element) && !arguments.TryGetValue("symbol", out element))
            return null;

        return element.ValueKind is JsonValueKind.String ? element.GetString() : null;
    }
}
