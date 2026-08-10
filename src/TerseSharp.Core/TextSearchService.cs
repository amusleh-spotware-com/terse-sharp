using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
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
        TextSearchRequest request,
        CancellationToken cancellationToken)
    {
        var files = Kept([.. Matched(workspace, request.Glob).Where(IsSearchableFile)], request.Exclude);

        return await ScannedAsync(files, request, cancellationToken).ConfigureAwait(false);
    }
    public static async Task<string> SearchOutsideAsync(TextSearchRequest request, CancellationToken cancellationToken)
    {
        var candidates = Outside(request.Root ?? string.Empty, request.Glob);

        return candidates.IsOk
            ? await ScannedAsync(Kept(candidates.Value!, request.Exclude), request, cancellationToken).ConfigureAwait(false)
            : candidates.Error!.Render();
    }
    private static async Task<string> ScannedAsync(
    List<WorkspacePath> files,
    TextSearchRequest request,
    CancellationToken cancellationToken)
    {
        var matcher = TextMatcher.Create(request.Patterns, request.Regex);
        var perFile = new FileHits[files.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            ParallelWork.Options(cancellationToken),
            async (index, token) => perFile[index] = await ScanAsync(files[index], matcher, request, token).ConfigureAwait(false))
            .ConfigureAwait(false);

        return Render(request, perFile);
    }

    private static Result<List<WorkspacePath>> Outside(string root, string glob)
    {
        if (!Path.IsPathFullyQualified(root))
        {
            return Result.Fail<List<WorkspacePath>>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"root '{root}' is not an absolute path"),
                "pass an absolute directory, or drop root= to search the workspace"));
        }

        var full = Path.GetFullPath(root);

        if (!Directory.Exists(full))
            return Result.Fail<List<WorkspacePath>>(Errors.DocumentNotFound(root));

        var matcher = FileGlob.Compile(string.IsNullOrWhiteSpace(glob) ? "*" : glob);
        var matched = new List<WorkspacePath>(1024);

        foreach (var file in WorkspaceFiles.Enumerate(full, _ => true))
        {
            var relative = Path.GetRelativePath(full, file);

            if (matcher.MatchesRelative(relative) && IsSearchableFile(new WorkspacePath(file, relative)))
                matched.Add(new WorkspacePath(file, relative));
        }

        return Result.Ok(matched);
    }

    public static string FindFiles(
    LoadedWorkspace workspace,
    string glob,
    int maxResults,
    bool stamps,
    IReadOnlySet<string>? tracked = null)
    {
        var files = Tracked(Matched(workspace, glob), tracked);
        var response = new ResponseBuilder("find_files", glob);
        response.Summary(ResultCap.Shown(files.Count, maxResults), files.Count, "files", "a narrower glob= or maxResults=");
        foreach (var file in files.Capped(maxResults))
            response.Line(stamps ? Stamped(file) : file.RelativePath);

        return response.ToString();
    }

    private static bool IsBinary(string text) =>
        text.AsSpan(0, Math.Min(text.Length, BinaryProbe)).Contains('\0');

    private static FileHits Collect(string relativePath, string text, TextMatcher matcher, TextSearchRequest request)
    {
        var span = text.AsSpan();
        var hits = new List<string>();
        var tracker = new LineTracker();
        var total = 0;
        var index = 0;

        while (index < span.Length && matcher.Next(span, index) is { At: >= 0 } match)
        {
            total++;

            if (hits.Count < request.MaxResults)
                hits.Add(Format(relativePath, span, match, ref tracker, request, matcher));

            index = EndOfLine(span, match.At + Math.Max(match.Length - 1, 0)) + 1;
        }

        return new FileHits(hits, total, 0);
    }

    private static int Content(ReadOnlySpan<char> text, TextMatch match)
    {
        var offset = text.Slice(match.At, match.Length).IndexOfAnyExcept(Blank);

        return offset < 0 ? match.At : match.At + offset;
    }

    private static string Format(string relativePath, ReadOnlySpan<char> text, TextMatch match, ref LineTracker tracker, TextSearchRequest request, TextMatcher matcher)
    {
        var at = Content(text, match);

        tracker.Advance(text, at);

        var start = text[..at].LastIndexOf('\n') + 1;
        var end = EndOfLine(text, at);
        var matched = text.Slice(match.At, match.Length).Trim();
        var payload = request.MatchesOnly && matched.Length > 0 ? matched : text[start..end].Trim();
        var hit = request.SeveralPatterns
            ? string.Create(CultureInfo.InvariantCulture, $"{relativePath}:{tracker.Line}{RecordSeparator}{Tagged(matcher, text[start..end], match.Query)}{RecordSeparator}{Shorten(payload)}")
            : string.Create(CultureInfo.InvariantCulture, $"{relativePath}:{tracker.Line}{RecordSeparator}{Shorten(payload)}");

        return request.Around is 0 ? hit : hit + Around(text, new LineWindow(start, end, tracker.Line), request.Around);
    }

    private static string Around(ReadOnlySpan<char> text, LineWindow window, int context)
    {
        var from = window.Start;
        var above = 0;

        while (above < context && from > 0)
        {
            from = text[..(from - 1)].LastIndexOf('\n') + 1;
            above++;
        }

        var to = window.End;
        var below = 0;

        while (below < context && to < text.Length)
        {
            to = EndOfLine(text, to + 1);
            below++;
        }

        var builder = new StringBuilder((above + below) * 64);

        Continued(builder, text[from..window.Start], window.Line - above);
        Continued(builder, text[Math.Min(window.End + 1, to)..to], window.Line + 1);

        return builder.ToString();
    }

    private static void Continued(StringBuilder builder, ReadOnlySpan<char> block, int firstLine)
    {
        var lines = WithoutFinalBreak(block);
        var number = firstLine;

        while (!lines.IsEmpty)
        {
            var end = lines.IndexOf('\n');
            var line = end >= 0 ? lines[..end] : lines;

            builder.Append('\n').Append(CultureInfo.InvariantCulture, $"    {number}: {Shorten(line.TrimEnd())}");
            number++;
            lines = end >= 0 ? lines[(end + 1)..] : default;
        }
    }

    private static ReadOnlySpan<char> WithoutFinalBreak(ReadOnlySpan<char> block) => block switch
    {
        [.., '\r', '\n'] => block[..^2],
        [.., '\n'] => block[..^1],
        _ => block,
    };

    private readonly record struct LineWindow(int Start, int End, int Line);

    private readonly record struct TextMatch(int At, int Length, int Query);

    private static readonly TextMatch Missing = new(-1, 0, 0);
    private static readonly SearchValues<char> Blank = SearchValues.Create(" \t\r\n\f\v");

    private static int EndOfLine(ReadOnlySpan<char> text, int at) =>
        text[at..].IndexOf('\n') is var offset and >= 0 ? at + offset : text.Length;

    private static string Shorten(ReadOnlySpan<char> line) => line.Length <= MaxLineLength
        ? new string(line)
        : string.Create(CultureInfo.InvariantCulture, $"{line[..MaxLineLength]}... (+{line.Length - MaxLineLength} chars)");

    private static string Render(TextSearchRequest request, FileHits[] perFile)
    {
        var response = new ResponseBuilder(request.Tool, Argument(request));
        var total = 0;
        var skipped = 0;

        foreach (var file in perFile)
        {
            total += file.Total;
            skipped += file.Skipped;
        }

        var cap = ResultCap.Shown(total, request.MaxResults);
        var shown = new List<string>(Math.Min(cap, 512));

        foreach (var file in perFile)
            shown.AddRange(file.Hits.Take(Math.Max(0, cap - shown.Count)));

        var tally = new SearchTally(shown.Count, total, skipped);

        return request.Unique
            ? Write(response, Collapsed(shown), request, tally)
            : Write(response, shown, request, tally);
    }
    private readonly record struct SearchTally(int Shown, int Total, int Skipped);

    private static List<string> Collapsed(List<string> shown)
    {
        var counts = new Dictionary<string, int>(shown.Count, StringComparer.Ordinal);
        var first = new List<(string Hit, string Key)>(shown.Count);

        foreach (var hit in shown)
        {
            var key = Payload(hit);

            if (counts.TryAdd(key, 1))
                first.Add((hit, key));
            else
                counts[key]++;
        }

        return [.. first.Select(entry => counts[entry.Key] is var count && count > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{entry.Hit}  x{count}")
            : entry.Hit)];
    }

    private static string Payload(string hit) =>
    hit.IndexOf(RecordSeparator, StringComparison.Ordinal) is var at and >= 0 ? hit[(at + RecordSeparator.Length)..] : hit;

    private const string RecordSeparator = "  ";

    private static string Write(ResponseBuilder response, List<string> records, TextSearchRequest request, SearchTally tally)
    {
        response.Summary(tally.Shown, tally.Total, "matches", "glob= or maxResults=");

        Annotate(response, records.Count, request, tally);

        foreach (var record in records)
            response.Line(record);

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

    private readonly record struct TextMatcher(ImmutableArray<string> Patterns, ImmutableArray<Regex> Expressions, ImmutableArray<byte[]> Needles)
    {
        public static TextMatcher Create(ImmutableArray<string> patterns, bool regex) => regex
            ? new(patterns, Compiled(patterns), [])
            : new(patterns, [], Encoded(patterns));

        public bool MayContain(ReadOnlySpan<byte> content)
        {
            if (Needles.IsEmpty)
                return true;

            foreach (var needle in Needles)
            {
                if (content.IndexOf(needle) >= 0)
                    return true;
            }

            return false;
        }

        public TextMatch Next(ReadOnlySpan<char> text, int from)
        {
            var best = Missing;

            for (var query = 0; query < Patterns.Length; query++)
            {
                var found = Find(text, from, query);

                if (found.At >= 0 && (best.At < 0 || found.At < best.At))
                    best = found;
            }

            return best;
        }

        private TextMatch Find(ReadOnlySpan<char> text, int from, int query)
        {
            if (Expressions.IsEmpty)
            {
                return text[from..].IndexOf(Patterns[query], StringComparison.Ordinal) is var offset and >= 0
                    ? new TextMatch(from + offset, Patterns[query].Length, query)
                    : Missing;
            }

            foreach (var match in Expressions[query].EnumerateMatches(text, from))
                return new TextMatch(match.Index, match.Length, query);

            return Missing;
        }

        private static ImmutableArray<Regex> Compiled(ImmutableArray<string> patterns)
        {
            var expressions = ImmutableArray.CreateBuilder<Regex>(patterns.Length);

            foreach (var pattern in patterns)
                expressions.Add(Compile(pattern));

            return expressions.MoveToImmutable();
        }

        private static ImmutableArray<byte[]> Encoded(ImmutableArray<string> patterns)
        {
            var needles = ImmutableArray.CreateBuilder<byte[]>(patterns.Length);

            foreach (var pattern in patterns)
                needles.Add(Encoding.UTF8.GetBytes(pattern));

            return needles.MoveToImmutable();
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

        public bool MatchesLine(ReadOnlySpan<char> line, int query) => Expressions.IsEmpty
            ? line.IndexOf(Patterns[query], StringComparison.Ordinal) >= 0
            : Expressions[query].IsMatch(line);
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
        TextSearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProbeAsync(file, matcher, request, cancellationToken).ConfigureAwait(false);
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
        TextSearchRequest request,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(file.FullPath, Options());

        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanSeek)
                return Text(file.RelativePath, await StreamedAsync(stream, cancellationToken).ConfigureAwait(false), matcher, request);

            return stream.Length > MaxSearchableBytes
                ? FileHits.Oversized
                : await BufferedAsync(stream, file.RelativePath, matcher, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> StreamedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileHits Text(string relativePath, string text, TextMatcher matcher, TextSearchRequest request) =>
        IsBinary(text) ? FileHits.None : Collect(relativePath, text, matcher, request);

    private static async Task<FileHits> BufferedAsync(
        FileStream stream,
        string relativePath,
        TextMatcher matcher,
        TextSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (await LooksBinaryAsync(stream, cancellationToken).ConfigureAwait(false))
            return FileHits.None;

        stream.Position = 0;

        var length = Math.Max((int)stream.Length, 1);
        var buffer = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            var filled = await FillAsync(stream, buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

            return Scan(relativePath, buffer, filled, matcher, request);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<bool> LooksBinaryAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var probe = ArrayPool<byte>.Shared.Rent(BinaryProbe);

        try
        {
            var read = await FillAsync(stream, probe.AsMemory(0, BinaryProbe), cancellationToken).ConfigureAwait(false);

            return LooksBinary(probe.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(probe);
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

    private static FileHits Scan(string relativePath, byte[] buffer, int length, TextMatcher matcher, TextSearchRequest request)
    {
        var content = buffer.AsSpan(0, length);

        return !IsWideText(content) && !matcher.MayContain(content)
            ? FileHits.None
            : Text(relativePath, Decode(content), matcher, request);
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

    private static string Stamped(WorkspacePath file)
    {
        var stamp = FileStamp.Of(file.FullPath);

        return stamp == FileStamp.Missing
            ? string.Create(CultureInfo.InvariantCulture, $"{file.RelativePath}  MISSING")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{file.RelativePath}  {new DateTime(stamp.Ticks, DateTimeKind.Utc):yyyy-MM-ddTHH:mm:ssZ}  {stamp.Length}");
    }

    private static List<WorkspacePath> Kept(List<WorkspacePath> files, string? exclude)
    {
        if (exclude is not { Length: > 0 })
            return files;

        var matcher = FileGlob.Compile(exclude);
        var kept = new List<WorkspacePath>(files.Count);
        foreach (var file in files)
        {
            if (!matcher.MatchesRelative(file.RelativePath))
                kept.Add(file);
        }

        return kept;
    }

    private static List<WorkspacePath> Tracked(List<WorkspacePath> files, IReadOnlySet<string>? tracked)
    {
        if (tracked is null)
            return files;

        var kept = new List<WorkspacePath>(files.Count);

        foreach (var file in files)
        {
            if (tracked.Contains(file.RelativePath))
                kept.Add(file);
        }

        return kept;
    }

    private static readonly string[] QueryTags = ["q1", "q2", "q3", "q4", "q5", "q6", "q7", "q8", "q9", "q10"];

    private static string Tag(int query) => query < QueryTags.Length
        ? QueryTags[query]
        : string.Create(CultureInfo.InvariantCulture, $"q{query + 1}");

    private static string Argument(TextSearchRequest request) =>
        request.SeveralPatterns ? string.Join(RecordSeparator, request.Patterns) : request.Pattern;

    private static void Annotate(ResponseBuilder response, int records, TextSearchRequest request, SearchTally tally)
    {
        if (records > 0)
            response.Note("HEURISTIC  text match");

        if (request.Root is { Length: > 0 } root)
            response.Note("outside-workspace  " + root);

        if (records < tally.Shown)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"unique: {records} distinct line(s) collapsed from the {tally.Shown} shown; the x<count> on a record counts only what was shown"));
        }

        if (tally.Skipped > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"skipped {tally.Skipped} files over {MaxSearchableBytes / (1024 * 1024)} MB"));
    }

    private static int Other(TextMatcher matcher, ReadOnlySpan<char> line, int first, int from)
    {
        for (var query = from; query < matcher.Patterns.Length; query++)
        {
            if (query != first && matcher.MatchesLine(line, query))
                return query;
        }

        return -1;
    }

    private static string Combined(TextMatcher matcher, ReadOnlySpan<char> line, int first)
    {
        var builder = new StringBuilder(16);

        for (var query = 0; query < matcher.Patterns.Length; query++)
        {
            if (query != first && !matcher.MatchesLine(line, query))
                continue;

            if (builder.Length > 0)
                builder.Append(',');

            builder.Append(Tag(query));
        }

        return builder.ToString();
    }

    private static string Tagged(TextMatcher matcher, ReadOnlySpan<char> line, int first) =>
        Other(matcher, line, first, 0) < 0 ? Tag(first) : Combined(matcher, line, first);
}
