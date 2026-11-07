using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class AnalysisDeadlineTests
{
    [Fact]
    public async Task AnalyzeAsync_WithADeadlineAlreadyPassed_NamesEveryProjectUnanalyzedAndTheCallThatCoversThem()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var report = await AnalyzeAsync(workspace, path: null, includeDeadCode: false, sinceLast: false, TimeSpan.Zero);

        Assert.Contains("\nPARTIAL 0 of ", report, StringComparison.Ordinal);
        Assert.Contains("not analyzed, so absent from this answer: Fixture.Trading", report, StringComparison.Ordinal);
        Assert.Contains("\nnext: analyze paths=[\"", report, StringComparison.Ordinal);
        Assert.Contains("Fixture.Trading\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain("wider than this call's scope", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_ScopedToAFileWithADeadlineAlreadyPassed_NamesThatFileNotItsProjectDirectory()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var report = await AnalyzeAsync(workspace, workspace.Files.OrderServicePath, includeDeadCode: false, sinceLast: false, TimeSpan.Zero);

        Assert.Contains("\nPARTIAL 0 of 1 project(s) analyzed by the 0 s deadline", report, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs\"]", report, StringComparison.Ordinal);
        Assert.DoesNotContain("wider than this call's scope", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_WithDeadCodeAndADeadlineAlreadyPassed_SaysTheDeadCodeScanDidNotFinish()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var report = await AnalyzeAsync(workspace, workspace.Files.OrderServicePath, includeDeadCode: true, sinceLast: false, TimeSpan.Zero);

        Assert.Contains("\nPARTIAL the dead-code scan did not finish by the 0 s deadline, so no TERSE001 finding is reported", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_WithNoDeadline_AnswersTheWholePassWithoutAPartialLine()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var report = await AnalyzeAsync(workspace, workspace.Files.OrderServicePath, includeDeadCode: true, sinceLast: false, timeout: null);

        Assert.Contains("engines=", report, StringComparison.Ordinal);
        Assert.DoesNotContain("PARTIAL", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_APartialPass_IsNotRecordedAsTheSinceLastBaseline()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);

        var partial = await AnalyzeAsync(workspace, workspace.Files.OrderServicePath, includeDeadCode: false, sinceLast: true, TimeSpan.Zero);
        var whole = await AnalyzeAsync(workspace, workspace.Files.OrderServicePath, includeDeadCode: false, sinceLast: true, timeout: null);

        Assert.DoesNotContain("this run is the baseline", partial, StringComparison.Ordinal);
        Assert.Contains("no previous analyze of this scope: this run is the baseline", whole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenTheCallerCancels_ThrowsRatherThanAnsweringPartial()
    {
        using var workspace = await TemporaryWorkspace.OpenAsync(TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AnalysisService.AnalyzeAsync(
            workspace.Workspace, null, DiagnosticSeverity.Info, [], includeDeadCode: false, maxResults: 200, sinceLast: false, changed: false,
            timeout: TimeSpan.FromMinutes(10), cancellationToken: cancelled.Token));
    }

    private static Task<string> AnalyzeAsync(TemporaryWorkspace workspace, string? path, bool includeDeadCode, bool sinceLast, TimeSpan? timeout) =>
        AnalysisService.AnalyzeAsync(
            workspace.Workspace, path, DiagnosticSeverity.Info, [], includeDeadCode: includeDeadCode, maxResults: 200, sinceLast: sinceLast, changed: false,
            timeout: timeout, cancellationToken: TestContext.Current.CancellationToken);
}
