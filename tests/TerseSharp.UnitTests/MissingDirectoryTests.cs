using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MissingDirectoryTests
{
    [Fact]
    public void Refused_ADirectoryTwoLevelsBelowAnExistingOne_NamesThatOneAndListsIt()
    {
        var existing = Directory.CreateTempSubdirectory("terse-missing-dir-").FullName;

        try
        {
            var error = MissingDirectory.Refused(Path.Combine(existing, "a", "b"));

            Assert.Equal(TerseErrorCode.DocumentNotFound, error.Code);
            Assert.Equal("directory '" + Path.Combine(existing, "a", "b") + "' does not exist - the nearest existing ancestor is '" + existing + "'", error.Message);
            Assert.StartsWith("list find_files root=\"" + existing + "\"", error.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(existing);
        }
    }

    [Fact]
    public void Refused_AnExistingFile_SaysItIsAFileRatherThanAMissingDirectory()
    {
        var file = Path.GetTempFileName();

        try
        {
            var error = MissingDirectory.Refused(file);

            Assert.Equal(TerseErrorCode.DocumentNotFound, error.Code);
            Assert.Equal("'" + file + "' is a file, not a directory", error.Message);
            Assert.EndsWith("read it with read_text path=\"" + file + "\"", error.Remedy, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
