using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class LineEndingsApplyTests
{
    [Fact]
    public void Apply_ConvertsLfToCrlf() =>
        Assert.Equal("a\r\nb\r\n", LineEndings.Apply("a\nb\n", LineEndings.Windows));

    [Fact]
    public void Apply_ConvertsCrlfToLf() =>
        Assert.Equal("a\nb\n", LineEndings.Apply("a\r\nb\r\n", LineEndings.Unix));

    [Fact]
    public void Apply_LeavesAFormFeedInsideALiteralAlone() =>
        Assert.Equal("var s = \"a\fb\";\n", LineEndings.Apply("var s = \"a\fb\";\n", LineEndings.Unix));

    [Theory]
    [InlineData(0x000B)]
    [InlineData(0x0085)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void Apply_LeavesOtherUnicodeSeparatorsAlone(int code)
    {
        var content = "x" + (char)code + "y\n";

        Assert.Equal(content, LineEndings.Apply(content, LineEndings.Unix));
    }

    [Fact]
    public void Apply_WithNoTrailingNewline_KeepsTheLastLine() =>
        Assert.Equal("a\r\nb", LineEndings.Apply("a\nb", LineEndings.Windows));

    [Fact]
    public void Uniform_ForACrlfOnlyFile_IsWindows() =>
        Assert.Equal(LineEndings.Windows, LineEndings.Uniform("a\r\nb\r\n"));

    [Fact]
    public void Uniform_ForAnLfOnlyFile_IsUnix() =>
        Assert.Equal(LineEndings.Unix, LineEndings.Uniform("a\nb\n"));

    [Fact]
    public void Uniform_ForAMixedFile_IsNull() =>
        Assert.Null(LineEndings.Uniform("a\r\nb\nc\r\n"));

    [Fact]
    public void Uniform_ForAFileWithNoNewlines_IsNull() => Assert.Null(LineEndings.Uniform("single"));
}
