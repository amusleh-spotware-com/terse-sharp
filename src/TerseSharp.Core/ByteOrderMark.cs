using System.Text;

namespace TerseSharp.Core;

internal readonly record struct ByteOrderMark(int Length, int CodeUnit)
{
    private static readonly ByteOrderMark Absent = new(0, 1);

    private static ReadOnlySpan<byte> Utf32LittleEndian => [0xFF, 0xFE, 0x00, 0x00];

    private static ReadOnlySpan<byte> Utf32BigEndian => [0x00, 0x00, 0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf16LittleEndian => [0xFF, 0xFE];

    private static ReadOnlySpan<byte> Utf16BigEndian => [0xFE, 0xFF];

    private static ReadOnlySpan<byte> Utf8 => [0xEF, 0xBB, 0xBF];

    public static ByteOrderMark Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Utf32LittleEndian) || head.StartsWith(Utf32BigEndian))
            return new(4, 4);

        if (head.StartsWith(Utf16LittleEndian) || head.StartsWith(Utf16BigEndian))
            return new(2, 2);

        return head.StartsWith(Utf8) ? new(3, 1) : Absent;
    }

    public static Encoding EncodingOf(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Utf32LittleEndian))
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);

        if (head.StartsWith(Utf32BigEndian))
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);

        return SixteenOrEight(head);
    }

    private static Encoding SixteenOrEight(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Utf16LittleEndian))
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        if (head.StartsWith(Utf16BigEndian))
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);

        return new UTF8Encoding(head.StartsWith(Utf8));
    }
}
