using System.Buffers;

namespace TerseSharp.Server;

internal static class NudgeLog
{
    internal const string Variable = "TERSE_NUDGE_LOG";

    internal const string FileName = "batch-nudge.log";

    internal const string Unidentified = "-";

    private static readonly SearchValues<char> Blanks = SearchValues.Create(" \t\r\n");

    internal static bool Enabled() => string.Equals(Environment.GetEnvironmentVariable(Variable), "1", StringComparison.Ordinal);

    internal static string Usable(string? toolUseId) =>
        toolUseId is { Length: > 0 } && !toolUseId.AsSpan().ContainsAny(Blanks) ? toolUseId : Unidentified;

    internal static async Task RecordAsync(string home, string toolUseId, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(home, ".terse"));
            await File.AppendAllTextAsync(Path.Combine(directory.FullName, FileName), Line(toolUseId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Line(string toolUseId) =>
        string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:O} {toolUseId} nudged\n");
}
