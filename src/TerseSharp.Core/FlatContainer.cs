using System.Text.Json;
using System.Xml.Linq;

namespace TerseSharp.Core;

public readonly record struct PackagePin(string Id, string? Version);

public readonly record struct FeedAnswer(bool Parsed, string? Newer)
{
    public static FeedAnswer Unparsed => new(false, null);
}

public sealed record FeedAudit(int Examined, int Outdated, IReadOnlyList<string> Lines);

public static class FlatContainer
{
    public const string Endpoint = "https://api.nuget.org/v3-flatcontainer/";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    public static async Task<FeedAudit> AuditAsync(string root, string projectPath, CancellationToken cancellationToken)
    {
        var pins = await ProjectFile.PinsAsync(root, projectPath).ConfigureAwait(false);
        using var client = new HttpClient { Timeout = Deadline, MaxResponseContentBufferSize = 1 << 20 };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("terse");

        return await AuditAsync(pins, (id, token) => IndexAsync(client, id, token), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<FeedAudit> AuditAsync(
        IReadOnlyList<PackagePin> pins,
        Func<string, CancellationToken, Task<byte[]?>> fetch,
        CancellationToken cancellationToken)
    {
        var indexes = await Task.WhenAll(pins.Select(pin => Lookup(pin, fetch, cancellationToken))).ConfigureAwait(false);
        List<string> lines = [with(pins.Count)];
        var outdated = 0;

        for (var position = 0; position < pins.Count; position++)
        {
            if (Verdict(pins[position], indexes[position]) is not { } line)
                continue;

            lines.Add(line);
            outdated += line.StartsWith("HEURISTIC", StringComparison.Ordinal) ? 1 : 0;
        }

        return new FeedAudit(pins.Count, outdated, lines);
    }

    public static string? Verdict(PackagePin pin, byte[]? index) =>
        pin.Version is not { } current || !PackageVersionOrder.IsConcrete(current) ? Unresolved(pin, "its version is not a literal, so it was not looked up")
        : index is null ? Unresolved(pin, "nuget.org returned no index for it")
        : FeedIndex.Newer(index, current) switch
        {
            { Parsed: false } => Unresolved(pin, "nuget.org answered without a version list"),
            { Newer: { } latest } => string.Create(CultureInfo.InvariantCulture, $"HEURISTIC  {pin.Id}  {current} -> {latest}"),
            _ => null,
        };

    public static Uri Index(string id) =>
        new(string.Concat(Endpoint, Uri.EscapeDataString(Lowered(id)), "/index.json"), UriKind.Absolute);

    private static async Task<byte[]?> IndexAsync(HttpClient client, string id, CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetByteArrayAsync(Index(id), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string Lowered(string id) =>
        string.Create(id.Length, id, static (target, source) => source.AsSpan().ToLowerInvariant(target));

    private static Task<byte[]?> Lookup(PackagePin pin, Func<string, CancellationToken, Task<byte[]?>> fetch, CancellationToken cancellationToken) =>
        pin.Version is { } version && PackageVersionOrder.IsConcrete(version) ? fetch(pin.Id, cancellationToken) : Task.FromResult<byte[]?>(null);

    private static string Unresolved(PackagePin pin, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"UNRESOLVED  {pin.Id}  {pin.Version ?? "-"}  {reason}");
}

public static class PackagePins
{
    public static IEnumerable<PackagePin> From(IEnumerable<XElement> references, XDocument? central)
    {
        foreach (var reference in references)
        {
            if (reference.Attribute("Include")?.Value is { Length: > 0 } id)
                yield return new PackagePin(id, Declared(reference) ?? Central(central, id));
        }
    }

    private static string? Declared(XElement reference) =>
        reference.Attribute("VersionOverride")?.Value
        ?? reference.Attribute("Version")?.Value
        ?? reference.Element("Version")?.Value;

    private static string? Central(XDocument? central, string id) =>
        central?.Descendants("PackageVersion")
            .FirstOrDefault(version => string.Equals(version.Attribute("Include")?.Value, id, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Version")?.Value;
}

public static class FeedIndex
{
    private const int MaxVersion = 128;

    public static FeedAnswer Newer(ReadOnlySpan<byte> index, ReadOnlySpan<char> current)
    {
        try
        {
            var reader = new Utf8JsonReader(index);

            return SeekVersions(ref reader) ? Scan(ref reader, current) : FeedAnswer.Unparsed;
        }
        catch (JsonException)
        {
            return FeedAnswer.Unparsed;
        }
    }

    private static bool SeekVersions(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.CurrentDepth is 1 && reader.TokenType is JsonTokenType.PropertyName && reader.ValueTextEquals("versions"u8))
                return reader.Read() && reader.TokenType is JsonTokenType.StartArray;
        }

        return false;
    }

    private static FeedAnswer Scan(ref Utf8JsonReader reader, ReadOnlySpan<char> current)
    {
        Span<char> best = stackalloc char[MaxVersion];
        Span<char> candidate = stackalloc char[MaxVersion];
        var length = 0;
        var prerelease = PackageVersionOrder.WithoutMetadata(current).Contains('-');

        while (reader.Read() && reader.TokenType is JsonTokenType.String && reader.ValueSpan.Length <= MaxVersion)
        {
            var version = candidate[..reader.CopyString(candidate)];

            if (Wanted(version, prerelease) && PackageVersionOrder.Compare(version, length is 0 ? current : best[..length]) > 0)
                length = Kept(version, best);
        }

        return reader.TokenType is JsonTokenType.EndArray
            ? new FeedAnswer(true, length is 0 ? null : new string(best[..length]))
            : FeedAnswer.Unparsed;
    }

    private static bool Wanted(ReadOnlySpan<char> version, bool prerelease) =>
        PackageVersionOrder.IsConcrete(version) && (prerelease || !PackageVersionOrder.WithoutMetadata(version).Contains('-'));

    private static int Kept(ReadOnlySpan<char> version, Span<char> best)
    {
        version.CopyTo(best);

        return version.Length;
    }
}

public static class PackageVersionOrder
{
    public static int Compare(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var leftCore = WithoutMetadata(left);
        var rightCore = WithoutMetadata(right);
        var leftDash = leftCore.IndexOf('-');
        var rightDash = rightCore.IndexOf('-');
        var release = Segments(Release(leftCore, leftDash), Release(rightCore, rightDash), padded: true);

        return (release, leftDash < 0, rightDash < 0) switch
        {
            (not 0, _, _) => release,
            (_, true, true) => 0,
            (_, true, false) => 1,
            (_, false, true) => -1,
            _ => Segments(leftCore[(leftDash + 1)..], rightCore[(rightDash + 1)..], padded: false),
        };
    }

    public static bool IsConcrete(ReadOnlySpan<char> version)
    {
        var core = WithoutMetadata(version);
        var dash = core.IndexOf('-');
        var release = Release(core, dash);
        var segments = 0;

        foreach (var range in release.Split('.'))
        {
            if (!Numeric(release[range], out _) || ++segments > 4)
                return false;
        }

        return segments > 0 && dash < core.Length - 1;
    }

    internal static ReadOnlySpan<char> WithoutMetadata(ReadOnlySpan<char> version) =>
        version.IndexOf('+') is var plus and >= 0 ? version[..plus] : version;

    private static int Segments(ReadOnlySpan<char> left, ReadOnlySpan<char> right, bool padded)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var order = 0;

        while (order is 0)
        {
            var (hasLeft, hasRight) = (leftParts.MoveNext(), rightParts.MoveNext());

            if (!hasLeft && !hasRight)
                return 0;

            order = padded || hasLeft == hasRight
                ? Label(hasLeft ? left[leftParts.Current] : "0", hasRight ? right[rightParts.Current] : "0")
                : hasLeft ? 1 : -1;
        }

        return order;
    }

    private static int Label(ReadOnlySpan<char> left, ReadOnlySpan<char> right) =>
        (Numeric(left, out var leftValue), Numeric(right, out var rightValue)) switch
        {
            (true, true) => leftValue.CompareTo(rightValue),
            (true, false) => -1,
            (false, true) => 1,
            _ => Math.Sign(left.CompareTo(right, StringComparison.OrdinalIgnoreCase)),
        };

    private static bool Numeric(ReadOnlySpan<char> part, out long value) =>
        long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static ReadOnlySpan<char> Release(ReadOnlySpan<char> core, int dash) => dash < 0 ? core : core[..dash];
}
