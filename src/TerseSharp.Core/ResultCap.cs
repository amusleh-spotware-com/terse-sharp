namespace TerseSharp.Core;

public static class ResultCap
{
    private const int SlackDivisor = 10;

    public static int Shown(int total, int cap) =>
        cap > 0 && total <= cap + (cap / SlackDivisor) ? total : Math.Min(total, cap);

    public static IEnumerable<T> Capped<T>(this IReadOnlyCollection<T> items, int cap) =>
        items.Take(Shown(items.Count, cap));
}
