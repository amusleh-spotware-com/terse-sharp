using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class AtomicWriteTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-atomic-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public async Task Text_KeepsTheByteOrderMarkOfTheFileItReplaces()
    {
        var path = Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await AtomicWrite.TextAsync(path, "second", TestContext.Current.CancellationToken);

        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(path)[..3]);
    }

    [Fact]
    public async Task Text_DoesNotAddAByteOrderMarkToAFileThatHadNone()
    {
        var path = Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await AtomicWrite.TextAsync(path, "second", TestContext.Current.CancellationToken);

        Assert.Equal("second", File.ReadAllText(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public async Task Text_OnANewFile_WritesNoByteOrderMark()
    {
        var path = Path.Combine(directory.FullName, "fresh.txt");

        await AtomicWrite.TextAsync(path, "content", TestContext.Current.CancellationToken);

        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public async Task Text_CreatesTheDirectoriesTheTargetNeeds()
    {
        var path = Path.Combine(directory.FullName, "nested", "deeper", "created.txt");

        await AtomicWrite.TextAsync(path, "content", TestContext.Current.CancellationToken);

        Assert.Equal("content", File.ReadAllText(path));
    }

    private string Write(Encoding encoding)
    {
        var path = Path.Combine(directory.FullName, "sample.txt");

        File.WriteAllText(path, "first", encoding);

        return path;
    }
}
