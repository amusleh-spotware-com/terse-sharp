namespace TerseSharp.Server;

public static class OfferMode
{
    public const string Variable = "TERSE_OFFERS";

    public static bool Once(string? environment) => !string.Equals(environment, "always", StringComparison.OrdinalIgnoreCase);
}
