using System.Text;
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
            var whole = tool.Name.Length + (tool.Description?.Length ?? 0) + schema.Length;
            var named = Named(tool.InputSchema);

            names += tool.Name.Length;
            descriptions += tool.Description?.Length ?? 0;
            parameters += described;
            frame += schema.Length - described;
            costs.Add(new ToolCost(tool.Name, described, whole, tool.Name.Length + Undecorated(tool.Description) + named, schema.Length - named));
        }

        var every = Ordered(costs);

        return new Reading(tools.Count, Tokens(names + descriptions + parameters + frame), names, descriptions, parameters, frame, Costliest(costs), every);
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

    private sealed record Reading(int Tools, int Tokens, int Names, int Descriptions, int Parameters, int Frame, IReadOnlyList<ToolCost> Worst, IReadOnlyList<ToolCost> Every);

    private static Reading? unnarrowed;

    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Unnarrowed() =>
            next => async (request, cancellationToken) =>
            {
                var listed = await next(request, cancellationToken).ConfigureAwait(false);

                Volatile.Write(ref unnarrowed, Measure(listed.Tools));
                ToolSchemaEstimate.Publish(Installed, ToolExamples.DecorationLength);

                return listed;
            };

    private static string OfWhole(Reading reading) => Volatile.Read(ref unnarrowed) is { } full && full.Tools > reading.Tools
            ? string.Create(CultureInfo.InvariantCulture, $"  surface={full.Tools} tools {full.Tokens} tokens")
            : string.Empty;

    public static void Observe(IList<Tool> advertised, IList<Tool> whole)
    {
        Volatile.Write(ref unnarrowed, Measure(whole));
        Volatile.Write(ref last, Measure(advertised));
        ToolSchemaEstimate.Publish(Installed, ToolExamples.DecorationLength);
    }

    private const int MaxCostliest = 10;

    public readonly record struct ToolCost(string Name, int Parameters, int Total = 0, int Basis = 0, int Frame = 0);

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

    private static List<ToolCost> Ordered(List<ToolCost> costs)
    {
        var every = new List<ToolCost>(costs);

        every.Sort(static (left, right) => right.Total.CompareTo(left.Total));

        return every;
    }

    public static string? PerTool()
    {
        if (Volatile.Read(ref last) is not { Every.Count: > 0 } reading)
            return null;

        var builder = new StringBuilder(reading.Every.Count * 24);

        builder.Append(CultureInfo.InvariantCulture, $"perTool={reading.Every.Count} advertised, schema tokens descending");

        foreach (var cost in reading.Every)
            builder.Append(CultureInfo.InvariantCulture, $"\n  {cost.Name} {Tokens(cost.Total)}");

        return builder.ToString();
    }

    private static int Named(JsonElement schema)
    {
        if (schema.ValueKind is not JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind is not JsonValueKind.Object)
            return 0;

        var total = 0;

        foreach (var property in properties.EnumerateObject())
            total += property.Name.Length + (property.Value.ValueKind is JsonValueKind.Object && property.Value.TryGetProperty("description", out var text) && text.ValueKind is JsonValueKind.String ? text.GetString()!.Length : 0);

        return total;
    }

    private static int Undecorated(string? description) => description switch
    {
        null => 0,
        _ when description.AsSpan().IndexOf(ToolExamples.Separator, StringComparison.Ordinal) is var at and >= 0 => at,
        _ => description.Length,
    };

    public static async Task<string?> EstimatedAsync(Microsoft.CodeAnalysis.Solution solution, CancellationToken cancellationToken)
    {
        var declared = await ToolSchemaSource.DeclaredAsync(solution, cancellationToken).ConfigureAwait(false);

        return declared.Count is 0 ? null : ToolSchemaEstimate.Render(declared, Installed(), ToolExamples.DecorationLength);
    }

    private static Dictionary<string, InstalledSchema> Installed()
    {
        var every = Volatile.Read(ref unnarrowed)?.Every ?? [];
        var installed = new Dictionary<string, InstalledSchema>(every.Count, StringComparer.Ordinal);

        foreach (var cost in every)
            installed[cost.Name] = new InstalledSchema(cost.Basis, cost.Frame);

        return installed;
    }
}
