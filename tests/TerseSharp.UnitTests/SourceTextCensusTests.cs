using System.Globalization;

namespace TerseSharp.UnitTests;

public sealed class SourceTextCensusTests
{
    [Fact]
    public async Task EveryCheckedInSourceFile_HoldsNoControlCharacterThatWouldMakeItBinary()
    {
        var offenders = new List<string>();
        var examined = 0;

        foreach (var file in Sources())
        {
            examined++;

            var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            var offset = Control(text);

            if (offset >= 0)
                offenders.Add(Path.GetRelativePath(Fixtures.RepositoryRoot, file) + " at offset " + offset.ToString(CultureInfo.InvariantCulture));
        }

        Assert.True(examined >= 100, string.Create(CultureInfo.InvariantCulture, $"the census found only {examined} source files"));
        Assert.True(offenders.Count is 0, "source files carrying a control character: " + string.Join(", ", offenders));
    }

    private static int Control(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsControl(text[index]) && text[index] is not ('\t' or '\r' or '\n'))
                return index;
        }

        return -1;
    }

    private static IEnumerable<string> Sources()
    {
        foreach (var pattern in new[] { "*.cs", "*.md", "*.csproj", "*.props", "*.targets" })
        {
            foreach (var directory in new[] { "src", "tests" })
            {
                var root = Path.Combine(Fixtures.RepositoryRoot, directory);

                foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                {
                    if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    {
                        yield return file;
                    }
                }
            }
        }
    }
}
