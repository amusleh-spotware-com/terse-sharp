namespace TerseSharp.Server;

public readonly record struct BuildScope(string? Configuration, string? TargetFramework)
{
    public bool IsDefault => Configuration is not { Length: > 0 } && TargetFramework is not { Length: > 0 };

    public string[] Applied(IReadOnlyList<string> arguments)
    {
        var scoped = new List<string>(arguments.Count + 4);

        scoped.AddRange(arguments);

        if (Configuration is { Length: > 0 } configuration)
            scoped.AddRange(["-c", configuration]);

        if (TargetFramework is { Length: > 0 } framework)
            scoped.AddRange(["-f", framework]);

        return [.. scoped];
    }
}
