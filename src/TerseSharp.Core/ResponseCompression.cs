namespace TerseSharp.Core;

internal static class ResponseCompression
{
    public static string Counters(int errors, int errorDelta, int warnings, int warningDelta)
    {
        var left = Counter("errors", errors, errorDelta);
        var right = Counter("warnings", warnings, warningDelta);

        return string.Concat(left, left.Length > 0 && right.Length > 0 ? " " : string.Empty, right);
    }

    public static string VerboseCounters(int errors, int errorDelta, int warnings, int warningDelta) => string.Create(
        CultureInfo.InvariantCulture,
        $"errors={errors} ({Signed(errorDelta)}) warnings={warnings} ({Signed(warningDelta)})");

    private static string Counter(string name, int count, int delta) => count is 0 && delta is 0
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"{name}={count} ({Signed(delta)})");

    private static string Signed(int delta) =>
        delta >= 0 ? "+" + delta.ToString(CultureInfo.InvariantCulture) : delta.ToString(CultureInfo.InvariantCulture);
}
