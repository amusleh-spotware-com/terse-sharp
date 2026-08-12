using System.Globalization;
using Xunit;

namespace Fixture.Trading.Tests;

public sealed class DeliberateOutcomesTests
{
    static DeliberateOutcomesTests() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Note("fixture-host-exit-marker");

    private static void Note(string line)
    {
        var directory = Environment.GetEnvironmentVariable("TERSE_RESULTS_DIRECTORY");

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            Console.Error.WriteLine(line);

            return;
        }

        var name = "terse-notes-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".txt";

        try
        {
            File.WriteAllText(Path.Combine(directory, name), line);
        }
        catch (IOException)
        {
            Console.Error.WriteLine(line);
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine(line);
        }
    }

    [Fact]
    public void Succeeds()
    {
        var repository = new InMemoryOrderRepository();

        Assert.Equal(0, repository.PendingCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PassesWithData(int volume)
    {
        Assert.True(volume > 0);
    }

    [Fact]
    public void FailsAssertion()
    {
        Assert.Equal(4, 2 + 3);
    }

    [Theory]
    [InlineData(0)]
    public void FailsWithData(int volume)
    {
        Assert.True(volume > 0);
    }

    [Fact]
    public void Throws()
    {
        throw new InvalidOperationException("probe boom");
    }

    [Fact(Skip = "deliberately skipped so the fixture exercises the skipped counter")]
    public void SkippedByDesign()
    {
        Assert.Fail("never runs");
    }
}
