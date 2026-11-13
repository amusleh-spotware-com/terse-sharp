namespace TerseSharp.E2ETests;

internal static class TransientWrites
{
    private const int Attempts = 3;

    public static async Task<string> WriteAsync(this TerseServerFixture server, Dictionary<string, object?> arguments)
    {
        var text = string.Empty;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            text = await server.CallAsync("write_text", [with(arguments)]);

            if (!text.StartsWith("ERROR Transient", StringComparison.Ordinal))
                break;

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        return text;
    }

    public static async Task<string> WriteNewAsync(this TerseServerFixture server, Dictionary<string, object?> arguments)
    {
        var text = string.Empty;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            text = await server.CallAsync("write_text", [with(arguments)]);

            if (!text.StartsWith("ERROR Transient", StringComparison.Ordinal))
                break;

            foreach (var path in Created(arguments))
                await server.CallAsync("write_text", new() { ["path"] = path, ["delete"] = true, ["force"] = true });

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        return text;
    }

    private static IEnumerable<object?> Created(Dictionary<string, object?> arguments) =>
        arguments.TryGetValue("files", out var files) && files is System.Collections.IEnumerable entries
            ? entries.OfType<System.Collections.IDictionary>().Select(entry => entry["path"])
            : [arguments["path"]];
}
