namespace TerseSharp.Server;

public static class Doctor
{
    public static async Task<string> RunAsync(string? workspace, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            Check("dotnet SDK", Environment.Version.ToString(), true, "install the .NET 10 SDK"),
            Check("MSBuild", MsBuildBootstrap.Ensure(), true, "install the .NET SDK or Visual Studio Build Tools"),
            ClientLine(),
            await WorkspaceLineAsync(workspace, cancellationToken).ConfigureAwait(false)
        };

        return string.Join("\n", lines);
    }

    private static string ClientLine()
    {
        var registered = ClientRegistrar.Known().Where(target => File.Exists(target.ConfigPath)).ToArray();

        return Check(
            "clients",
            registered.Length is 0 ? "none found" : string.Join(", ", registered.Select(target => target.Name)),
            registered.Length > 0,
            "run: terse install");
    }

    private static async Task<string> WorkspaceLineAsync(string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();

        if (target is null)
            return Check("workspace", "none discovered", false, "run terse from a directory containing a .sln or .csproj");

        using var registry = new WorkspaceRegistry();
        var result = await registry.LoadAsync(target, cancellationToken).ConfigureAwait(false);

        return Check(
            "workspace",
            string.Create(CultureInfo.InvariantCulture, $"{target} projects={result.ProjectCount} elapsedMs={result.ElapsedMilliseconds} failures={result.Failures.Count}"),
            result.ProjectCount > 0,
            "check the solution loads with: dotnet build");
    }

    private static string? Discovered() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;

    private static string Check(string name, string detail, bool ok, string remedy) =>
        ok
            ? string.Create(CultureInfo.InvariantCulture, $"OK   {name}: {detail}")
            : string.Create(CultureInfo.InvariantCulture, $"FAIL {name}: {detail} -> {remedy}");
}
