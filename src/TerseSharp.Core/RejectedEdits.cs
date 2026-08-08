namespace TerseSharp.Core;

public sealed record RejectedEdit(string Token, string Root, string Tool, IReadOnlyList<string> Targets, IReadOnlyList<string> Payloads);

public static class RejectedEdits
{
    private const int Capacity = 8;

    private static readonly Lock Gate = new();

    private static readonly Queue<RejectedEdit> Held = new(Capacity);

    private static int counter;

    public static string Remember(string root, string tool, IReadOnlyList<string> targets, IReadOnlyList<string> payloads)
    {
        lock (Gate)
        {
            var token = "r" + (++counter).ToString(CultureInfo.InvariantCulture);

            Held.Enqueue(new RejectedEdit(token, root, tool, targets, payloads));

            while (Held.Count > Capacity)
                Held.Dequeue();

            return token;
        }
    }

    public static RejectedEdit? Recall(string token)
    {
        lock (Gate)
        {
            foreach (var edit in Held)
            {
                if (string.Equals(edit.Token, token, StringComparison.Ordinal))
                    return edit;
            }

            return null;
        }
    }
}
