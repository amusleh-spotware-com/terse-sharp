using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ArchiveServiceTests
{
    [Fact]
    public async Task Entry_SplitsAnExistingAbsoluteArchiveAtItsBang()
    {
        var archive = Path.Combine(Path.GetTempPath(), "terse-entry-" + Path.GetRandomFileName() + ".zip");

        await File.WriteAllBytesAsync(archive, [0x50, 0x4B], TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal(
                new ArchiveService.ArchiveEntryPath(Path.GetFullPath(archive), "lib/net10.0/a.xml"),
                ArchiveService.Entry(archive + "!\\lib\\net10.0\\a.xml"));
            Assert.Null(ArchiveService.Entry(archive + ".missing!/a.xml"));
            Assert.Null(ArchiveService.Entry("relative.zip!/a.xml"));
            Assert.Null(ArchiveService.Entry(archive));
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [Fact]
    public async Task Entry_ResolvesARelativeArchiveAgainstTheRootAndSkipsABangInADirectoryName()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-entry-" + Path.GetRandomFileName());
        var archive = Path.Combine(root, "odd!", "pkg.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        await File.WriteAllBytesAsync(archive, [0x50, 0x4B], TestContext.Current.CancellationToken);

        try
        {
            var expected = new ArchiveService.ArchiveEntryPath(archive, "lib/a.xml");

            Assert.Equal(expected, ArchiveService.Entry("odd!/pkg.zip!/lib/a.xml", root));
            Assert.Equal(expected, ArchiveService.Entry(archive + "!\\lib\\a.xml"));
            Assert.Null(ArchiveService.Entry("odd!/pkg.zip!/lib/a.xml"));
            Assert.Null(ArchiveService.Entry("odd!/missing.zip!/lib/a.xml", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_OnAnArchiveHeldWithNoReadSharing_AnswersFileLocked()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows makes FileShare.None a mandatory lock, so only there can a held handle stop a read");
        var archive = Path.Combine(Path.GetTempPath(), "terse-locked-" + Path.GetRandomFileName() + ".zip");
        await File.WriteAllBytesAsync(archive, [0x50, 0x4B], TestContext.Current.CancellationToken);

        try
        {
            await using (new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var listed = await ArchiveService.ListAsync(archive, "**", 100, false, null, false, TestContext.Current.CancellationToken);

                Assert.False(listed.IsOk);
                Assert.Equal(TerseErrorCode.FileLocked, listed.Error!.Code);
            }
        }
        finally
        {
            File.Delete(archive);
        }
    }
}
