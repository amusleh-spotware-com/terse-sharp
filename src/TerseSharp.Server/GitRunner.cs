namespace TerseSharp.Server;

internal static class GitRunner
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    public static async Task<Result<string>> ReadAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var run = await ChildProcess.RunAsync("git", arguments, workingDirectory, Deadline, cancellationToken, environment: null, Utf8).ConfigureAwait(false);

        if (run.TimedOut)
            return Result.Fail<string>(Errors.Invalid("git did not answer within 60 s and was killed", "narrow the request with path=, or run the command yourself"));

        if (run.Stopped)
            return Result.Fail<string>(Errors.Invalid("the request was cancelled before git answered, and the process tree was killed", "re-issue the call; nothing about the repository is known to be wrong"));

        if (!run.Drained)
            return Result.Fail<string>(Errors.Invalid("git exited but its output stream stayed open, so what was read is incomplete", "narrow the request with path=, or run the command yourself; answering from a partial stream would be a wrong answer, not a short one"));

        return run.ExitCode is 0
            ? Result.Ok(run.StandardOutput)
            : Result.Fail<string>(Errors.Invalid(
                "git exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + Head(run.StandardError),
                "check that this workspace is a git repository and that baseRef names a commit that exists"));
    }

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
}
