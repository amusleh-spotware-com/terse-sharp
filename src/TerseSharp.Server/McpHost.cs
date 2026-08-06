using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TerseSharp.Server;

public static class McpHost
{
    public static async Task RunAsync(
        string? workspace,
        bool readOnly,
        bool watch,
        int maxWorkspaces,
        TimeSpan idleFor,
        CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(_ => new ToolContext(new WorkspaceRegistry(maxWorkspaces, watch), readOnly));
        builder.Services.AddSingleton<LastTestRun>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolArgumentFilter.Structured));

        var host = builder.Build();

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

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                context.Registry.DropIdleCompilations(idleFor);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static TimeSpan Sweep(TimeSpan idleFor) =>
        TimeSpan.FromTicks(Math.Max(idleFor.Ticks / 3, TimeSpan.TicksPerMinute));

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

            if (UpdateSettings.Requested() is { } request)
                UpdateBanner.Publish(UpdateCheck.Notice(request.Running, await UpdateCheck.RunAsync(request, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("terse maintenance failed: " + exception.Message).ConfigureAwait(false);
        }
    }

    private static void BeginMaintenance(CancellationToken cancellationToken)
    {
        if (UpdateSettings.Enabled())
            _ = Task.Run(() => MaintainAsync(cancellationToken), cancellationToken);
    }

    private static void BeginSweep(CancellationToken cancellationToken) =>
        _ = Task.Run(ShadowCopyAnalyzerLoader.Sweep, cancellationToken);
}
