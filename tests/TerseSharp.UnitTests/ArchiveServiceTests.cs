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
}
