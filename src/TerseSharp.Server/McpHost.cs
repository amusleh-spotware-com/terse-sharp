using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TerseSharp.Server;

public static class McpHost
{
    public static async Task RunAsync(
            string? workspace,
            bool readOnly,
            bool watch,
            int maxWorkspaces,
            TimeSpan idleFor,
            string? tools,
            CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        var overrides = await ToolSettings.LoadAsync(Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        var surface = ToolProfile.Resolve(tools) with { Overrides = overrides };
        var context = new ToolContext(new WorkspaceRegistry(maxWorkspaces, watch), readOnly, surface);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(_ => context);
        builder.Services.AddSingleton<LastTestRun>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(ToolArgumentFilter.Structured);
                filters.AddCallToolFilter(RepeatSteer.Filter());
                filters.AddListToolsFilter(AdvertisedCost.Filter());

                if (surface.Advertised is not null || surface.MarkupDerived || overrides.Configured)
                    filters.AddListToolsFilter(ToolProfile.Filter(surface, context));

                filters.AddListToolsFilter(AdvertisedCost.Unnarrowed());
                filters.AddListToolsFilter(SchemaCompactor.Filter());
            });

        var host = builder.Build();

        context.ToolsChanged = token => Announce(host.Services, token);

        if (ToolSettings.Notice(overrides) is { } notice)
            await Console.Error.WriteLineAsync(notice).ConfigureAwait(false);

        if (await ClientRegistrar.ProbeAsync(cancellationToken).ConfigureAwait(false) is { } assets)
            AssetBanner.Publish(assets);

        Preload(host.Services, workspace, cancellationToken);
        BeginMaintenance(cancellationToken);
        BeginSweep(cancellationToken);
        BeginIdleRelease(host.Services, idleFor, cancellationToken);

        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void BeginIdleRelease(IServiceProvider services, TimeSpan idleFor, CancellationToken cancellationToken)
    {
        if (idleFor <= TimeSpan.Zero)
            return;

        var context = services.GetRequiredService<ToolContext>();

        _ = Task.Run(() => ReleaseIdleAsync(context, idleFor, cancellationToken), cancellationToken);
    }

    private static async Task ReleaseIdleAsync(ToolContext context, TimeSpan idleFor, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Sweep(idleFor));

        while (await Ticked(timer, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                context.Registry.DropIdleCompilations(idleFor);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await Console.Error.WriteLineAsync("terse idle release failed: " + exception.Message).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> Ticked(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static TimeSpan Sweep(TimeSpan idleFor) =>
        TimeSpan.FromTicks(Math.Clamp(idleFor.Ticks / 3, TimeSpan.TicksPerMinute, TimeSpan.TicksPerMinute * 5));

    private static void Preload(IServiceProvider services, string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();

        if (target is null)
            return;

        services.GetRequiredService<ToolContext>().BeginPreload(target, cancellationToken);
    }

    private static string? Discovered() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;

    private static async Task MaintainAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await ClientRegistrar.RefreshAsync(cancellationToken).ConfigureAwait(false) is { } refreshed)
                await Console.Error.WriteLineAsync(refreshed).ConfigureAwait(false);

            if (UpdateSettings.Enabled() && UpdateSettings.Requested() is { } request)
                UpdateBanner.Publish(UpdateCheck.Notice(request.Running, await UpdateCheck.RunAsync(request, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("terse maintenance failed: " + exception.Message).ConfigureAwait(false);
        }
    }

    private static void BeginMaintenance(CancellationToken cancellationToken) =>
        _ = Task.Run(() => MaintainAsync(cancellationToken), cancellationToken);

    private static void BeginSweep(CancellationToken cancellationToken) =>
        _ = Task.Run(ShadowCopyAnalyzerLoader.Sweep, cancellationToken);

    private static Task Announce(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<McpServer>()
            .SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken);
}
