using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TerseSharp.Server;

public static class McpHost
{
    public static async Task RunAsync(string? workspace, bool readOnly, bool watch, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(_ => new ToolContext(new WorkspaceRegistry(watch: watch), readOnly));
        builder.Services.AddSingleton<LastTestRun>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var host = builder.Build();
        var serving = host.RunAsync(cancellationToken);

        Preload(host.Services, workspace, cancellationToken);

        await serving.ConfigureAwait(false);
    }

    private static void Preload(IServiceProvider services, string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();

        if (target is null)
            return;

        services.GetRequiredService<ToolContext>().BeginPreload(target, cancellationToken);
    }

    private static string? Discovered() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;
}
