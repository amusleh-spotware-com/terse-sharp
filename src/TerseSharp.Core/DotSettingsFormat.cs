using System.Xml.Linq;

namespace TerseSharp.Core;

public readonly record struct DotSettingsConvention(string? Path, bool? UseTabs, int? IndentSize)
{
    public bool Governs => Path is { Length: > 0 } && (UseTabs is not null || IndentSize is not null);
}

public static class DotSettingsFormat
{
    private const string Suffix = ".sln.DotSettings";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (DateTime Stamp, DotSettingsConvention Convention)> Cached = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<DotSettingsConvention> FoundAsync(string? filePath, CancellationToken cancellationToken)
    {
        if (filePath is not { Length: > 0 } || Path.GetDirectoryName(filePath) is not { Length: > 0 } directory)
            return default;

        if (Remembered(directory) is { } remembered)
            return remembered;

        var convention = await WalkedAsync(directory, cancellationToken).ConfigureAwait(false);

        if (convention.Path is { Length: > 0 } settings)
        {
            lock (Gate)
                Cached[directory] = (File.GetLastWriteTimeUtc(settings), convention);
        }

        return convention;
    }

    public static void Forget()
    {
        lock (Gate)
            Cached.Clear();
    }

    private static DotSettingsConvention? Remembered(string directory)
    {
        lock (Gate)
        {
            if (!Cached.TryGetValue(directory, out var known) || known.Convention.Path is not { Length: > 0 } settings)
                return null;

            if (File.GetLastWriteTimeUtc(settings) != known.Stamp)
            {
                Cached.Remove(directory);

                return null;
            }

            return known.Convention;
        }
    }

    private static async Task<DotSettingsConvention> WalkedAsync(string directory, CancellationToken cancellationToken)
    {
        for (var current = directory; current is { Length: > 0 }; current = Path.GetDirectoryName(current) ?? string.Empty)
        {
            if (Settings(current) is { } file)
                return await ParsedAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return default;
    }

    private static string? Settings(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*" + Suffix))
            return file;

        return null;
    }

    private static async Task<DotSettingsConvention> ParsedAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            var document = XDocument.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));

            return new DotSettingsConvention(file, Tabs(document), Size(document));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return default;
        }
    }

    private static bool? Tabs(XDocument document)
    {
        var value = Entry(document, "USE_TABS_ONLY") ?? Entry(document, "USE_TABS");

        return value is not null && bool.TryParse(value, out var tabs) ? tabs : null;
    }

    private static int? Size(XDocument document) =>
        Entry(document, "INDENT_SIZE") is { } value
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
        && size > 0
            ? size
            : null;

    private static string? Entry(XDocument document, string setting)
    {
        foreach (var element in document.Descendants())
        {
            if (Named(element, setting))
                return element.Value.Trim();
        }

        return null;
    }

    private static bool Named(XElement element, string setting) =>
        element.Attributes().Any(attribute =>
            string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal)
            && attribute.Value.EndsWith("/" + setting + "/@EntryValue", StringComparison.Ordinal));
}
