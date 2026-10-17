using System.Xml.Linq;

namespace TerseSharp.Core;

public static class XmlSolutionMapping
{
    public static SolutionMapping Read(XDocument document)
    {
        var declared = document.Descendants("Configurations").ToArray();
        var active = new BuildConfiguration(
            ConfigurationMapping.Preferred(Names(declared, "BuildType"), "Debug"),
            ConfigurationMapping.PreferredPlatform(Names(declared, "Platform")));

        return new(active, [.. document.Descendants("Project").Select(project => Mapped(project, active)).OfType<ProjectMapping>()]);
    }

    private static string[] Names(XElement[] declared, string kind) =>
        [.. declared.Elements(kind).Select(element => element.Attribute("Name")?.Value).OfType<string>()];

    private static string ProjectPlatform(string solutionPlatform) =>
        BuildConfiguration.IsAnyCpu(solutionPlatform) || solutionPlatform.Equals("Mixed Platforms", StringComparison.OrdinalIgnoreCase)
            ? "AnyCPU"
            : solutionPlatform;

    private static ProjectMapping? Mapped(XElement project, BuildConfiguration active) =>
        project.Attribute("Path")?.Value is { Length: > 0 } path
            ? new(
                SolutionFile.Slashed(path),
                new(Override(project, "BuildType", active) ?? active.Configuration, Override(project, "Platform", active) ?? ProjectPlatform(active.Platform)))
            : null;

    private static string? Override(XElement project, string kind, BuildConfiguration active) =>
        project.Elements(kind).LastOrDefault(element => Applies(element.Attribute("Solution")?.Value, active))?.Attribute("Project")?.Value;

    private static bool Applies(string? pattern, BuildConfiguration active)
    {
        if (pattern is not { Length: > 0 })
            return true;

        var text = pattern.AsSpan();
        var bar = text.IndexOf('|');
        var configuration = bar < 0 ? text : text[..bar];
        var platform = bar < 0 ? "*".AsSpan() : text[(bar + 1)..];

        return Matches(configuration.Trim(), active.Configuration) && Matches(platform.Trim(), active.Platform);
    }

    private static bool Matches(ReadOnlySpan<char> pattern, string value) =>
        pattern is "*" || BuildConfiguration.SamePlatform(pattern, value);
}
