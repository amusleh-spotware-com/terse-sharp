namespace TerseSharp.Core;

public sealed record RejectedEdit(
    string Token,
    string Root,
    string Tool,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Payloads,
    IReadOnlyList<string> Add,
    string? AddTo,
    IReadOnlyList<string> Usings,
    bool Rename = false,
    MemberPlacement? Placement = null,
    IReadOnlyList<FileService.TextEdit>? Edits = null);

public readonly record struct TokenOrigin(string? Tool, int Newest, bool Issued);

public static class RejectedEdits
{
    public const int Capacity = 16;

    private const int Remembered = 256;

    private static readonly Lock Gate = new();

    private static readonly Queue<RejectedEdit> Held = new(Capacity);

    private static readonly string?[] Issuers = new string?[Remembered];

    private static int counter;

    public static string Remember(
        string root,
        string tool,
        IReadOnlyList<string> targets,
        IReadOnlyList<string> payloads,
        IReadOnlyList<string>? add = null,
        string? addTo = null,
        IReadOnlyList<string>? usings = null,
        bool rename = false,
        MemberPlacement? placement = null,
        IReadOnlyList<FileService.TextEdit>? edits = null)
    {
        lock (Gate)
        {
            var issued = ++counter;
            var token = "r" + issued.ToString(CultureInfo.InvariantCulture);

            Issuers[issued % Remembered] = tool;
            Held.Enqueue(new RejectedEdit(token, root, tool, targets, payloads, add ?? [], addTo, usings ?? [], rename, placement, edits));

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

    public static TokenOrigin Origin(string token)
    {
        lock (Gate)
        {
            return Number(token) is { } issued && issued <= counter
                ? new TokenOrigin(counter - issued < Remembered ? Issuers[issued % Remembered] : null, counter, Issued: true)
                : new TokenOrigin(null, counter, Issued: false);
        }
    }

    private static int? Number(string token) =>
        token.AsSpan() is ['r', .. var digits]
        && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var issued)
        && issued > 0
            ? issued
            : null;
}
