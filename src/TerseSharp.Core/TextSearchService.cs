using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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
            ? await ScannedAsync(Kept([.. candidates.Value!.Where(IsSearchableFile)], request.Exclude), request, cancellationToken).ConfigureAwait(false)
            : candidates.Error!.Render();
    }
    private static async Task<string> ScannedAsync(
        List<WorkspacePath> files,
        TextSearchRequest request,
        CancellationToken cancellationToken)
    {
        var matcher = TextMatcher.Create(request.Patterns, request.Regex, request.Word);
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

            if (matcher.MatchesRelative(relative))
                matched.Add(new WorkspacePath(file, relative));
        }

        return Result.Ok(matched);
    }

    public static string FindFiles(
            LoadedWorkspace workspace,
            string glob,
            int maxResults,
            bool stamps,
            IReadOnlySet<string>? tracked = null,
            string? name = null,
            int depth = 0,
            bool chosen = false) =>
            Rendered(Tracked(Matched(workspace, glob), tracked), glob, maxResults, stamps, name, depth, null, chosen);

    private static List<WorkspacePath> Named(List<WorkspacePath> files, string? name)
    {
        if (name is not { Length: > 0 } text)
            return files;

        var kept = new List<WorkspacePath>(files.Count);

        foreach (var file in files)
        {
            if (Path.GetFileName(file.RelativePath.AsSpan()).Contains(text, StringComparison.OrdinalIgnoreCase))
                kept.Add(file);
        }

        return kept;
    }

    private static bool IsBinary(string text) =>
        text.AsSpan(0, Math.Min(text.Length, BinaryProbe)).Contains('\0');

    private static FileHits Collect(string relativePath, string text, TextMatcher matcher, TextSearchRequest request)
    {
        var span = text.AsSpan();
        var hits = new List<string>();
        var tracker = new LineTracker();
        var counts = request.CountOnly ? new int[request.Patterns.Length] : [];
        var total = 0;
        var index = 0;
        SyntaxNode? root = null;

        while (index < span.Length && matcher.Next(span, index) is { At: >= 0 } match)
        {
            total++;

            if (request.CountOnly)
            {
                TallyLine(span, match, matcher, counts);
            }
            else if (hits.Count < request.MaxResults)
            {
                root ??= Rooted(relativePath, text, request);
                hits.Add(Format(relativePath, span, match, ref tracker, new HitContext(request, matcher, root)));
            }

            index = EndOfLine(span, Resumed(match, request)) + 1;
        }

        return new FileHits(Rows(relativePath, hits, total, counts, request), total, 0);
    }

    private static int Content(ReadOnlySpan<char> text, TextMatch match)
    {
        var offset = text.Slice(match.At, match.Length).IndexOfAnyExcept(Blank);

        return offset < 0 ? match.At : match.At + offset;
    }

    private static string Format(string relativePath, ReadOnlySpan<char> text, TextMatch match, ref LineTracker tracker, in HitContext context)
    {
        var request = context.Request;
        var at = Content(text, match);

        tracker.Advance(text, at);

        var start = text[..at].LastIndexOf('\n') + 1;
        var end = EndOfLine(text, at);
        var matched = text.Slice(match.At, match.Length).Trim();
        var payload = request.MatchesOnly && matched.Length > 0 ? matched : text[start..end].Trim();
        var tagged = request.SeveralPatterns ? Tagged(context.Matcher, text[start..end], match.Query) + RecordSeparator : string.Empty;
        var hit = string.Create(
            CultureInfo.InvariantCulture,
            $"{relativePath}:{tracker.Line}{RecordSeparator}{tagged}{Container(context.Root, at)}{Shorten(payload)}");

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
        if (request.CountOnly)
            return Counts(request, perFile);

        var response = new ResponseBuilder(request.Tool, Argument(request)).Chosen(request.Chosen);
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

        if (request.Root is not { Length: > 0 } && ArgumentLine.Paths(records) is { } batch)
            response.Note(batch);

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
        public static TextMatcher Create(ImmutableArray<string> patterns, bool regex, bool word) => regex
            ? new(patterns, Compiled(patterns), []) { Word = word }
            : new(patterns, [], Encoded(patterns)) { Word = word };

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

        private TextMatch Find(ReadOnlySpan<char> text, int from, int query) => Expressions.IsEmpty
            ? Literal(text, from, query)
            : Expressed(text, from, query);

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
            ? Literal(line, 0, query).At >= 0
            : Expressions[query].IsMatch(line);

        public bool Word { get; init; }

        private TextMatch Literal(ReadOnlySpan<char> text, int from, int query)
        {
            var pattern = Patterns[query];
            var at = from;

            while (at <= text.Length)
            {
                var offset = text[at..].IndexOf(pattern, StringComparison.Ordinal);

                if (offset < 0)
                    return Missing;

                var start = at + offset;

                if (!Word || Bounded(text, start, pattern.Length))
                    return new TextMatch(start, pattern.Length, query);

                at = start + 1;
            }

            return Missing;
        }

        private TextMatch Expressed(ReadOnlySpan<char> text, int from, int query)
        {
            foreach (var match in Expressions[query].EnumerateMatches(text, from))
                return new TextMatch(match.Index, match.Length, query);

            return Missing;
        }

        private static bool Bounded(ReadOnlySpan<char> text, int start, int length) =>
            !IsWordCharacter(text, start - 1) && !IsWordCharacter(text, start + length);

        private static bool IsWordCharacter(ReadOnlySpan<char> text, int at) =>
            (uint)at < (uint)text.Length && (char.IsLetterOrDigit(text[at]) || text[at] is '_');
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

    private static int Resumed(TextMatch match, TextSearchRequest request) => request.SeveralPatterns
        ? match.At
        : match.At + Math.Max(match.Length - 1, 0);

    private readonly record struct FileRow(string Text, string? Path, int Files);

    private static List<FileRow> Listed(List<WorkspacePath> files, bool stamps)
    {
        var rows = new List<FileRow>(files.Count);

        foreach (var file in files)
            rows.Add(Row(file, stamps));

        return rows;
    }

    private static List<FileRow> Rolled(List<WorkspacePath> files, int depth, bool stamps)
    {
        var counts = Counted(files, depth);
        var byPrefix = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var folded = seen.GetAlternateLookup<ReadOnlySpan<char>>();
        var rows = new List<FileRow>(counts.Count);

        foreach (var file in files)
        {
            var prefix = Prefix(file.RelativePath, depth);

            if (prefix.IsEmpty || byPrefix[prefix] is 1)
                rows.Add(Row(file, stamps));
            else if (folded.Add(prefix))
                rows.Add(new FileRow(Rollup(prefix, byPrefix[prefix]), null, byPrefix[prefix]));
        }

        return rows;
    }

    private static Dictionary<string, int> Counted(List<WorkspacePath> files, int depth)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var byPrefix = counts.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var file in files)
        {
            var prefix = Prefix(file.RelativePath, depth);

            if (!prefix.IsEmpty && !byPrefix.TryAdd(prefix, 1))
                byPrefix[prefix] += 1;
        }

        return counts;
    }

    private static ReadOnlySpan<char> Prefix(ReadOnlySpan<char> path, int depth)
    {
        var at = 0;

        for (var segment = 0; segment < depth; segment++)
        {
            var next = path[at..].IndexOfAny('/', '\\');

            if (next < 0)
                return default;

            at += next + 1;
        }

        return at is 0 ? default : path[..(at - 1)];
    }

    private static string Rollup(ReadOnlySpan<char> prefix, int files)
    {
        var buffer = prefix.Length <= MaxPrefix ? stackalloc char[MaxPrefix] : new char[prefix.Length];
        var directory = buffer[..prefix.Length];

        prefix.CopyTo(directory);
        directory.Replace('\\', '/');

        return string.Create(CultureInfo.InvariantCulture, $"{directory}/**  x{files} files");
    }

    private static FileRow Row(WorkspacePath file, bool stamps) =>
        new(stamps ? Stamped(file) : file.RelativePath, file.RelativePath, 1);

    private static int Covered(FileRow[] rows)
    {
        var files = 0;

        foreach (var row in rows)
            files += row.Files;

        return files;
    }

    private static void Batch(ResponseBuilder response, FileRow[] rows, string? root)
    {
        var paths = new List<string>(rows.Length);

        foreach (var row in rows)
        {
            if (row.Path is { } path)
                paths.Add(root is { Length: > 0 } ? Path.Combine(root, path) : path);
        }

        if (ArgumentLine.Paths(paths) is { } batch)
            response.Note(batch);
    }

    private const int MaxPrefix = 512;

    private static string Rendered(
            List<WorkspacePath> matched,
            string glob,
            int maxResults,
            bool stamps,
            string? name,
            int depth,
            string? root,
            bool chosen = false)
    {
        var files = Named(matched, name);
        var rows = depth > 0 ? Rolled(files, depth, stamps) : Listed(files, stamps);
        var response = new ResponseBuilder("find_files", glob).Chosen(chosen);
        var shown = rows.Capped(maxResults).ToArray();

        response.Summary(Covered(shown), files.Count, "files", "a narrower glob=, a smaller depth= or maxResults=");

        foreach (var row in shown)
            response.Line(row.Text);

        if (root is { Length: > 0 })
            response.Note("outside-workspace  " + root);

        if (files.Count is 0 && name is not { Length: > 0 })
            response.Note("no file matched - pass name=<text> to match a file name substring instead of a glob");

        Batch(response, shown, root);

        return response.ToString();
    }

    public static Result<string> FindFilesOutside(
            string root,
            string glob,
            int maxResults,
            bool stamps,
            string? name = null,
            int depth = 0,
            bool chosen = false)
    {
        var candidates = Outside(root, glob);

        return candidates.IsOk
            ? Result.Ok(Rendered(candidates.Value!, glob, maxResults, stamps, name, depth, Path.GetFullPath(root), chosen))
            : Result.Fail<string>(candidates.Error!);
    }

    private static void TallyLine(ReadOnlySpan<char> text, TextMatch match, TextMatcher matcher, int[] counts)
    {
        var start = text[..match.At].LastIndexOf('\n') + 1;
        var line = text[start..EndOfLine(text, match.At)];
        var tagged = false;

        for (var query = 0; query < counts.Length; query++)
        {
            if (!matcher.MatchesLine(line, query))
                continue;

            counts[query]++;
            tagged = true;
        }

        if (!tagged)
            counts[match.Query]++;
    }

    private static List<string> Rows(string relativePath, List<string> hits, int total, int[] counts, TextSearchRequest request)
    {
        if (!request.CountOnly)
            return hits;

        return total is 0 ? [] : [Tally(relativePath, total, counts, request)];
    }

    private static string Tally(string relativePath, int total, int[] counts, TextSearchRequest request) => request.SeveralPatterns
            ? string.Create(CultureInfo.InvariantCulture, $"{relativePath}{RecordSeparator}{total}{RecordSeparator}{Tags(counts)}")
            : string.Create(CultureInfo.InvariantCulture, $"{relativePath}{RecordSeparator}{total}");

    private static string Tags(int[] counts)
    {
        var parts = new List<string>(counts.Length);

        for (var query = 0; query < counts.Length; query++)
        {
            if (counts[query] > 0)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Tag(query)}={counts[query]}"));
        }

        return string.Join(' ', parts);
    }

    private static (List<string> Rows, int Matched, int Lines, int Skipped) Tallied(FileHits[] perFile, int maxResults)
    {
        var rows = new List<string>();
        var matched = 0;
        var lines = 0;
        var skipped = 0;

        foreach (var file in perFile)
        {
            lines += file.Total;
            skipped += file.Skipped;

            if (file.Hits.Count is 0)
                continue;

            matched++;

            if (rows.Count < maxResults)
                rows.Add(file.Hits[0]);
        }

        return (rows, matched, lines, skipped);
    }

    private static string Counts(TextSearchRequest request, FileHits[] perFile)
    {
        var response = new ResponseBuilder(request.Tool, Argument(request)).Chosen(request.Chosen);
        var tallied = Tallied(perFile, request.MaxResults);

        response.Summary(tallied.Rows.Count, tallied.Matched, "files", "glob= or maxResults=");
        Annotate(response, tallied.Rows.Count, request, new SearchTally(tallied.Rows.Count, tallied.Matched, tallied.Skipped));
        response.Note(string.Create(CultureInfo.InvariantCulture, $"{tallied.Lines} matching lines"));

        foreach (var row in tallied.Rows)
            response.Line(row);

        return response.ToString();
    }

    private readonly record struct HitContext(TextSearchRequest Request, TextMatcher Matcher, SyntaxNode? Root);

    private static SyntaxNode? Rooted(string relativePath, string text, TextSearchRequest request) =>
        request.Containers && SourceFile.IsCSharp(relativePath) ? CSharpSyntaxTree.ParseText(text).GetRoot() : null;

    private static string Container(SyntaxNode? root, int at)
    {
        if (root is null)
            return string.Empty;

        return UsageContainer.Of(root, new TextSpan(at, 0)) is { } declaration
            ? declaration + RecordSeparator
            : string.Empty;
    }

    public static string FindFilesMany(
        LoadedWorkspace workspace,
        IReadOnlyList<string> globs,
        int maxResults,
        bool stamps,
        IReadOnlySet<string>? tracked = null,
        string? name = null,
        int depth = 0,
        bool chosen = false)
    {
        var response = new ResponseBuilder("find_files", string.Join(", ", globs));

        response.Summary(globs.Count, globs.Count, "globs");

        foreach (var glob in globs)
        {
            response.Note(glob);
            response.Line(FindFiles(workspace, glob, maxResults, stamps, tracked, name, depth, chosen).TrimEnd('\n'));
        }

        return response.ToString();
    }
}
