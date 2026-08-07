namespace TerseSharp.Server;

public readonly record struct BuildScope(string? Configuration, string? TargetFramework, IReadOnlyList<string>? Properties = null)
{
    public bool IsDefault => Configuration is not { Length: > 0 }
        && TargetFramework is not { Length: > 0 }
        && Properties is not { Count: > 0 };

    public string[] Applied(IReadOnlyList<string> arguments)
    {
        var properties = Properties ?? [];
        var scoped = new List<string>(arguments.Count + 4 + properties.Count);

        scoped.AddRange(arguments);

        if (Configuration is { Length: > 0 } configuration)
            scoped.AddRange(["-c", configuration]);

        if (TargetFramework is { Length: > 0 } framework)
            scoped.AddRange(["-f", framework]);

        foreach (var property in properties)
            scoped.Add("-p:" + property);

        return [.. scoped];
    }
}
