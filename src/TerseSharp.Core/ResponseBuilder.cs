using System.Text;

namespace TerseSharp.Core;

public sealed class ResponseBuilder(string tool, string argument)
{
    private const int SteerThreshold = 25;

    private readonly List<Entry> entries = new(16);
    private bool verbose;

    public ResponseBuilder Verbose(bool value)
    {
        verbose = value;

        return this;
    }

    public ResponseBuilder Summary(int shown, int total, string unit) => Summary(shown, total, unit, null);

    public ResponseBuilder Summary(int shown, int total, string unit, string? narrowWith)
    {
        entries.Add(new Entry(EntryKind.Summary, string.Empty, new Counted(shown, total, unit, narrowWith)));

        return this;
    }

    public ResponseBuilder Note(string note) => Append(EntryKind.Note, note);

    public ResponseBuilder Line(string line) => Append(EntryKind.Record, line);

    public override string ToString() => verbose ? Verbatim() : Compressed();

    private ResponseBuilder Append(EntryKind kind, string text)
    {
        entries.Add(new Entry(kind, text, default));

        return this;
    }

    private string Verbatim()
    {
        var text = new StringBuilder(512);

        text.Append(tool);

        if (!string.IsNullOrEmpty(argument))
            text.Append(' ').Append(argument);

        text.Append('\n');

        foreach (var entry in entries)
            text.Append(entry.Kind is EntryKind.Summary ? Full(entry.Count) : entry.Text).Append('\n');

        return text.ToString().TrimEnd('\n');
    }

    private string Compressed()
    {
        var text = new StringBuilder(512);

        foreach (var entry in entries)
            text.Append(entry.Kind is EntryKind.Summary ? Brief(entry.Count) : entry.Text).Append('\n');

        return text.ToString().TrimEnd('\n');
    }

    private static string Full(Counted count)
    {
        var truncated = count.Total > count.Shown;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{count.Shown} {count.Unit} (truncated={(truncated ? "true" : "false")}, total={count.Total}){(truncated ? Steer(count) : Advertised(count))}\n");
    }

    private static string Brief(Counted count) => count.Total > count.Shown
        ? string.Create(CultureInfo.InvariantCulture, $"{count.Shown}/{count.Total} {count.Unit} truncated{Steer(count)}")
        : string.Create(CultureInfo.InvariantCulture, $"{count.Shown} {count.Unit}{Advertised(count)}");

    private static string Advertised(Counted count) =>
        count.Shown >= SteerThreshold ? Steer(count) : string.Empty;

    private static string Steer(Counted count) =>
        count.NarrowWith is { Length: > 0 } narrow ? " - narrow with " + narrow : string.Empty;

    private enum EntryKind
    {
        Summary,
        Note,
        Record,
    }

    private readonly record struct Counted(int Shown, int Total, string Unit, string? NarrowWith);

    private readonly record struct Entry(EntryKind Kind, string Text, Counted Count);
}
