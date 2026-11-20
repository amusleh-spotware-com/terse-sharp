using System.Buffers;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private const string PowerShellBatch = "TerseSharp guard: NOTHING in this PowerShell batch ran - the guard rewrites only Bash, so it denied the whole call. Make the terse-sharp call named below for the part it replaces, then re-issue only the rest as it stands: ";

    private static readonly SearchValues<string> PowerShellOpaque = SearchValues.Create(["@'", "@\"", "<#"], StringComparison.Ordinal);

    private static GuardVerdict OnPowerShell(string? command, string? cwd) =>
        command is null || command.AsSpan().ContainsAny(PowerShellOpaque)
            ? Allowed
            : Unrewritten(OnBash(Bashed(command), cwd));

    private static string Bashed(string command) =>
        EnvironmentReference().Replace(PathParameter().Replace(LocationCmdlet().Replace(BacktickEscape().Replace(command, Unescaped), Relocated), " "), "$$$1");

    private static string Unescaped(Match match) => match.Groups[1].Success ? match.Groups[1].Value : " ";

    private static string Relocated(Match match) => match.Value.ToUpperInvariant() switch
    {
        "PUSH-LOCATION" or "PUSHD" => "pushd",
        "POP-LOCATION" or "POPD" => "popd",
        _ => "cd",
    };

    private static GuardVerdict Unrewritten(GuardVerdict verdict) => verdict.Rewrite is { Length: > 0 } rest
        ? new GuardVerdict(true, PowerShellBatch + rest, verdict.Routing, verdict.Replaces)
        : verdict;

    [GeneratedRegex(@"`(?:\r?\n|(.))")]
    private static partial Regex BacktickEscape();

    [GeneratedRegex(@"(?<=(?:^|[;|&{(\r\n])\s*)(?:Set-Location|sl|chdir|Push-Location|pushd|Pop-Location|popd)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LocationCmdlet();

    [GeneratedRegex(@"\s-(?:Literal)?Path(?=\s)", RegexOptions.IgnoreCase)]
    private static partial Regex PathParameter();

    [GeneratedRegex(@"\$env:(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex EnvironmentReference();
}
