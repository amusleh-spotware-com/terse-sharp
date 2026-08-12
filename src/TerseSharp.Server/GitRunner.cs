namespace TerseSharp.Server;

internal static class GitRunner
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    public static async Task<Result<string>> ReadAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var run = await ChildProcess.RunAsync("git", arguments, workingDirectory, Deadline, cancellationToken).ConfigureAwait(false);

        if (run.TimedOut)
            return Result.Fail<string>(Errors.Invalid("git did not answer within 60 s and was killed", "narrow the request with path=, or run the command yourself"));

        return run.ExitCode is 0
            ? Result.Ok(run.StandardOutput)
            : Result.Fail<string>(Errors.Invalid(
                "git exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + Head(run.StandardError),
                "check that this workspace is a git repository and that baseRef names a commit that exists"));
    }

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
