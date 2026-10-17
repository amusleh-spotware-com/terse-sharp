namespace TerseSharp.Core;

public readonly record struct BuildConfiguration(string Configuration, string Platform)
{
    public static readonly BuildConfiguration Default = new("Debug", "AnyCPU");

    public bool Same(BuildConfiguration other) =>
        Configuration.Equals(other.Configuration, StringComparison.OrdinalIgnoreCase) && SamePlatform(Platform, other.Platform);

    public static bool SamePlatform(ReadOnlySpan<char> left, ReadOnlySpan<char> right) =>
        (IsAnyCpu(left) && IsAnyCpu(right)) || left.Equals(right, StringComparison.OrdinalIgnoreCase);

    public static bool IsAnyCpu(ReadOnlySpan<char> platform) =>
        platform.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase) || platform.Equals("Any CPU", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Configuration + "|" + Platform;
}

public sealed record ProjectMapping(string ProjectPath, BuildConfiguration Built);

public sealed record SolutionMapping(BuildConfiguration Active, IReadOnlyList<ProjectMapping> Projects)
{
    public static readonly SolutionMapping None = new(new BuildConfiguration("Debug", "Any CPU"), []);
}

public static class ConfigurationMapping
{
    private static readonly string[] EvaluatedNames = ["Configuration", "Platform"];

    public static async Task<IReadOnlyList<string>> MismatchesAsync(string solutionPath, CancellationToken cancellationToken)
    {
        var mapping = await ReadAsync(solutionPath, cancellationToken).ConfigureAwait(false);
        var candidates = mapping.Projects.Where(project => !project.Built.Same(BuildConfiguration.Default)).ToArray();

        return candidates.Length is 0
            ? []
            : await Task.Run(() => Compared(solutionPath, mapping.Active, candidates, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SolutionMapping> ReadAsync(string solutionPath, CancellationToken cancellationToken) =>
        (SolutionFile.IsXml(solutionPath), solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) switch
        {
            (true, _) => XmlSolutionMapping.Read(await SolutionFile.LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false)),
            (_, true) => ClassicSolutionMapping.Read(await File.ReadAllLinesAsync(solutionPath, cancellationToken).ConfigureAwait(false)),
            _ => SolutionMapping.None,
        };

    internal static string Preferred(string[] names, string wanted) =>
        names.Length is 0 || names.Contains(wanted, StringComparer.OrdinalIgnoreCase) ? wanted : names[0];

    internal static string PreferredPlatform(string[] names) =>
        names.Contains("Mixed Platforms", StringComparer.OrdinalIgnoreCase) ? "Mixed Platforms" : Preferred(names, "Any CPU");

    private static List<string> Compared(string solutionPath, BuildConfiguration active, ProjectMapping[] candidates, CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        var mismatches = new List<string>(candidates.Length);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Evaluated(root, candidate) is { } loaded && !loaded.Same(candidate.Built))
                mismatches.Add(Mismatch(candidate, active, loaded));
        }

        return mismatches;
    }

    private static BuildConfiguration? Evaluated(string root, ProjectMapping candidate) =>
        ProjectEvaluation.Values(Path.GetFullPath(Path.Combine(root, candidate.ProjectPath)), EvaluatedNames) is [var configuration, var platform]
            ? new BuildConfiguration(configuration, platform)
            : null;

    private static string Mismatch(ProjectMapping candidate, BuildConfiguration active, BuildConfiguration loaded) =>
        "WARNING config-mapping " + candidate.ProjectPath + " builds as " + candidate.Built.ToString() + " under " + active.ToString()
        + ", loaded as " + loaded.ToString() + " - get_diagnostics and errors= severities follow the loaded one, build follows the solution";
}
