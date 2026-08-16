using System.Text.Json;
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

    public static string? Describe(bool verbose = false) => Volatile.Read(ref last) is { } reading
        ? string.Create(CultureInfo.InvariantCulture, $"advertised={reading.Tools} tools {reading.Tokens} tokens")
            + (verbose ? "\n" + Breakdown(reading) : string.Empty)
        : null;

    private static string Breakdown(Reading reading) => string.Create(
        CultureInfo.InvariantCulture,
        $"  toolDescriptions={Tokens(reading.Descriptions)} parameterDescriptions={Tokens(reading.Parameters)} schemaFrame={Tokens(reading.Frame)} names={Tokens(reading.Names)}");


    private static int Tokens(int characters) => (characters + 3) / 4;

    private static Reading Measure(IList<Tool> tools)
    {
        var names = 0;
        var descriptions = 0;
        var parameters = 0;
        var frame = 0;

        foreach (var tool in tools)
        {
            var schema = tool.InputSchema.GetRawText();
            var described = Described(tool.InputSchema);

            names += tool.Name.Length;
            descriptions += tool.Description?.Length ?? 0;
            parameters += described;
            frame += schema.Length - described;
        }

        return new Reading(tools.Count, Tokens(names + descriptions + parameters + frame), names, descriptions, parameters, frame);
    }

    private static int Described(JsonElement schema)
    {
        switch (schema.ValueKind)
        {
            case JsonValueKind.Object:
                var total = 0;

                foreach (var property in schema.EnumerateObject())
                {
                    total += property.NameEquals("description") && property.Value.ValueKind is JsonValueKind.String
                        ? property.Value.GetRawText().Length - 2
                        : Described(property.Value);
                }

                return total;

            case JsonValueKind.Array:
                var items = 0;

                foreach (var item in schema.EnumerateArray())
                    items += Described(item);

                return items;

            default:
                return 0;
        }
    }

    private sealed record Reading(int Tools, int Tokens, int Names, int Descriptions, int Parameters, int Frame);
}
