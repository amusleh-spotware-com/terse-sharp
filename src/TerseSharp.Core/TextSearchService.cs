using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class TextSearchService
{
    private const int MaxLineLength = 200;

    private const int BinaryProbe = 4096;

    private const long MaxSearchableBytes = 16L * 1024 * 1024;

    private static readonly string[] ExcludedDirectories = [".git", "bin", "obj", "node_modules", ".vs", ".idea"];

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
        var candidates = Files(workspace.Root, glob).Where(IsSearchable).ToArray();
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
        var files = Files(workspace.Root, glob).ToArray();
        var response = new ResponseBuilder("find_files", glob);

        response.Summary(Math.Min(files.Length, maxResults), files.Length, "files", "a narrower glob= or maxResults=");

        foreach (var file in files.Take(maxResults))
            response.Line(file.RelativePath);

        return response.ToString();
    }

    private static bool IsSearchable(SourceCandidate file) =>
        !BinaryExtensions.Contains(Path.GetExtension(file.FullPath));

    private static async Task<FileHits> ScanAsync(
        SourceCandidate file,
        TextMatcher matcher,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (file.Length > MaxSearchableBytes)
            return FileHits.Oversized;

        var text = await ReadAsync(file.FullPath, cancellationToken).ConfigureAwait(false);

        return text is null || IsBinary(text) ? FileHits.None : Collect(file.RelativePath, text, matcher, maxResults);
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

    private static async Task<string?> ReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            var stream = new FileStream(file, Options());

            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);

                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStreamOptions Options() => new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    };

    private static IEnumerable<SourceCandidate> Files(string root, string glob)
    {
        var matcher = FileGlob.Compile(string.IsNullOrWhiteSpace(glob) ? "*" : glob);

        return Walk(root).Where(file => matcher.MatchesRelative(file.RelativePath));
    }

    private static IEnumerable<SourceCandidate> Walk(string root)
    {
        var pending = new Stack<string>();

        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in Subdirectories(directory))
                pending.Push(child);

            foreach (var file in Entries(directory))
                yield return new SourceCandidate(file.FullName, Path.GetRelativePath(root, file.FullName), file.Length);
        }
    }

    private static IEnumerable<string> Subdirectories(string directory) =>
        Directories(directory).Where(child => !ExcludedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase));

    private static FileInfo[] Entries(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).GetFiles();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] Directories(string directory)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(directory)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private readonly record struct SourceCandidate(string FullPath, string RelativePath, long Length);

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

    private readonly record struct TextMatcher(string Pattern, Regex? Expression)
    {
        public static TextMatcher Create(string pattern, bool regex) => new(pattern, regex ? Compile(pattern) : null);

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
                return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
            }
            catch (NotSupportedException)
            {
                return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
        }
    }
}
