using System.Buffers;

namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private const string PowerShellBatch = "TerseSharp guard: NOTHING in this PowerShell batch ran - the guard rewrites only Bash, so it denied the whole call. Make the terse-sharp call named below for the part it replaces, then re-issue only the rest as it stands: ";

    private static readonly SearchValues<string> PowerShellSyntax = SearchValues.Create(["`", "@'", "@\"", "<#"], StringComparison.Ordinal);

    private static GuardVerdict OnPowerShell(string? command, string? cwd) =>
        command is null || command.AsSpan().ContainsAny(PowerShellSyntax)
            ? Allowed
            : Unrewritten(OnBash(command, cwd));

    private static GuardVerdict Unrewritten(GuardVerdict verdict) => verdict.Rewrite is { Length: > 0 } rest
        ? new GuardVerdict(true, PowerShellBatch + rest, verdict.Routing, verdict.Replaces)
        : verdict;
}
