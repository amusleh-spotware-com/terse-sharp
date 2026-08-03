using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

internal static class ToolArgumentFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Structured => next => async (request, cancellationToken) =>
    {
        try
        {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return Failed(Rejected(request, exception));
        }
    };

    private static TerseError Rejected(RequestContext<CallToolRequestParams> request, ArgumentException exception)
    {
        var schema = Schema(request);
        var supplied = request.Params?.Arguments;
        var accepted = Accepted(schema);
        var required = Required(schema);
        var missing = required.Where(name => supplied is null || !supplied.ContainsKey(name)).ToArray();
        var unrecognized = supplied is null
            ? []
            : supplied.Keys.Where(name => !accepted.Contains(name, StringComparer.Ordinal)).ToArray();

        return Errors.Invalid(
            request.Params?.Name + " rejected the call: " + Reason(exception, missing, unrecognized),
            Remedy(required, accepted));
    }

    private static string Reason(ArgumentException exception, string[] missing, string[] unrecognized) =>
        Detail(missing, unrecognized) is { Length: > 0 } detail ? detail : exception.Message;

    private static string Detail(string[] missing, string[] unrecognized) => (missing, unrecognized) switch
    {
        ([], []) => string.Empty,
        (_, []) => "missing " + string.Join(", ", missing),
        ([], _) => "unrecognized " + string.Join(", ", unrecognized),
        _ => "missing " + string.Join(", ", missing) + "; unrecognized " + string.Join(", ", unrecognized),
    };

    private static string Remedy(string[] required, string[] accepted) => accepted.Length is 0
        ? "call tools/list and pass exactly the parameters this tool declares"
        : "required: " + Listed(required) + "; accepted: " + string.Join(", ", accepted);

    private static string Listed(string[] names) => names.Length is 0 ? "none" : string.Join(", ", names);

    private static CallToolResult Failed(TerseError error) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = error.Render() }],
    };

    private static JsonElement? Schema(RequestContext<CallToolRequestParams> request) =>
        request.MatchedPrimitive is McpServerTool tool ? tool.ProtocolTool.InputSchema : null;

    private static string[] Required(JsonElement? schema)
    {
        if (schema is not { } element
            || !element.TryGetProperty("required", out var required)
            || required.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();

        foreach (var item in required.EnumerateArray())
        {
            if (item.GetString() is { Length: > 0 } name)
                names.Add(name);
        }

        return [.. names];
    }

    private static string[] Accepted(JsonElement? schema)
    {
        if (schema is not { } element
            || !element.TryGetProperty("properties", out var properties)
            || properties.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        var names = new List<string>();

        foreach (var property in properties.EnumerateObject())
            names.Add(property.Name);

        return [.. names];
    }
}
