namespace TerseSharp.Server;

public static class Doctor
{
    public static async Task<string> RunAsync(string? workspace, CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            SdkLine(),
            Check("MSBuild", MsBuildBootstrap.Ensure(), true, "install the .NET SDK or Visual Studio Build Tools"),
            ClientLine(),
            await AssetsLineAsync(cancellationToken).ConfigureAwait(false),
            await UpdateLineAsync(cancellationToken).ConfigureAwait(false),
            WatcherLine(),
            await WorkspaceLineAsync(workspace, cancellationToken).ConfigureAwait(false)
        };

        return string.Join("\n", lines);
    }

    private static string SdkLine()
    {
        var runtime = Environment.Version;

        return Check(
            "dotnet runtime",
            runtime.ToString(),
            runtime.Major >= 10,
            "install the .NET 10 SDK from https://dot.net");
    }

    private static string ClientLine()
    {
        var registered = Described(ClientConfigState.Registered);
        var invalid = Described(ClientConfigState.Invalid);
        var detail = registered.Length is 0 ? "terse-sharp not registered" : string.Join(", ", registered);

        return Check(
            "clients",
            invalid.Length is 0 ? detail : detail + "; invalid JSON in " + string.Join(", ", invalid),
            registered.Length is not 0 && invalid.Length is 0,
            invalid.Length is 0 ? "run: terse install" : "repair the invalid config, then run: terse install");
    }

    private static string[] Described(ClientConfigState state) =>
        [.. ClientRegistrar.Known().Where(target => ClientRegistrar.State(target) == state).Select(Describe)];

    private static string Describe(ClientTarget target) => target.Name + " -> " + target.ConfigPath;

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

    private static string WatcherLine()
    {
        try
        {
            using var probe = new FileSystemWatcher(Path.GetTempPath()) { EnableRaisingEvents = true };

            return Check("watcher", "this platform supports file watching", true, string.Empty);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return Check("watcher", exception.Message, false, "run with --no-watch; freshness then rests on the per-file stamp check");
        }
    }

    private static string UpdateDetail(ReleaseVersion running, ReleaseVersion? latest) => latest switch
    {
        { } published when published.IsNewerThan(running) => string.Create(CultureInfo.InvariantCulture, $"terse {running} -> {published} is available"),
        { } => string.Create(CultureInfo.InvariantCulture, $"terse {running} is current"),
        _ => string.Create(CultureInfo.InvariantCulture, $"terse {running}; the latest release could not be read"),
    };

    private static string Asset(bool installed, bool current) => (installed, current) switch
    {
        (false, _) => "absent",
        (_, true) => "current",
        _ => "stale",
    };

    private static async Task<string> UpdateLineAsync(CancellationToken cancellationToken)
    {
        if (!UpdateSettings.Enabled())
            return Check("update", "checks are off (TERSE_UPDATE=0)", true, string.Empty);

        if (UpdateSettings.Requested() is not { } request)
        {
            return Check(
                "update",
                "the running version could not be read ('" + UpdateSettings.Running() + "')",
                false,
                "reinstall the tool: dotnet tool update -g TerseSharp");
        }

        var latest = await UpdateCheck.RunAsync(request with { Window = TimeSpan.Zero }, cancellationToken).ConfigureAwait(false);

        return Check(
            "update",
            UpdateDetail(request.Running, latest),
            latest is not { } published || !published.IsNewerThan(request.Running),
            "run: dotnet tool update -g TerseSharp");
    }

    private static async Task<string> AssetsLineAsync(CancellationToken cancellationToken)
    {
        var state = await ClientRegistrar.AssetsAsync(cancellationToken).ConfigureAwait(false);

        return Check(
            "assets",
            "skill=" + Asset(state.SkillInstalled, state.SkillCurrent) + " guard=" + Asset(state.GuardInstalled, state.GuardCurrent),
            !state.Stale,
            "run: terse install --skill --guard");
    }
}
