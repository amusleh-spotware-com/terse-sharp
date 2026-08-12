using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

internal static class ToolArgumentFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Structured => next => async (request, cancellationToken) =>
{
    if (Unrecognized(request) is { } rejected)
        return Failed(rejected);
    try
    {
        return await next(request, cancellationToken).ConfigureAwait(false);
    }
    catch (ArgumentException exception)
    {
        return Failed(Rejected(request, exception));
    }
    catch (OperationCanceledException)
    {
        return Failed(Errors.Cancelled());
    }
    catch (Exception exception)
    {
        return Failed(Uncoercible(request, exception));
    }
};

    private static TerseError Uncoercible(RequestContext<CallToolRequestParams> request, Exception exception)
    {
        if (exception is not (JsonException or InvalidCastException or FormatException or NotSupportedException))
            return Errors.Internal(exception);

        var schema = Schema(request);

        return Errors.Invalid(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Params?.Name} rejected the call: {exception.GetType().Name}: {exception.Message}") + Located(request, exception),
            Remedy(Required(schema), Accepted(schema)) + ToolExamples.Suffix(request.Params?.Name));
    }
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
            Remedy(required, accepted) + ToolExamples.Suffix(request.Params?.Name));
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

    private static string[] Arrays(JsonElement? schema)
    {
        if (schema is not { } element
            || !element.TryGetProperty("properties", out var properties)
            || properties.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        var names = new List<string>();

        foreach (var property in properties.EnumerateObject())
        {
            if (property.Value.TryGetProperty("items", out _))
                names.Add(property.Name);
        }

        return [.. names];
    }

    private static string Located(RequestContext<CallToolRequestParams> request, Exception exception)
    {
        if (exception is not JsonException json || json.BytePositionInLine is not { } offset || request.Params?.Arguments is not { Count: > 0 } supplied)
            return string.Empty;

        var candidates = new List<string>();

        foreach (var name in Arrays(Schema(request)))
        {
            if (supplied.TryGetValue(name, out var value) && offset < value.GetRawText().Length)
                candidates.Add(name);
        }

        return Attributed(json, supplied, candidates, offset);
    }

    private static string Attributed(
        JsonException json,
        IDictionary<string, JsonElement> supplied,
        List<string> candidates,
        long offset)
    {
        var named = candidates.Find(candidate => json.Path is { Length: > 0 } path && path.Contains(candidate, StringComparison.Ordinal));

        if (named is not null)
            return "\n" + Quoted(named, supplied[named], offset);

        return candidates switch
        {
            [] => string.Empty,
            [var only] => "\n" + Quoted(only, supplied[only], offset),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"\nthe value that failed is one of the array parameters {string.Join(", ", candidates)}, at byte {offset} of its own JSON; the exception does not say which"),
        };
    }

    private static string Quoted(string name, JsonElement value, long offset)
    {
        var raw = value.GetRawText();
        var from = (int)Math.Max(0, offset - Window);
        var to = (int)Math.Min(raw.Length, offset + Window);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name} is an array parameter, and byte {offset} of its {raw.Length} characters falls near: {raw.AsSpan(from, to - from)}");
    }

    private const int Window = 40;

    private static TerseError? Unrecognized(RequestContext<CallToolRequestParams> request)
    {
        var schema = Schema(request);

        return request.Params?.Arguments is not { Count: > 0 } supplied
            ? null
            : Unrecognized(request.Params?.Name, supplied.Keys, () => Required(schema), Accepted(schema));
    }

    public static TerseError? Unrecognized(string? tool, IEnumerable<string> supplied, Func<string[]> required, string[] accepted)
    {
        if (accepted.Length is 0)
            return null;

        var unknown = supplied
            .Where(name => !name.StartsWith('_') && !accepted.Contains(name, StringComparer.Ordinal))
            .ToArray();

        return unknown.Length is 0
            ? null
            : Errors.Invalid(
                tool + " rejected the call: unrecognized " + string.Join(", ", unknown),
                Remedy(required(), accepted) + ToolExamples.Suffix(tool));
    }
}
