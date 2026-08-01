namespace TerseSharp.Core;

public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, bool Prerelease)
{
    public static bool TryParse(ReadOnlySpan<char> text, out ReleaseVersion version)
    {
        version = default;

        var trimmed = text.Trim();

        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        if (!TryComponents(Core(trimmed, out var prerelease), out var major, out var minor, out var patch))
            return false;

        version = new ReleaseVersion(major, minor, patch, prerelease);

        return true;
    }

    public bool IsNewerThan(ReleaseVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch)) switch
        {
            > 0 => true,
            < 0 => false,
            _ => other.Prerelease && !Prerelease,
        };

    public override string ToString() =>
        Prerelease
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-pre")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    private static ReadOnlySpan<char> Core(ReadOnlySpan<char> text, out bool prerelease)
    {
        var dash = text.IndexOf('-');
        var cut = Earliest(dash, text.IndexOf('+'));

        prerelease = cut >= 0 && cut == dash;

        return cut < 0 ? text : text[..cut];
    }

    private static int Earliest(int left, int right) => (left, right) switch
    {
        (< 0, _) => right,
        (_, < 0) => left,
        _ => Math.Min(left, right),
    };

    private static bool TryComponents(ReadOnlySpan<char> core, out int major, out int minor, out int patch)
    {
        minor = 0;
        patch = 0;

        var first = core.IndexOf('.');

        if (first < 0 || !TryNumber(core[..first], out major))
        {
            major = 0;

            return false;
        }

        var rest = core[(first + 1)..];
        var second = rest.IndexOf('.');

        return second < 0
            ? TryNumber(rest, out minor)
            : TryNumber(rest[..second], out minor) && TryNumber(rest[(second + 1)..], out patch);
    }

    private static bool TryNumber(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
