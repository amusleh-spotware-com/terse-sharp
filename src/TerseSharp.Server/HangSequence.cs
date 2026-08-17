using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Server;

internal static class HangSequence
{
    private const string SequenceGlob = "*Sequence*.xml";
    private const int MaxNames = 5;

    public static async Task<string[]> ActiveAsync(DirectoryInfo results, CancellationToken cancellationToken)
    {
        if (!results.Exists)
            return [];

        var names = new List<string>(MaxNames);

        foreach (var file in results.EnumerateFiles(SequenceGlob, SearchOption.AllDirectories))
        {
            if (names.Count == MaxNames)
                break;

            if (await LastAsync(file, cancellationToken).ConfigureAwait(false) is { Length: > 0 } name && !names.Contains(name, StringComparer.Ordinal))
                names.Add(name);
        }

        return [.. names];
    }

    private static async Task<string?> LastAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);

            return document.Root?.Elements().LastOrDefault(element => element.Name.LocalName is "Test")?.Attribute("Name")?.Value;
        }
        catch (Exception failure) when (failure is IOException or XmlException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
