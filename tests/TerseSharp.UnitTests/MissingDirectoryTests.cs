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
}
