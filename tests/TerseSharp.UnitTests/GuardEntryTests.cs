using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class GuardEntryTests
{
    private const string PostBatch = "--post-batch";
    private const string FolderRules = "--folder-rules";

    public static TheoryData<string[]> HookFlagShapes =>
    [
        Array.Empty<string>(),
        new[] { PostBatch },
        new[] { FolderRules },
        new[] { PostBatch, FolderRules },
        new[] { FolderRules, PostBatch },
    ];

    public static TheoryData<string[]> CommandLineShapes =>
    [
        Array.Empty<string>(),
        new[] { "serve" },
        new[] { "Guard" },
        new[] { "guard", "--help" },
        new[] { "guard", "-h" },
        new[] { "guard", "--post-batch=true" },
        new[] { "guard", "--post-batch", "true" },
        new[] { "guard", "--post-batch", "--post-batch" },
        new[] { "guard", "--folder-rules", "--folder-rules" },
        new[] { "guard", "--no-such-flag" },
        new[] { "@guard.rsp" },
        new[] { "[diagram]", "guard" },
    ];

    [Theory]
    [MemberData(nameof(HookFlagShapes))]
    public void Route_ForEveryShapeOfTheTwoHookFlags_PicksTheHookTheCommandLineActionPicks(string[] flags) =>
        Assert.Equal(
            GuardEntry.Hook(flags.Contains(PostBatch), flags.Contains(FolderRules)),
            GuardEntry.Route(["guard", .. flags]));

    [Theory]
    [MemberData(nameof(CommandLineShapes))]
    public void Route_ForAnyOtherShape_LeavesItToTheCommandLine(string[] arguments) =>
        Assert.Null(GuardEntry.Route(arguments));

    [Fact]
    public async Task RunAsync_ForMalformedInputOnThePreToolUseHook_AnswersTheGuardsAllowAndZero()
    {
        var output = new StringWriter();

        var exitCode = await GuardEntry.RunAsync(GuardHook.PreToolUse, new StringReader("not json"), output, new StringWriter(), TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal("{}", output.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_WhenTheHookThrows_WritesTheCommandLinesUnhandledExceptionLineAndReturnsOne()
    {
        var error = new StringWriter();

        var exitCode = await GuardEntry.RunAsync(GuardHook.PreToolUse, new ThrowingReader(), new StringWriter(), error, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Unhandled exception: System.InvalidOperationException", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class ThrowingReader : TextReader
    {
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("stdin failed");
    }
}
