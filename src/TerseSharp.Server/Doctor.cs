using System.Diagnostics;

namespace TerseSharp.Server;

public static class Doctor
{
    public static async Task<string> RunAsync(string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();
        var lines = new List<string>
    {
        VersionLine(),
        SdkLine(),
        Check("MSBuild", MsBuildBootstrap.Ensure(), true, "install the .NET SDK or Visual Studio Build Tools"),
        ClientLine(),
        await AssetsLineAsync(cancellationToken).ConfigureAwait(false),
        await UpdateLineAsync(cancellationToken).ConfigureAwait(false),
        WatcherLine(),
        ProcessLine(),
    };

        lines.AddRange(await InstalledLinesAsync(Probed(target), cancellationToken).ConfigureAwait(false));
        lines.AddRange(await WorkspaceLinesAsync(target, cancellationToken).ConfigureAwait(false));

        return string.Join("\n", lines);
    }
    private static string Probed(string? target)
    {
        if (target is not { Length: > 0 })
            return Directory.GetCurrentDirectory();

        var full = Path.GetFullPath(target);

        if (Directory.Exists(full))
            return full;

        return Path.GetDirectoryName(full) is { Length: > 0 } directory ? directory : Directory.GetCurrentDirectory();
    }

    private static string SdkLine()
    {
        var runtime = Environment.Version;

        return Check(
            "server runtime",
            string.Create(CultureInfo.InvariantCulture, $"{runtime} - the runtime this server process is on, not what the machine offers a build"),
            runtime.Major >= 10,
            "install the .NET 10 SDK from https://dot.net");
    }

    private static async Task<string[]> InstalledLinesAsync(string probeDirectory, CancellationToken cancellationToken)
    {
        var installed = await DotnetRunner.InstalledAsync(probeDirectory, cancellationToken).ConfigureAwait(false);

        return
        [
            Check(
                "dotnet sdks",
                Listed(installed.Sdks) + "; selected in " + probeDirectory + ": " + Selected(installed.Selected),
                installed.Sdks.Length > 0 && installed.Selected.Length > 0,
                "install the .NET SDK from https://dot.net, or relax the global.json pin"),
            Check(
                "dotnet runtimes",
                Listed(installed.Runtimes),
                installed.Runtimes.Length > 0,
                "install the runtime every target framework in the solution needs from https://dot.net"),
        ];
    }

    private static string Selected(string version) =>
        version.Length is 0 ? "none - the SDK the effective global.json pins is missing" : version;


    private static string Listed(string[] values) =>
        values.Length is 0 ? "none reported" : string.Join(", ", values);

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
            !state.NeedsInstall,
            "run: terse install --skill --guard");
    }

    private const long BytesPerMegabyte = 1024 * 1024;
    private static readonly string[] ProcessNames = ["terse", "testhost", "testhost.x86"];

    private static string ProcessLine()
    {
        var live = Live();

        return live.Length is 0
            ? Check(
                "processes",
                "no other terse or testhost process is running - a server started as 'dotnet terse.dll' is not listed",
                true,
                string.Empty)
            : Check(
                "processes",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{live.Length} live: {string.Join(", ", live)} - a stale one holds the built binaries, so a build can silently no-op and a test run can report the previous binary's result; stop them and re-run"),
                true,
                string.Empty);
    }
    private static string[] Live()
    {
        var self = Environment.ProcessId;
        var found = new List<string>();

        foreach (var name in ProcessNames)
        {
            foreach (var process in Processes(name))
            {
                using (process)
                {
                    if (process.Id != self)
                        found.Add(Sketch(name, process));
                }
            }
        }

        return [.. found];
    }

    private static Process[] Processes(string name)
    {
        try
        {
            return Process.GetProcessesByName(name);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            return [];
        }
    }

    private static string Sketch(string name, Process process)
    {
        try
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{name}#{process.Id} {process.WorkingSet64 / BytesPerMegabyte}MB started={process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{name}#{process.Id}");
        }
    }

    private const int LatencyCalls = 20;

    private static async Task<string[]> WorkspaceLinesAsync(string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();

        if (target is null)
            return [Check("workspace", "none discovered", false, "run terse from a directory containing a .sln or .csproj")];

        using var context = new ToolContext(new WorkspaceRegistry(), readOnly: true);
        var result = await context.Registry.LoadAsync(target, cancellationToken).ConfigureAwait(false);
        var loaded = Check(
            "workspace",
            string.Create(CultureInfo.InvariantCulture, $"{target} projects={result.ProjectCount} elapsedMs={result.ElapsedMilliseconds} failures={result.Failures.Count}"),
            result.ProjectCount > 0,
            "check the solution loads with: dotnet build");

        if (result.ProjectCount is 0)
            return [loaded];

        return
        [
            loaded,
        await LatencyLineAsync(context, cancellationToken).ConfigureAwait(false),
        await PhaseLineAsync(context, cancellationToken).ConfigureAwait(false),
    ];
    }

    private static async Task<string> LatencyLineAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var latency = await context.MeasureAsync(LatencyCalls, cancellationToken).ConfigureAwait(false);
        var floor = latency.ResolveMs + latency.SyncMs;

        return Check(
            "latency",
            string.Create(
                CultureInfo.InvariantCulture,
                $"calls={latency.Calls} resolveMs={latency.ResolveMs:F2} syncMs={latency.SyncMs:F2} actionMs={latency.ActionMs:F2}"),
            floor < LatencyFloorMs,
            "a per-call resolve+sync floor of a second or more is a workspace-resolution defect - report this line");
    }

    private static async Task<string> PhaseLineAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var phases = await context.MeasurePhasesAsync(cancellationToken).ConfigureAwait(false);

        if (phases.Document is not { Length: > 0 })
            return Check("phases", "not measured - no C# document was reachable", false, "load a solution whose projects carry source, then re-run");

        return Check(
            "phases",
            string.Create(
                CultureInfo.InvariantCulture,
                $"widest={phases.Document} outlineMs={phases.OutlineMs:F2} gateMs={phases.GateMs:F2} diffMs={phases.DiffMs:F2}"),
            phases.GateMs < PhaseFloorMs,
            "the compile gate, not workspace resolution, is where the per-call time goes - report this line with the solution size");
    }

    private const double PhaseFloorMs = 60_000;
    private const double LatencyFloorMs = 1000;

    private static string VersionLine()
    {
        var version = UpdateSettings.Version();

        return version.Length is 0
            ? Check("version", "the running version could not be read", false, "reinstall the tool: dotnet tool update -g TerseSharp")
            : Check("version", "terse " + version, true, string.Empty);
    }
}
