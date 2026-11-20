namespace TerseSharp.Core;

public static class SlowCall
{
    public const string Variable = "TERSE_LISTING_TIMING_MS";

    public static TimeSpan After { get; } = Configured(Environment.GetEnvironmentVariable(Variable));

    public static TimeSpan Configured(string? milliseconds) => long.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= int.MaxValue
        ? TimeSpan.FromMilliseconds(parsed)
        : TimeSpan.FromSeconds(10);
}
