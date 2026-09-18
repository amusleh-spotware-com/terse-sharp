using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(EditPulseCollection))]
public sealed class BinaryContentTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("terse-bom").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf8bom")]
    [InlineData("utf8")]
    public async Task Reject_ForTextCarryingAByteOrderMark_ServesItInsteadOfCallingItBinary(string encoding)
    {
        var path = await WrittenAsync(Named(encoding), "@Model.BalanceString\n", encoding + ".cshtml");

        Assert.Null((await BinaryContent.ProbeAsync(path, "TransactionsReport.cshtml", TestContext.Current.CancellationToken)).Refusal);
    }

    [Fact]
    public async Task Reject_ForBytesWithNoByteOrderMarkAndANullByte_StillRefusesThem()
    {
        var path = Path.Combine(root, "image.png");

        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x00, 0x1A], TestContext.Current.CancellationToken);

        var rejected = (await BinaryContent.ProbeAsync(path, "image.png", TestContext.Current.CancellationToken)).Refusal;

        Assert.NotNull(rejected);
        Assert.Contains("looks binary", rejected?.Error?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_ForUtf16TextHoldingARealNullCharacter_RefusesIt()
    {
        var path = await WrittenAsync(Encoding.Unicode, "ab\0cd", "nulled.txt");

        Assert.NotNull((await BinaryContent.ProbeAsync(path, "nulled.txt", TestContext.Current.CancellationToken)).Refusal);
    }

    [Fact]
    public async Task EncodingOf_ForAFileWithAUtf16ByteOrderMark_RoundTripsItInsteadOfRewritingItAsUtf8()
    {
        var path = await WrittenAsync(Encoding.Unicode, "before\n", "note.md");

        await AtomicWrite.TextAsync(path, "after\n", TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.Unicode.GetPreamble(), bytes[..2]);
        Assert.Equal("after\n", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private async Task<string> WrittenAsync(Encoding encoding, string content, string name)
    {
        var path = Path.Combine(root, name);

        await File.WriteAllBytesAsync(
            path,
            [.. encoding.GetPreamble(), .. encoding.GetBytes(content)],
            TestContext.Current.CancellationToken);

        return path;
    }

    private static Encoding Named(string encoding) => encoding switch
    {
        "utf16le" => Encoding.Unicode,
        "utf16be" => Encoding.BigEndianUnicode,
        "utf32le" => Encoding.UTF32,
        "utf8bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };
}
