using System.Text;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

[Collection(nameof(EditPulseCollection))]
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

    [Fact]
    public async Task TextAsync_WhileAnotherReaderStillHoldsTheTarget_RetriesInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "terse-contended-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(40, TestContext.Current.CancellationToken);
                await holder.DisposeAsync();
            },
            TestContext.Current.CancellationToken);
        try
        {
            await AtomicWrite.TextAsync(path, "after", TestContext.Current.CancellationToken);

            Assert.Equal("after", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            await release;
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TextAsync_OverAReadOnlyFile_RefusesWithWriteDeniedAndLeavesTheFileUntouched()
    {
        var path = Path.Combine(directory.FullName, "readonly.txt");
        await File.WriteAllTextAsync(path, "before", TestContext.Current.CancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var denied = await Assert.ThrowsAsync<WriteDeniedException>(() => AtomicWrite.TextAsync(path, "after", TestContext.Current.CancellationToken));

            Assert.True(denied.ReadOnly);
            Assert.Equal(path, denied.FilePath);
            Assert.Equal("before", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(directory.GetFiles());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task BytesAsync_OverAReadOnlyFile_RefusesWithWriteDeniedAndLeavesTheFileUntouched()
    {
        var path = Path.Combine(directory.FullName, "readonly.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var denied = await Assert.ThrowsAsync<WriteDeniedException>(() => AtomicWrite.BytesAsync(path, [9], TestContext.Current.CancellationToken));

            Assert.True(denied.ReadOnly);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(directory.GetFiles());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
