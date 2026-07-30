using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TerseSharp.Server;

public static class McpHost
{
    public static async Task RunAsync(string? workspace, bool readOnly, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(_ => new ToolContext(new WorkspaceRegistry(), readOnly));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var host = builder.Build();

        await PreloadAsync(host.Services, workspace, cancellationToken).ConfigureAwait(false);
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PreloadAsync(IServiceProvider services, string? workspace, CancellationToken cancellationToken)
    {
        var target = workspace ?? Discovered();

        if (target is null)
            return;

        var context = services.GetRequiredService<ToolContext>();

        await context.Registry.LoadAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static string? Discovered() =>
        WorkspaceDiscovery.Find(Directory.GetCurrentDirectory()) is [var first, ..] ? first : null;
}
