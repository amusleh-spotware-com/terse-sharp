namespace TerseSharp.Core;

public static class ClassicSolutionMapping
{
    private const string SolutionPlatforms = "GlobalSection(SolutionConfigurationPlatforms)";

    private const string ProjectPlatforms = "GlobalSection(ProjectConfigurationPlatforms)";

    public static SolutionMapping Read(string[] lines)
    {
        var declared = Section(lines, SolutionPlatforms).Select(Declared).OfType<BuildConfiguration>().ToArray();
        var active = new BuildConfiguration(
            ConfigurationMapping.Preferred([.. declared.Select(entry => entry.Configuration)], "Debug"),
            ConfigurationMapping.PreferredPlatform([.. declared.Select(entry => entry.Platform)]));
        var key = "." + active.ToString() + ".ActiveCfg = ";
        var paths = Paths(lines);

        return new(active, [.. Section(lines, ProjectPlatforms).Select(line => Mapped(line, key, paths)).OfType<ProjectMapping>()]);
    }

    private static IEnumerable<string> Section(string[] lines, string header) =>
        lines.Select(line => line.Trim())
            .SkipWhile(line => !line.StartsWith(header, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.Equals("EndGlobalSection", StringComparison.Ordinal));

    private static BuildConfiguration? Declared(string line) =>
        line.AsSpan().IndexOf(" = ", StringComparison.Ordinal) is var equals and >= 0 ? Pair(line.AsSpan(0, equals)) : null;

    private static BuildConfiguration? Pair(ReadOnlySpan<char> text) =>
        text.IndexOf('|') is var bar and >= 0
            ? new BuildConfiguration(new string(text[..bar].Trim()), new string(text[(bar + 1)..].Trim()))
            : null;

    private static ProjectMapping? Mapped(string line, string key, Dictionary<string, string> paths)
    {
        var at = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);

        return at > 0 && paths.TryGetValue(line[..at], out var path) && Pair(line.AsSpan(at + key.Length)) is { } built
            ? new(path, built)
            : null;
    }

    private static Dictionary<string, string> Paths(string[] lines)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.StartsWith("Project(", StringComparison.Ordinal) && Fields(line) is ({ } path, { } guid))
                paths[guid] = path;
        }

        return paths;
    }

    private static (string? Path, string? Guid) Fields(string line)
    {
        var text = line.AsSpan();
        string? path = null;
        var field = 0;

        foreach (var range in text.Split('"'))
        {
            switch (field++)
            {
                case 5:
                    path = SolutionFile.Slashed(text[range]);
                    break;
                case 7:
                    return (path, new string(text[range]));
            }
        }

        return (path, null);
    }
}
