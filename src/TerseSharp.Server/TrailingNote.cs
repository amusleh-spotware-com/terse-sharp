using ModelContextProtocol.Protocol;

namespace TerseSharp.Server;

public static class TrailingNote
{
    public static void Append(CallToolResult result, string note) =>
        result.Content.Add(new TextContentBlock { Text = Separated(result, note) });

    internal static string Separated(CallToolResult result, string note) =>
        result.Content is [.., TextContentBlock { Text: [.., not '\n'] }] ? "\n" + note : note;
}
