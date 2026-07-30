using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public static class TextSearchService
{
    private static readonly string[] ExcludedDirectories = [".git", "bin", "obj", "node_modules", ".vs", ".idea"];

    public static string Search(LoadedWorkspace workspace, string pattern, string glob, bool regex, int maxResults)
    {
        var matcher = regex ? new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2)) : null;
        var hits = new List<string>(maxResults);
        var total = 0;

        foreach (var file in Files(workspace.Root, glob))
            total += Scan(file, workspace.Root, pattern, matcher, hits, maxResults);

        return Render(regex ? "search_regex" : "search_text", pattern, hits, total);
    }

    public static string FindFiles(LoadedWorkspace workspace, string glob, int maxResults)
    {
        var files = Files(workspace.Root, glob).Take(maxResults + 1).ToArray();
        var response = new ResponseBuilder("find_files", glob);

        response.Summary(Math.Min(files.Length, maxResults), files.Length, "files");

        foreach (var file in files.Take(maxResults))
            response.Line(Path.GetRelativePath(workspace.Root, file));

        return response.ToString();
    }

    private static int Scan(
        string file,
        string root,
        string pattern,
        Regex? matcher,
        List<string> hits,
        int maxResults)
    {
        var found = 0;
        var relative = Path.GetRelativePath(root, file);
        var lineNumber = 0;

        foreach (var line in File.ReadLines(file))
        {
            lineNumber++;

            if (!Matches(line, pattern, matcher))
                continue;

            found++;

            if (hits.Count < maxResults)
                hits.Add(string.Create(CultureInfo.InvariantCulture, $"{relative}:{lineNumber}  HEURISTIC  {line.Trim()}"));
        }

        return found;
    }

    private static bool Matches(string line, string pattern, Regex? matcher) =>
        matcher is null ? line.Contains(pattern, StringComparison.Ordinal) : matcher.IsMatch(line);

    private static string Render(string tool, string pattern, List<string> hits, int total)
    {
        var response = new ResponseBuilder(tool, pattern);

        response.Summary(hits.Count, total, "matches");

        foreach (var hit in hits)
            response.Line(hit);

        return response.ToString();
    }

    private static IEnumerable<string> Files(string root, string glob) =>
        Directory
            .EnumerateFiles(root, string.IsNullOrWhiteSpace(glob) ? "*" : glob, SearchOption.AllDirectories)
            .Where(file => !IsExcluded(file, root));

    private static bool IsExcluded(string file, string root) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
