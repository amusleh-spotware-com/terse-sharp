namespace Fixture.ConfigMapping;

public static class Unawaited
{
    public static async Task Run()
    {
        Task.Delay(1);
        await Task.Yield();
    }
}
