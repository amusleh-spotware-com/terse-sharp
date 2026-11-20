namespace TerseSharp.Server;

internal static class GuardNotice
{
    public static string Prefixed(string rewrite, string? routing) =>
        "printf '%s\\n' '" + Quoted(Message(routing)) + "' >&2\n" + rewrite;

    private static string Message(string? routing) => routing is { Length: > 0 }
        ? "terse guard: part of this command was removed and did NOT run - call instead: " + routing
        : "terse guard: part of this command was removed and did NOT run - the hook note names the terse call that replaces it";

    private static string Quoted(string text) => text.Replace("'", "'\\''", StringComparison.Ordinal);
}
