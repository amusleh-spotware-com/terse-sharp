using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class HangSequenceTests
{
    [Fact]
    public async Task ActiveAsync_WithABlameSequenceFile_NamesTheTestThatWasStillRunning()
    {
        var results = await WrittenAsync(
            "run_Sequence.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <TestSequence>
              <Test Name="Hang.Tests.FastTests.Finishes" Source="Hang.Tests.dll" />
              <Test Name="Hang.Tests.HangingTests.NeverFinishes" Source="Hang.Tests.dll" />
            </TestSequence>
            """);

        try
        {
            Assert.Equal(["Hang.Tests.HangingTests.NeverFinishes"], await HangSequence.ActiveAsync(results, TestContext.Current.CancellationToken));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ActiveAsync_WithNoSequenceFile_NamesNothingInsteadOfGuessing()
    {
        var results = Directory.CreateTempSubdirectory("terse-hang-");

        try
        {
            Assert.Empty(await HangSequence.ActiveAsync(results, TestContext.Current.CancellationToken));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ActiveAsync_WithAnUnreadableSequenceFile_AnswersNothingInsteadOfThrowing()
    {
        var results = await WrittenAsync("broken_Sequence.xml", "<TestSequence><Test Name=\"Unclosed\"");

        try
        {
            Assert.Empty(await HangSequence.ActiveAsync(results, TestContext.Current.CancellationToken));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ActiveAsync_WithASequenceFileInANestedSlot_StillFindsIt()
    {
        var results = await WrittenAsync(
            Path.Combine("slot-1", "attachments", "x_Sequence.xml"),
            "<TestSequence><Test Name=\"Nested.Tests.Stuck\" Source=\"Nested.dll\" /></TestSequence>");

        try
        {
            Assert.Equal(["Nested.Tests.Stuck"], await HangSequence.ActiveAsync(results, TestContext.Current.CancellationToken));
        }
        finally
        {
            results.Delete(recursive: true);
        }
    }

    private static async Task<DirectoryInfo> WrittenAsync(string relativePath, string content)
    {
        var results = Directory.CreateTempSubdirectory("terse-hang-");
        var file = new FileInfo(Path.Combine(results.FullName, relativePath));

        file.Directory!.Create();
        await File.WriteAllTextAsync(file.FullName, content, TestContext.Current.CancellationToken);

        return results;
    }
}
