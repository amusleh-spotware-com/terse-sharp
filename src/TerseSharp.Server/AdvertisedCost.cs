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
                + (verbose ? OfWhole(reading) + "\n" + Breakdown(reading) : string.Empty)
            : null;

    private static string Breakdown(Reading reading) => string.Create(
            CultureInfo.InvariantCulture,
            $"  toolDescriptions={Tokens(reading.Descriptions)} parameterDescriptions={Tokens(reading.Parameters)} schemaFrame={Tokens(reading.Frame)} names={Tokens(reading.Names)}")
        + Ranked(reading.Worst);

    private static int Tokens(int characters) => (characters + 3) / 4;

    private static Reading Measure(IList<Tool> tools)
    {
        var names = 0;
        var descriptions = 0;
        var parameters = 0;
        var frame = 0;
        var costs = new List<ToolCost>(tools.Count);

        foreach (var tool in tools)
        {
            var schema = tool.InputSchema.GetRawText();
            var described = Described(tool.InputSchema);

            names += tool.Name.Length;
            descriptions += tool.Description?.Length ?? 0;
            parameters += described;
            frame += schema.Length - described;
            costs.Add(new ToolCost(tool.Name, described));
        }

        return new Reading(tools.Count, Tokens(names + descriptions + parameters + frame), names, descriptions, parameters, frame, Costliest(costs));
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

    private sealed record Reading(int Tools, int Tokens, int Names, int Descriptions, int Parameters, int Frame, IReadOnlyList<ToolCost> Worst);

    private static Reading? unnarrowed;

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Unnarrowed() =>
            next => async (request, cancellationToken) =>
            {
                var listed = await next(request, cancellationToken).ConfigureAwait(false);

                Volatile.Write(ref unnarrowed, Measure(listed.Tools));

                return listed;
            };

    private static string OfWhole(Reading reading) => Volatile.Read(ref unnarrowed) is { } full && full.Tools > reading.Tools
            ? string.Create(CultureInfo.InvariantCulture, $"  surface={full.Tools} tools {full.Tokens} tokens")
            : string.Empty;

    public static void Observe(IList<Tool> advertised, IList<Tool> whole)
    {
        Volatile.Write(ref unnarrowed, Measure(whole));
        Volatile.Write(ref last, Measure(advertised));
    }

    private const int MaxCostliest = 10;

    public readonly record struct ToolCost(string Name, int Parameters);

    private static List<ToolCost> Costliest(List<ToolCost> costs)
    {
        costs.Sort(static (left, right) => right.Parameters.CompareTo(left.Parameters));

        return costs.Count > MaxCostliest ? costs.GetRange(0, MaxCostliest) : costs;
    }

    private static string Ranked(IReadOnlyList<ToolCost> worst)
    {
        if (worst.Count is 0)
            return string.Empty;

        var parts = new string[worst.Count];

        for (var index = 0; index < worst.Count; index++)
            parts[index] = string.Create(CultureInfo.InvariantCulture, $"{worst[index].Name}={Tokens(worst[index].Parameters)}");

        return "\n  parameterDescriptions, costliest first: " + string.Join(' ', parts);
    }
}
