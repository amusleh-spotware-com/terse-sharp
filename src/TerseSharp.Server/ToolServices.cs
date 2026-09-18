using Microsoft.Extensions.DependencyInjection;

namespace TerseSharp.Server;

public static class ToolServices
{
    public static IServiceCollection AddToolSingletons(this IServiceCollection services) => services
        .AddSingleton<LastTestRun>()
        .AddSingleton<UnchangedRun>()
        .AddSingleton<ListingMemo>()
        .AddSingleton<ReplayGate>();
}
