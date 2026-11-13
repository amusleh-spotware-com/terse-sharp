namespace TerseSharp.Server.Tools;

internal static class HeldAnchor
{
    public static IReadOnlyList<string>? For(string[]? add, string? container, RejectedEdit held) =>
        add is null && container is null && held.Add.Count > 0 ? held.Targets : null;
}
