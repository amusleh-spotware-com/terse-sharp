using System.Text;

namespace TerseSharp.Core;

public sealed class ResponseBuilder
{
    private readonly StringBuilder text = new(512);

    public ResponseBuilder(string tool, string argument)
    {
        text.Append(tool);

        if (!string.IsNullOrEmpty(argument))
            text.Append(' ').Append(argument);

        text.Append('\n');
    }

    public ResponseBuilder Summary(int shown, int total, string unit)
    {
        var truncated = total > shown ? "true" : "false";

        text.Append(CultureInfo.InvariantCulture, $"{shown} {unit} (truncated={truncated}, total={total})\n\n");

        return this;
    }

    public ResponseBuilder Note(string note)
    {
        text.Append(note).Append('\n');

        return this;
    }

    public ResponseBuilder Line(string line)
    {
        text.Append(line).Append('\n');

        return this;
    }

    public override string ToString() => text.ToString().TrimEnd('\n');
}
