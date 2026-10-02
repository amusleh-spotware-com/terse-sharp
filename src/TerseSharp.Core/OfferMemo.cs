using System.Collections.Concurrent;

namespace TerseSharp.Core;

public static class OfferMemo
{
    private static readonly ConcurrentDictionary<(string Tool, string Offer), byte> Shown = new();

    private static volatile bool remembers;

    public static void Remember(bool once) => remembers = once;

    public static bool First(string tool, string offer) => !remembers || Shown.TryAdd((tool, offer), 0);

    public static void Forget() => Shown.Clear();
}
