namespace TerseSharp.Server;

public static class UpdateBanner
{
    private static string? pending;

    public static void Publish(string? notice) => Interlocked.Exchange(ref pending, notice);

    public static string? Take() => Interlocked.Exchange(ref pending, null);
}
