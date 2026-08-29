namespace TerseSharp.Core;

public static class EditPulse
{
    private static int changed;

    public static int Changed => Volatile.Read(ref changed);

    public static void Bump(int documents)
    {
        if (documents > 0)
            Interlocked.Add(ref changed, documents);
    }
}
