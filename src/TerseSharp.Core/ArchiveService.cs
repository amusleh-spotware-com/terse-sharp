using System.IO.Compression;
using System.Text;

namespace TerseSharp.Core;

public static class ArchiveService
{
    private const long MaxEntryBytes = 16L * 1024 * 1024;

    public readonly record struct ArchiveEntryPath(string Archive, string Entry);

    public static ArchiveEntryPath? Entry(string path, string? root = null)
    {
        for (var at = Bang(path, 0); at >= 0; at = Bang(path, at + 2))
        {
            if (Existing(path[..at], root) is { } archive)
                return new ArchiveEntryPath(archive, path[(at + 2)..].Replace('\\', '/'));
        }

        return null;
    }

    public static async Task<Result<string>> ListAsync(
        string archivePath,
        string glob,
        int maxResults,
        bool stamps,
        string? name,
        bool chosen,
        CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(archivePath);

        try
        {
            await using var archive = await ZipFile.OpenReadAsync(full, cancellationToken).ConfigureAwait(false);

            return Result.Ok(Listed(archive, full, glob, maxResults, stamps, name, chosen));
        }
        catch (InvalidDataException)
        {
            return Result.Fail<string>(NotAnArchive(full));
        }
        catch (IOException failure) when (FileService.SharingViolation(failure))
        {
            return Result.Fail<string>(Errors.FileLocked(full));
        }
    }

    public static async Task<Result<string>> ReadAsync(ArchiveEntryPath target, FileService.ReadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(target.Archive, cancellationToken).ConfigureAwait(false);

            return archive.GetEntry(target.Entry) is { } entry && IsFile(entry)
                ? await DecodedAsync(entry, target.Archive + "!/" + target.Entry, request, cancellationToken).ConfigureAwait(false)
                : Result.Fail<string>(Missing(target));
        }
        catch (InvalidDataException)
        {
            return Result.Fail<string>(NotAnArchive(target.Archive));
        }
        catch (IOException failure) when (FileService.SharingViolation(failure))
        {
            return Result.Fail<string>(Errors.FileLocked(target.Archive));
        }
    }

    private static int Bang(ReadOnlySpan<char> path, int from)
    {
        var forward = path[from..].IndexOf("!/", StringComparison.Ordinal);
        var backward = path[from..].IndexOf("!\\", StringComparison.Ordinal);
        var at = forward < 0 || (backward >= 0 && backward < forward) ? backward : forward;

        return at < 0 ? -1 : from + at;
    }

    private static string Listed(ZipArchive archive, string full, string glob, int maxResults, bool stamps, string? name, bool chosen)
    {
        var kept = Kept(archive, FileGlob.Compile(glob), name);
        var shown = Math.Min(kept.Count, maxResults);
        var response = new ResponseBuilder("find_files", glob).Chosen(chosen);

        response.Summary(shown, kept.Count, "entries", "a narrower glob=, name= or maxResults=");

        for (var index = 0; index < shown; index++)
            response.Line(Row(kept[index], stamps));

        response.Note("archive  " + full);

        return response.ToString();
    }

    private static List<ZipArchiveEntry> Kept(ZipArchive archive, FileGlob matcher, string? name)
    {
        var kept = new List<ZipArchiveEntry>(archive.Entries.Count);

        foreach (var entry in archive.Entries)
        {
            if (IsFile(entry) && matcher.MatchesRelative(entry.FullName) && Named(entry, name))
                kept.Add(entry);
        }

        return kept;
    }

    private static bool IsFile(ZipArchiveEntry entry) => entry.FullName is { Length: > 0 } full && !full.EndsWith('/');

    private static bool Named(ZipArchiveEntry entry, string? name) =>
        name is not { Length: > 0 } || entry.Name.Contains(name, StringComparison.OrdinalIgnoreCase);

    private static string Row(ZipArchiveEntry entry, bool stamps) => stamps
        ? string.Create(CultureInfo.InvariantCulture, $"{entry.FullName}  {entry.LastWriteTime.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}  {entry.Length}")
        : entry.FullName;

    private static async Task<Result<string>> DecodedAsync(ZipArchiveEntry entry, string label, FileService.ReadRequest request, CancellationToken cancellationToken)
    {
        if (entry.Length > MaxEntryBytes)
            return Result.Fail<string>(Oversized(label, entry.Length));

        var bytes = new byte[entry.Length];
        var read = await FilledAsync(entry, bytes, cancellationToken).ConfigureAwait(false);
        var probe = BinaryContent.Probe(bytes.AsSpan(0, read), label, entry.Length);

        if (probe.Refusal is { } binary)
            return binary;

        var text = await TextAsync(bytes, read, probe.Utf16, cancellationToken).ConfigureAwait(false);

        return FileService.Rendered(entry.FullName, label, text, request with { Length = entry.Length, Characters = text.Length, Ticks = entry.LastWriteTime.UtcTicks });
    }

    private static async Task<int> FilledAsync(ZipArchiveEntry entry, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> TextAsync(byte[] bytes, int read, Encoding? utf16, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, 0, read, writable: false), utf16 ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TerseError Missing(ArchiveEntryPath target) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{target.Entry}' is not a file entry of '{target.Archive}'"),
        string.Create(CultureInfo.InvariantCulture, $"list its entries with find_files root=\"{target.Archive}\" - an entry name is case-sensitive and separated by /"));

    private static TerseError NotAnArchive(string path) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' is a file but not a zip archive"),
        "root= takes a directory, or a .zip or .nupkg file to list its entries; read_text reads a plain file by its path alone");

    private static TerseError Oversized(string label, long length) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{label}' is {length} bytes uncompressed, over the {MaxEntryBytes} bytes an archive entry is read up to"),
        "read a smaller entry, or extract this one and read_text the extracted file with tail= or a line range");

    public static bool RelativeEntry(string path) => Bang(path, 0) >= 0 && !Path.IsPathFullyQualified(path);

    private static string? Existing(string prefix, string? root)
    {
        var full = (Path.IsPathFullyQualified(prefix), root) switch
        {
            (true, _) => Path.GetFullPath(prefix),
            (false, { Length: > 0 } basePath) when prefix.Length > 0 => Path.GetFullPath(prefix, basePath),
            _ => null,
        };

        return full is not null && File.Exists(full) ? full : null;
    }
}
