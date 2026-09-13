using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolServicesTests
{
    [Fact]
    public void EveryToolType_IsConstructibleFromTheProbeRegistrations()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => new ToolContext(new WorkspaceRegistry(1, watch: false), readOnly: false, ToolProfile.Resolve("all", null)));
        services.AddToolSingletons();

        using var provider = services.BuildServiceProvider();

        var toolTypes = typeof(ToolContext).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToArray();

        Assert.NotEmpty(toolTypes);
        Assert.All(toolTypes, type => Assert.NotNull(ActivatorUtilities.CreateInstance(provider, type)));
    }
}
