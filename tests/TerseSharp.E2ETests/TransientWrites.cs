namespace TerseSharp.E2ETests;

internal static class TransientWrites
{
    private const int Attempts = 3;

    public static async Task<string> WriteAsync(this TerseServerFixture server, Dictionary<string, object?> arguments)
    {
        var text = string.Empty;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            text = await server.CallAsync("write_text", new(arguments));

            if (!text.StartsWith("ERROR Transient", StringComparison.Ordinal))
                break;

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        return text;
    }
}
