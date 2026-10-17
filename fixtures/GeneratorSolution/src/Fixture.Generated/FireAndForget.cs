using System.Threading.Tasks;

namespace Fixture.Generated;

public static class FireAndForget
{
    public static async Task StartAsync()
    {
        Pending();
        await Task.Yield();
    }

    private static Task Pending() => Task.CompletedTask;
}
