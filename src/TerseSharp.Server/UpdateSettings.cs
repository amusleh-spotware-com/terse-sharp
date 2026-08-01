using System.Reflection;

namespace TerseSharp.Server;

public static class UpdateSettings
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static UpdateRequest? Requested() =>
        Enabled() && ReleaseVersion.TryParse(Running(), out var running)
            ? new UpdateRequest(running, StatePath(), Endpoint(), Window)
            : null;

    public static string StatePath() => Path.Combine(ClientRegistrar.Home(), ".terse", "update");

    public static string Running() =>
        typeof(UpdateSettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;

    public static bool Enabled() =>
        !string.Equals(Environment.GetEnvironmentVariable("TERSE_UPDATE"), "0", StringComparison.Ordinal);

    private static string Endpoint() =>
        Environment.GetEnvironmentVariable("TERSE_UPDATE_URL") is { Length: > 0 } overridden
            ? overridden
            : UpdateCheck.DefaultEndpoint;
}
