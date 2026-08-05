using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class TextSearchService
{
    private const int MaxLineLength = 200;

    private const int BinaryProbe = 4096;

    private const long MaxSearchableBytes = 16L * 1024 * 1024;


    private static readonly FrozenSet<string> BinaryExtensions = new[]
    {
        ".dll", ".exe", ".pdb", ".so", ".dylib", ".lib", ".obj", ".res", ".resources", ".cache",
        ".zip", ".7z", ".gz", ".tar", ".nupkg", ".snupkg", ".bin", ".dat", ".db", ".sqlite",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp", ".pdf",
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".mp3", ".mp4", ".wav", ".snk", ".pfx",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static async Task<string> SearchAsync(
        LoadedWorkspace workspace,
        string pattern,
        string glob,
        bool regex,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var matcher = TextMatcher.Create(pattern, regex);
        var candidates = Matched(workspace, glob).Where(IsSearchableFile).ToArray();
        var perFile = new FileHits[candidates.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Length),
            ParallelWork.Options(cancellationToken),
            async (index, token) => perFile[index] = await ScanAsync(candidates[index], matcher, maxResults, token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return Render(regex ? "search_regex" : "search_text", pattern, perFile, maxResults);
    }

    public static string FindFiles(LoadedWorkspace workspace, string glob, int maxResults)
    {
        var files = Matched(workspace, glob);
        var response = new ResponseBuilder("find_files", glob);

        response.Summary(Math.Min(files.Count, maxResults), files.Count, "files", "a narrower glob= or maxResults=");

        foreach (var file in files.Take(maxResults))
            response.Line(file.RelativePath);

        return response.ToString();
    }

    private static bool IsBinary(string text) =>
        text.AsSpan(0, Math.Min(text.Length, BinaryProbe)).Contains('\0');

    private static FileHits Collect(string relativePath, string text, TextMatcher matcher, int maxResults)
    {
        var span = text.AsSpan();
        var hits = new List<string>();
        var tracker = new LineTracker();
        var total = 0;
        var index = 0;

        while (index < span.Length && matcher.Next(span, index) is var at and >= 0)
        {
            total++;

            if (hits.Count < maxResults)
                hits.Add(Format(relativePath, span, at, ref tracker));

            index = EndOfLine(span, at) + 1;
        }

        return new FileHits(hits, total, 0);
    }

    private static string Format(string relativePath, ReadOnlySpan<char> text, int at, ref LineTracker tracker)
    {
        tracker.Advance(text, at);

        var start = text[..at].LastIndexOf('\n') + 1;
        var line = text[start..EndOfLine(text, at)].Trim();

        return string.Create(CultureInfo.InvariantCulture, $"{relativePath}:{tracker.Line}  HEURISTIC  {Shorten(line)}");
    }

    private static int EndOfLine(ReadOnlySpan<char> text, int at) =>
        text[at..].IndexOf('\n') is var offset and >= 0 ? at + offset : text.Length;

    private static string Shorten(ReadOnlySpan<char> line) => line.Length <= MaxLineLength
        ? new string(line)
        : string.Create(CultureInfo.InvariantCulture, $"{line[..MaxLineLength]}... (+{line.Length - MaxLineLength} chars)");

    private static string Render(string tool, string pattern, FileHits[] perFile, int maxResults)
    {
        var response = new ResponseBuilder(tool, pattern);
        var shown = new List<string>(Math.Min(maxResults, 512));
        var total = 0;
        var skipped = 0;

        foreach (var file in perFile)
        {
            total += file.Total;
            skipped += file.Skipped;
            shown.AddRange(file.Hits.Take(Math.Max(0, maxResults - shown.Count)));
        }

        return Write(response, shown, total, skipped);
    }

    private static string Write(ResponseBuilder response, List<string> shown, int total, int skipped)
    {
        response.Summary(shown.Count, total, "matches", "glob= or maxResults=");

        if (skipped > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"skipped {skipped} files over {MaxSearchableBytes / (1024 * 1024)} MB"));

        foreach (var hit in shown)
            response.Line(hit);

        return response.ToString();
    }

    private static FileStreamOptions Options() => new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    private readonly record struct FileHits(IReadOnlyList<string> Hits, int Total, int Skipped)
    {
        public static FileHits None => new([], 0, 0);

        public static FileHits Oversized => new([], 0, 1);
    }

    private struct LineTracker
    {
        private int scanned;

        public int Line { get; private set; } = 1;

        public LineTracker() => scanned = 0;

        public void Advance(ReadOnlySpan<char> text, int to)
        {
            var window = text[scanned..to];
            var offset = 0;

            while (window[offset..].IndexOf('\n') is var next and >= 0)
            {
                Line++;
                offset += next + 1;
            }

            scanned = to;
        }
    }

    private readonly record struct TextMatcher(string Pattern, Regex? Expression, byte[]? Needle)
    {
        public static TextMatcher Create(string pattern, bool regex) => regex
            ? new(pattern, Compile(pattern), null)
            : new(pattern, null, Encoding.UTF8.GetBytes(pattern));

        public bool MayContain(ReadOnlySpan<byte> content) => Needle is null || content.IndexOf(Needle) >= 0;

        public int Next(ReadOnlySpan<char> text, int from)
        {
            if (Expression is null)
                return text[from..].IndexOf(Pattern, StringComparison.Ordinal) is var offset and >= 0 ? from + offset : -1;

            foreach (var match in Expression.EnumerateMatches(text, from))
                return match.Index;

            return -1;
        }

        private static Regex Compile(string pattern)
        {
            try
            {
                return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Multiline);
            }
            catch (NotSupportedException)
            {
                return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromSeconds(2));
            }
        }
    }

    private static List<WorkspacePath> Matched(LoadedWorkspace workspace, string glob)
    {
        var matcher = FileGlob.Compile(string.IsNullOrWhiteSpace(glob) ? "*" : glob);
        var index = workspace.Indexes.Paths();
        var matched = new List<WorkspacePath>(Math.Min(index.Count, 1024));

        foreach (var path in index.Paths)
        {
            if (matcher.MatchesRelative(path.RelativePath))
                matched.Add(path);
        }

        return matched;
    }

    private static bool IsSearchableFile(WorkspacePath file) =>
        !BinaryExtensions.Contains(Path.GetExtension(file.FullPath));

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianBom = [0xFE, 0xFF];

    private static async Task<FileHits> ScanAsync(
        WorkspacePath file,
        TextMatcher matcher,
        int maxResults,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProbeAsync(file, matcher, maxResults, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return FileHits.None;
        }
        catch (UnauthorizedAccessException)
        {
            return FileHits.None;
        }
    }

    private static async Task<FileHits> ProbeAsync(
        WorkspacePath file,
        TextMatcher matcher,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(file.FullPath, Options());

        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanSeek)
                return Text(file.RelativePath, await StreamedAsync(stream, cancellationToken).ConfigureAwait(false), matcher, maxResults);

            return stream.Length > MaxSearchableBytes
                ? FileHits.Oversized
                : await BufferedAsync(stream, file.RelativePath, matcher, maxResults, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> StreamedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileHits Text(string relativePath, string text, TextMatcher matcher, int maxResults) =>
        IsBinary(text) ? FileHits.None : Collect(relativePath, text, matcher, maxResults);

    private static async Task<FileHits> BufferedAsync(
        FileStream stream,
        string relativePath,
        TextMatcher matcher,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max((int)stream.Length, 1));

        try
        {
            var probe = Math.Min(buffer.Length, BinaryProbe);
            var probed = await FillAsync(stream, buffer.AsMemory(0, probe), cancellationToken).ConfigureAwait(false);

            if (LooksBinary(buffer.AsSpan(0, probed)))
                return FileHits.None;

            var filled = probed + await FillAsync(stream, buffer.AsMemory(probed), cancellationToken).ConfigureAwait(false);

            return Scan(relativePath, buffer, filled, matcher, maxResults);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<int> FillAsync(FileStream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var filled = 0;

        while (filled < destination.Length)
        {
            var read = await stream.ReadAsync(destination[filled..], cancellationToken).ConfigureAwait(false);

            if (read is 0)
                break;

            filled += read;
        }

        return filled;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> prefix) =>
        !IsWideText(prefix) && prefix.IndexOf((byte)0) >= 0;

    private static FileHits Scan(string relativePath, byte[] buffer, int length, TextMatcher matcher, int maxResults)
    {
        var content = buffer.AsSpan(0, length);

        return !IsWideText(content) && !matcher.MayContain(content)
            ? FileHits.None
            : Text(relativePath, Decode(content), matcher, maxResults);
    }

    private static bool IsWideText(ReadOnlySpan<byte> content) =>
        content.StartsWith(Utf16LittleEndianBom) || content.StartsWith(Utf16BigEndianBom);

    private static string Decode(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Utf16LittleEndianBom))
            return Encoding.Unicode.GetString(content[Utf16LittleEndianBom.Length..]);

        if (content.StartsWith(Utf16BigEndianBom))
            return Encoding.BigEndianUnicode.GetString(content[Utf16BigEndianBom.Length..]);

        return Encoding.UTF8.GetString(content.StartsWith(Utf8Bom) ? content[Utf8Bom.Length..] : content);
    }
}
