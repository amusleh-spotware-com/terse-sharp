using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class FormatServiceTests
{
    [Fact]
    public async Task Cleanup_OverTheWholeSolution_NeverRewritesGeneratedCode()
    {
        var text = await RunAsync(null, FixMode.Usings);

        Assert.DoesNotContain(".g.cs", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Format_WithAnEmptyPath_RefusesRatherThanTargetingEverything()
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            string.Empty,
            new FixRequest(FixMode.None, [], DiagnosticSeverity.Info, Verify: false),
            new EditOptions("format", DryRun: false, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsOk);
        Assert.Equal(TerseErrorCode.DocumentNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Cleanup_WithAGlob_TargetsOnlyTheMatchingDocuments()
    {
        var text = await RunAsync("src/Fixture.Trading/Style*.cs", FixMode.Style);

        Assert.Contains("StyleSample.cs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Format_WithADirectory_TargetsEveryDocumentUnderIt()
    {
        var text = await RunAsync("src/Fixture.Trading/Views", FixMode.None);

        Assert.Contains("format verify", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    private static async Task<string> RunAsync(string? path, FixMode mode)
    {
        using var registry = new WorkspaceRegistry();

        await registry.LoadAsync(Fixtures.SolutionPath, TestContext.Current.CancellationToken);

        using var lease = registry.Resolve(null, null).Value!;

        var result = await FormatService.RunAsync(
            lease.Workspace,
            path,
            new FixRequest(mode, [], DiagnosticSeverity.Info, Verify: mode is FixMode.None),
            new EditOptions(mode is FixMode.None ? "format" : "cleanup", DryRun: true, AllowErrors: false),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsOk, result.Error?.Message);

        return result.Value!;
    }
}
