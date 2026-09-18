using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class Utf16WithoutMarkTests
{
    [Fact]
    public void Detect_ForLittleEndianTextCarryingNoMark_AnswersUnicode() =>
        Assert.Equal(Encoding.Unicode, Utf16WithoutMark.Detect(Encoding.Unicode.GetBytes("alpha beta\r\n")));

    [Fact]
    public void Detect_ForBigEndianTextCarryingNoMark_AnswersBigEndianUnicode() =>
        Assert.Equal(Encoding.BigEndianUnicode, Utf16WithoutMark.Detect(Encoding.BigEndianUnicode.GetBytes("alpha beta\r\n")));

    [Fact]
    public void Detect_ForPlainUtf8Text_AnswersNothing() =>
        Assert.Null(Utf16WithoutMark.Detect(Encoding.UTF8.GetBytes("alpha beta")));

    [Fact]
    public void Detect_ForTextCarryingAMark_AnswersNothingBecauseTheMarkAlreadySaysSo() =>
        Assert.Null(Utf16WithoutMark.Detect([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("alpha")]));

    [Fact]
    public void Detect_ForRealBinaryContent_AnswersNothing() =>
        Assert.Null(Utf16WithoutMark.Detect([0x00, 0x01, 0x02, 0x00, 0xFF, 0xFE, 0x03, 0x00]));

    [Fact]
    public void Detect_ForAProbeTooShortToDecide_AnswersNothing() =>
        Assert.Null(Utf16WithoutMark.Detect([0x61, 0x00]));
}
