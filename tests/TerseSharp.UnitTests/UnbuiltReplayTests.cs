using TerseSharp.Server;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class UnbuiltReplayTests
{
    [Fact]
    public void Request_FromAnUnbuiltMemory_ReplaysItsTargetFilterAndTargetsUnderTheRememberedScope()
    {
        var memory = new TestRunMemory("C:\\repo", "C:\\repo\\All.slnx", [], new BuildScope("Release", null, null), Unbuilt: new UnbuiltRun("FullyQualifiedName~Ns.A", ["A.Tests.csproj"]));

        var request = UnbuiltReplay.Request(memory, noBuild: false, TimeSpan.FromSeconds(600), verbose: false, default);

        Assert.Equal("C:\\repo\\All.slnx", request.Target);
        Assert.Equal("FullyQualifiedName~Ns.A", request.Filter);
        Assert.Equal(["A.Tests.csproj"], request.Targets);
        Assert.Equal("Release", request.Scope.Configuration);
        Assert.Equal(TimeSpan.FromSeconds(600), request.Timeout);
    }

    [Fact]
    public void Refused_WithTestsNamed_RefusesNamingWhyAndPassesWithoutThem()
    {
        Assert.Contains("failed to build", UnbuiltReplay.Refused(["Ns.A"], null), StringComparison.Ordinal);
        Assert.Contains("remedy:", UnbuiltReplay.Refused(null, ["Ns.A"]), StringComparison.Ordinal);
        Assert.Null(UnbuiltReplay.Refused(null, []));
    }
}
