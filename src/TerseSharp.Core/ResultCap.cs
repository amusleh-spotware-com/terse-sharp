namespace TerseSharp.Core;

public static class ResultCap
{
    private const int SlackDivisor = 10;
    private const int WholeMultiple = 2;

    public static int Shown(int total, int cap) =>
        cap > 0 && total <= cap + (cap / SlackDivisor) ? total : Math.Min(total, cap);

    public static int Whole(int total, int cap) =>
        cap > 0 && total <= cap * WholeMultiple ? total : Math.Min(total, cap);

    public static IEnumerable<T> Capped<T>(this IReadOnlyCollection<T> items, int cap) =>
        items.Take(Shown(items.Count, cap));

    public static IEnumerable<T> CappedWhole<T>(this IReadOnlyCollection<T> items, int cap) =>
        items.Take(Whole(items.Count, cap));
}
