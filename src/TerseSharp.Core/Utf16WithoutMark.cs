using System.Text;

namespace TerseSharp.Core;

public static class Utf16WithoutMark
{
    public static Encoding? Detect(ReadOnlySpan<byte> probe)
    {
        if (probe.Length < 4 || ByteOrderMark.Detect(probe).CodeUnit is not 1)
            return null;

        if (Consistent(probe, zeroAt: 1))
            return Encoding.Unicode;

        return Consistent(probe, zeroAt: 0) ? Encoding.BigEndianUnicode : null;
    }

    private static bool Consistent(ReadOnlySpan<byte> probe, int zeroAt)
    {
        var pairs = probe.Length / 2;

        for (var index = 0; index < pairs; index++)
        {
            if (probe[(index * 2) + zeroAt] is not 0 || !Printable(probe[(index * 2) + (1 - zeroAt)]))
                return false;
        }

        return pairs >= 2;
    }

    private static bool Printable(byte value) => value is 0x09 or 0x0A or 0x0D or (>= 0x20 and < 0x7F);
}
