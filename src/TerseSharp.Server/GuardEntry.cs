namespace TerseSharp.Server;

public enum GuardHook
{
    PreToolUse,
    PostBatch,
    FolderRules,
}

public static class GuardEntry
{
    private const string Verb = "guard";
    private const string PostBatchFlag = "--post-batch";
    private const string FolderRulesFlag = "--folder-rules";
    private const string UnhandledHeader = "Unhandled exception: ";

    public static GuardHook? Route(ReadOnlySpan<string> arguments) => arguments switch
    {
        [Verb] => GuardHook.PreToolUse,
        [Verb, PostBatchFlag] or [Verb, PostBatchFlag, FolderRulesFlag] or [Verb, FolderRulesFlag, PostBatchFlag] => GuardHook.PostBatch,
        [Verb, FolderRulesFlag] => GuardHook.FolderRules,
        _ => null,
    };

    public static GuardHook Hook(bool postBatch, bool folderRules) => (postBatch, folderRules) switch
    {
        (true, _) => GuardHook.PostBatch,
        (_, true) => GuardHook.FolderRules,
        _ => GuardHook.PreToolUse,
    };

    public static Task<int> DispatchAsync(GuardHook hook, TextReader input, TextWriter output, CancellationToken cancellationToken) => hook switch
    {
        GuardHook.PostBatch => BatchNudge.RunAsync(input, output, cancellationToken),
        GuardHook.FolderRules => FolderRules.RunAsync(input, output, cancellationToken),
        _ => ToolGuard.RunAsync(input, output, cancellationToken),
    };

    public static async Task<int> RunAsync(GuardHook hook, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            return await DispatchAsync(hook, input, output, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await error.WriteLineAsync(string.Concat(UnhandledHeader, exception.ToString())).ConfigureAwait(false);

            return 1;
        }
    }
}
