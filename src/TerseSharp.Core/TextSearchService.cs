using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class TextSearchService
{
    private const int MaxLineLength = 200;

    private const long MaxSearchableBytes = 16L * 1024 * 1024;

    private static readonly string[] ExcludedDirectories = [".git", "bin", "obj", "node_modules", ".vs", ".idea"];

    private static readonly FrozenSet<string> BinaryExtensions = new[]
    {
        ".dll", ".exe", ".pdb", ".so", ".dylib", ".lib", ".obj", ".res", ".resources", ".cache",
        ".zip", ".7z", ".gz", ".tar", ".nupkg", ".snupkg", ".bin", ".dat", ".db", ".sqlite",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp", ".pdf",
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".mp3", ".mp4", ".wav", ".snk", ".pfx",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string Search(LoadedWorkspace workspace, string pattern, string glob, bool regex, int maxResults)
    {
        var matcher = TextMatcher.Create(pattern, regex);
        var hits = new List<string>(Math.Min(maxResults, 512));
        var total = 0;
        var skipped = 0;

        foreach (var file in Files(workspace.Root, glob).Where(IsSearchable))
        {
            if (TooLarge(file.FullPath))
                skipped++;
            else
                total += Scan(file, matcher, hits, maxResults);
        }

        return Render(regex ? "search_regex" : "search_text", pattern, hits, total, skipped);
    }

    private static bool TooLarge(string path)
    {
        try
        {
            return new FileInfo(path).Length > MaxSearchableBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsSearchable(SourceCandidate file) =>
        !BinaryExtensions.Contains(Path.GetExtension(file.FullPath));

    public static string FindFiles(LoadedWorkspace workspace, string glob, int maxResults)
    {
        var files = Files(workspace.Root, glob).ToArray();
        var response = new ResponseBuilder("find_files", glob);

        response.Summary(Math.Min(files.Length, maxResults), files.Length, "files");

        foreach (var file in files.Take(maxResults))
            response.Line(file.RelativePath);

        return response.ToString();
    }

    private static int Scan(SourceCandidate file, TextMatcher matcher, List<string> hits, int maxResults)
    {
        var found = 0;
        var lineNumber = 0;

        foreach (var line in ReadLines(file.FullPath))
        {
            lineNumber++;

            if (line.Contains('\0'))
                break;

            if (!matcher.Matches(line))
                continue;

            found++;
            Collect(hits, maxResults, file.RelativePath, lineNumber, line);
        }

        return found;
    }

    private static void Collect(List<string> hits, int maxResults, string relativePath, int lineNumber, string line)
    {
        if (hits.Count >= maxResults)
            return;

        hits.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{relativePath}:{lineNumber}  HEURISTIC  {Shorten(line.Trim())}"));
    }

    private static string Shorten(string line) => line.Length <= MaxLineLength
        ? line
        : string.Create(CultureInfo.InvariantCulture, $"{line[..MaxLineLength]}... (+{line.Length - MaxLineLength} chars)");

    private static string Render(string tool, string pattern, List<string> hits, int total, int skipped)
    {
        var response = new ResponseBuilder(tool, pattern);

        response.Summary(hits.Count, total, "matches");

        if (skipped > 0)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"skipped {skipped} files over {MaxSearchableBytes / (1024 * 1024)} MB"));

        foreach (var hit in hits)
            response.Line(hit);

        return response.ToString();
    }

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

            foreach (var file in Entries(directory, Directory.EnumerateFiles))
                yield return new SourceCandidate(file, Path.GetRelativePath(root, file));
        }
    }

    private static IEnumerable<string> Subdirectories(string directory) =>
        Entries(directory, Directory.EnumerateDirectories)
            .Where(child => !ExcludedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase));

    private static string[] Entries(string directory, Func<string, IEnumerable<string>> enumerate)
    {
        try
        {
            return [.. enumerate(directory)];
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

    private static IEnumerable<string> ReadLines(string file)
    {
        using var reader = TryOpen(file);

        if (reader is null)
            yield break;

        while (TryReadLine(reader) is { } line)
            yield return line;
    }

    private static StreamReader? TryOpen(string file)
    {
        try
        {
            return new StreamReader(File.Open(
                file,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                }));
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

    private static string? TryReadLine(StreamReader reader)
    {
        try
        {
            return reader.ReadLine();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private readonly record struct SourceCandidate(string FullPath, string RelativePath);

    private readonly record struct TextMatcher(string Pattern, Regex? Expression)
    {
        public static TextMatcher Create(string pattern, bool regex) => new(pattern, regex ? Compile(pattern) : null);

        public bool Matches(string line) =>
            Expression is null ? line.Contains(Pattern, StringComparison.Ordinal) : Expression.IsMatch(line);

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
