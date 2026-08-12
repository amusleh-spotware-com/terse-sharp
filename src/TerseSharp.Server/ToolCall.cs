using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

internal static class ToolCall
{
    public static async Task<int> RunAsync(
        string tool,
        string? workspace,
        string? json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CalledAsync(tool, workspace, json, output, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await output.WriteLineAsync(Errors.Internal(Unwrapped(exception)).Render()).ConfigureAwait(false);

            return 1;
        }
    }

    private static Exception Unwrapped(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;

    private static async Task<int> CalledAsync(
        string tool,
        string? workspace,
        string? json,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var methods = Methods();

        if (!methods.TryGetValue(tool, out var method))
        {
            await output.WriteLineAsync(Errors.Invalid(
                "no tool is named '" + tool + "'",
                "run terse call --help, or pick one of: " + string.Join(", ", methods.Keys.Order(StringComparer.Ordinal).Take(12)) + ", ...").Render()).ConfigureAwait(false);

            return 1;
        }

        var services = new ServiceCollection();
        services.AddSingleton(_ => new ToolContext(new WorkspaceRegistry(1, watch: false), readOnly: false));
        services.AddSingleton<LastTestRun>();

        await using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<ToolContext>();

        if (workspace is { Length: > 0 })
            await context.Registry.LoadAsync(workspace, cancellationToken).ConfigureAwait(false);

        var arguments = Parsed(json);

        if (!arguments.IsOk)
        {
            await output.WriteLineAsync(arguments.Error!.Render()).ConfigureAwait(false);

            return 1;
        }

        var bound = Bind(method, arguments.Value!, cancellationToken);

        if (!bound.IsOk)
        {
            await output.WriteLineAsync(bound.Error!.Render()).ConfigureAwait(false);

            return 1;
        }

        var instance = ActivatorUtilities.CreateInstance(provider, method.DeclaringType!);

        await output.WriteLineAsync(await Text(method.Invoke(instance, bound.Value!)).ConfigureAwait(false)).ConfigureAwait(false);

        return 0;
    }

    private static Result<JsonObject> Parsed(string? json)
    {
        if (json is not { Length: > 0 })
            return Result.Ok(new JsonObject());

        try
        {
            return JsonNode.Parse(json) is JsonObject parsed
                ? Result.Ok(parsed)
                : Result.Fail<JsonObject>(Errors.Invalid("--json is not a JSON object", "pass an object such as --json '{\"path\": \"src/App.cs\"}'"));
        }
        catch (JsonException exception)
        {
            return Result.Fail<JsonObject>(Errors.Invalid("--json did not parse: " + exception.Message, "quote the whole object, and escape it for your shell"));
        }
    }

    private static Result<object?[]> Bind(MethodInfo method, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (Unrecognized(method, arguments) is { } unknown)
            return Result.Fail<object?[]>(unknown);

        var parameters = method.GetParameters();
        var bound = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                bound[index] = cancellationToken;
                continue;
            }

            if (arguments[parameter.Name!] is { } value)
            {
                var converted = Converted(value, parameter);

                if (!converted.IsOk)
                    return Result.Fail<object?[]>(converted.Error!);

                bound[index] = converted.Value;
                continue;
            }

            if (!parameter.HasDefaultValue)
            {
                return Result.Fail<object?[]>(Errors.Invalid(
                    "missing required argument '" + parameter.Name + "'",
                    "add it to --json, e.g. --json '{\"" + parameter.Name + "\": \"...\"}'"));
            }

            bound[index] = parameter.DefaultValue;
        }

        return Result.Ok(bound);
    }

    private static Result<object?> Converted(JsonNode value, ParameterInfo parameter)
    {
        try
        {
            return Result.Ok(value.Deserialize(parameter.ParameterType, CaseInsensitive));
        }
        catch (JsonException exception)
        {
            return Result.Fail<object?>(Errors.Invalid(
                "argument '" + parameter.Name + "' is not a " + parameter.ParameterType.Name + ": " + exception.Message,
                "check the type of that argument"));
        }
    }

    private static readonly JsonSerializerOptions CaseInsensitive = new(JsonSerializerDefaults.Web);

    private static async Task<string> Text(object? result) => result switch
    {
        Task<string> pending => await pending.ConfigureAwait(false),
        string text => text,
        null => string.Empty,
        _ => result.ToString() ?? string.Empty,
    };

    private static Dictionary<string, MethodInfo> Methods()
    {
        var methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        foreach (var type in typeof(ToolCall).Assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { Length: > 0 } name)
                    methods[name] = method;
            }
        }

        return methods;
    }

    private static TerseError? Unrecognized(MethodInfo method, JsonObject arguments)
    {
        var declared = method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();

        return ToolArgumentFilter.Unrecognized(
            method.GetCustomAttribute<McpServerToolAttribute>()?.Name,
            arguments.Select(argument => argument.Key),
            () => [.. declared.Where(parameter => !parameter.HasDefaultValue).Select(parameter => parameter.Name!)],
            [.. declared.Select(parameter => parameter.Name!)]);
    }
}
