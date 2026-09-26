using ModelContextProtocol.Protocol;

namespace TerseSharp.Server;

public static class TrailingNote
{
    public static void Append(CallToolResult result, string note)
    {
        if (result.Content is [.., TextContentBlock last])
            result.Content[result.Content.Count - 1] = new TextContentBlock { Text = Separated(last.Text, note) };
        else
            result.Content.Add(new TextContentBlock { Text = note });
    }

    internal static string Separated(string payload, string note) =>
        payload is [] or [.., '\n'] ? string.Concat(payload, note) : string.Concat(payload, "\n", note);
}
