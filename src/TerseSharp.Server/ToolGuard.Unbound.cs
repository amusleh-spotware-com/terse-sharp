namespace TerseSharp.Server;

public static partial class ToolGuard
{
    private static bool Unbound(string segment)
    {
        var command = Command(segment);
        var patterns = command.Length > 0 && PatternCommands.Contains(Path.GetFileNameWithoutExtension(command[0]), StringComparer.OrdinalIgnoreCase) ? 1 : 0;

        for (var index = 1; index < command.Length; index++)
        {
            if (command[index].StartsWith('-') || IsCount(command[index]) || patterns-- > 0)
                continue;

            if (IsBare(command[index]))
                return true;
        }

        return false;
    }

    private static bool IsBare(string operand) =>
        IsUnassigned(operand.StartsWith("${", StringComparison.Ordinal) && !operand.EndsWith('}') ? operand + "}" : operand);
}
