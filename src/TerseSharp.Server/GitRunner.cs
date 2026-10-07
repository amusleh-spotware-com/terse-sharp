namespace TerseSharp.Server;

internal static class GitRunner
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    public static async Task<Result<string>> ReadAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var run = await ChildProcess.RunAsync("git", arguments, workingDirectory, Deadline, cancellationToken, Unattended, Utf8).ConfigureAwait(false);

        return Answer(run);
    }

    internal static Result<string> Answer(ProcessRun run) => run switch
    {
        { TimedOut: true } => Result.Fail<string>(Errors.Timeout("git did not answer within 60 s and was killed - the arguments were valid, the walk was too long", "narrow the walk, not the argument: path= limits it to one path and baseRef= to a range such as HEAD~200..HEAD; a history contains= pickaxe diffs every commit it visits, so it needs both")),
        { Stopped: true } => Result.Fail<string>(Errors.Cancelled("the request was cancelled before git answered, and the process tree was killed", "re-issue the same call - the arguments were fine, and nothing about the repository is known to be wrong")),
        { Drained: false } => Result.Fail<string>(Errors.Incomplete("git exited but its output stream stayed open, so what was read is incomplete", "the arguments were fine: retry the same call, and if it repeats narrow it with path= or run the command yourself - answering from a partial stream would be a wrong answer, not a short one")),
        { ExitCode: 0 } => Result.Ok(run.StandardOutput),
        _ => Result.Fail<string>(Errors.Invalid(
            "git exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + Head(run.StandardError),
            "check that this workspace is a git repository and that baseRef names a commit that exists")),
    };

    private static readonly System.Text.UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static string Head(string output)
    {
        var trimmed = output.Trim();

        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "...";
    }

    public static Task<Result<string>> ShowAsync(
            string workingDirectory,
            string reference,
            string relativePath,
            CancellationToken cancellationToken) =>
            ReadAsync(workingDirectory, ["show", reference + ":./" + relativePath.Replace('\\', '/')], cancellationToken);

    private static readonly KeyValuePair<string, string>[] Unattended = [new("GIT_TERMINAL_PROMPT", "0"), new("GIT_ASKPASS", string.Empty), new("GCM_INTERACTIVE", "never")];
}
