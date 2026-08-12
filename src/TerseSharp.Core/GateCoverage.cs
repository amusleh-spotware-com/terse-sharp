namespace TerseSharp.Core;

public static class GateCoverage
{
    public const string Unchecked =
        "gate=semantic - errors=0 means the semantic model is clean; emit-time and source-generator errors are NOT checked, so run build once before you push, not after every edit";

    private static int announced;

    public static string? Once() => Interlocked.Exchange(ref announced, 1) is 0 ? Unchecked : null;

    public static void Forget() => Interlocked.Exchange(ref announced, 0);
}
