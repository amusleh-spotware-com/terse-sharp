namespace TerseSharp.Server;

public static class IdleLimit
{
    public const int DefaultMinutes = 15;

    public const string Variable = "TERSE_IDLE_MINUTES";

    public static TimeSpan Resolve(int? option) => Resolve(option, Environment.GetEnvironmentVariable(Variable));

    public static TimeSpan Resolve(int? option, string? environment) =>
        Minutes(option ?? Parse(environment) ?? DefaultMinutes);

    private static TimeSpan Minutes(int minutes) =>
        minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero;

    private static int? Parse(string? environment) =>
        int.TryParse(environment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
}
