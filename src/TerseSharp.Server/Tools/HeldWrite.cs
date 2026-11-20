namespace TerseSharp.Server.Tools;

internal static class HeldWrite
{
    internal static bool Force(bool asked, string? path, RejectedEdit held) =>
        asked || (path is not { Length: > 0 } && held.Forced is [true, ..]);

    internal static bool Overwrite(bool asked, string? path, RejectedEdit held) =>
        asked || (path is not { Length: > 0 } && held.Overwrite);
}
