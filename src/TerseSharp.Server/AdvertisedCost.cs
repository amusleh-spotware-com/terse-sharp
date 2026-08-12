using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class AdvertisedCost
{
    private static Reading? last;

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Filter() =>
        next => async (request, cancellationToken) =>
        {
            var listed = await next(request, cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref last, Measure(listed.Tools));

            return listed;
        };

    public static string? Describe() => Volatile.Read(ref last) is { } reading
        ? string.Create(CultureInfo.InvariantCulture, $"advertised={reading.Tools} tools {reading.Tokens} tokens")
        : null;

    private static Reading Measure(IList<Tool> tools)
    {
        var characters = 0;

        foreach (var tool in tools)
            characters += tool.Name.Length + (tool.Description?.Length ?? 0) + tool.InputSchema.GetRawText().Length;

        return new Reading(tools.Count, (characters + 3) / 4);
    }

    private sealed record Reading(int Tools, int Tokens);
}
