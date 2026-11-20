using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

internal static class WriteSerializer
{
    private const string Undo = "undo_last_change";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter => next => async (request, cancellationToken) =>
    {
        if (!Writes(request))
            return await next(request, cancellationToken).ConfigureAwait(false);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await next(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    };

    internal static bool Writes(string? tool, JsonElement? schema, IDictionary<string, JsonElement>? arguments) =>
        tool is Undo || (DeclaresDryRun(schema) && !DryRun(arguments));

    private static bool Writes(RequestContext<CallToolRequestParams> request) =>
        Writes(request.Params?.Name, request.MatchedPrimitive is McpServerTool tool ? tool.ProtocolTool.InputSchema : null, request.Params?.Arguments);

    private static bool DeclaresDryRun(JsonElement? schema) =>
        schema is { } element && element.TryGetProperty("properties", out var properties) && DeclaresDryRun(properties);

    private static bool DeclaresDryRun(JsonElement properties) =>
        properties.ValueKind is JsonValueKind.Object && properties.TryGetProperty("dryRun", out _);

    private static bool DryRun(IDictionary<string, JsonElement>? arguments) =>
        arguments is not null && arguments.TryGetValue("dryRun", out var value) && value.ValueKind is JsonValueKind.True;
}
