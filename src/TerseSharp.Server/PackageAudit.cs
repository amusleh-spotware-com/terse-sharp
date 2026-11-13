namespace TerseSharp.Server;

internal sealed record AuditFailure(ProcessRun Run, string Flag, string Root, string ProjectPath, bool Vulnerable);

internal static class PackageAudit
{
    public static async Task<string> AnsweredAsync(ResponseBuilder response, AuditFailure failure, CancellationToken cancellationToken)
    {
        if (failure.Vulnerable || !File.Exists(failure.ProjectPath))
            return Unaudited(response, failure);

        var audit = await FlatContainer.AuditAsync(failure.Root, failure.ProjectPath, cancellationToken).ConfigureAwait(false);

        response.Summary(audit.Outdated, audit.Outdated, "outdated packages");
        response.Note(FeedNote(failure.Run, audit.Examined));

        foreach (var line in audit.Lines.Concat(DotnetRunner.Tail(failure.Run.Output)))
            response.Line(line);

        return response.ToString();
    }

    private static string Unaudited(ResponseBuilder response, AuditFailure failure)
    {
        response.Summary(0, 0, "packages examined");
        response.Note(DotnetRunner.Unexamined(failure.Run, failure.Flag));

        foreach (var line in DotnetRunner.Tail(failure.Run.Output))
            response.Line(line);

        return response.ToString();
    }

    private static string FeedNote(ProcessRun run, int examined) =>
        string.Create(CultureInfo.InvariantCulture, $"FAILED dotnet list package --outdated exited {run.ExitCode}; answered from the nuget.org flat container instead - {examined} declared references examined, transitive packages and other configured feeds were not, so this is not a clean bill of health");
}
