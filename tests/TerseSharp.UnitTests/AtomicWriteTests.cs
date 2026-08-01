using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class AtomicWriteTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-atomic-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public void Text_KeepsTheByteOrderMarkOfTheFileItReplaces()
    {
        var path = Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        AtomicWrite.Text(path, "second");

        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
    }

    [Fact]
    public void Text_DoesNotAddAByteOrderMarkToAFileThatHadNone()
    {
        var path = Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        AtomicWrite.Text(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void Text_OnANewFile_WritesNoByteOrderMark()
    {
        var path = Path.Combine(directory.FullName, "fresh.txt");

        AtomicWrite.Text(path, "content");

        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    private string Write(Encoding encoding)
    {
        var path = Path.Combine(directory.FullName, "sample.txt");

        File.WriteAllText(path, "first", encoding);

        return path;
    }
}
