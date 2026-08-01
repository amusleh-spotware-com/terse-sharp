namespace TerseSharp.Core;

public readonly record struct UpdateState(DateTimeOffset CheckedUtc, ReleaseVersion? Latest)
{
    private const string Format = "1 ";
    private const string Unknown = "-";

    public string Render() =>
        string.Create(CultureInfo.InvariantCulture, $"{Format}{CheckedUtc.ToUniversalTime():O} {Latest?.ToString() ?? Unknown}");

    public static bool TryParse(ReadOnlySpan<char> text, out UpdateState state)
    {
        state = default;

        var line = text.Trim();

        if (!line.StartsWith(Format, StringComparison.Ordinal))
            return false;

        var rest = line[Format.Length..];
        var space = rest.IndexOf(' ');

        if (space < 0 || !TryMoment(rest[..space], out var moment))
            return false;

        state = new UpdateState(moment, ReleaseVersion.TryParse(rest[(space + 1)..], out var latest) ? latest : null);

        return true;
    }

    private static bool TryMoment(ReadOnlySpan<char> text, out DateTimeOffset moment) =>
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out moment);
}
