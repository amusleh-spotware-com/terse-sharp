using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TerseSharp.Core;

public sealed record SeverityRemap(
    string ProjectPath,
    ReportDiagnostic General,
    ImmutableDictionary<string, ReportDiagnostic> Specific,
    ImmutableArray<string> Loaded);

public static class SeverityRemaps
{
    public static readonly string[] Names = ["TreatWarningsAsErrors", "WarningsAsErrors", "WarningsNotAsErrors", "NoWarn"];

    public static SeverityRemap? Between(string projectPath, string[] loaded, string[]? mapped)
    {
        if (mapped is not { Length: 4 } || loaded is not { Length: 4 })
            return null;

        var directory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var before = Parsed(loaded, directory);
        var after = Parsed(mapped, directory);

        return new SeverityRemap(projectPath, after.GeneralDiagnosticOption, after.SpecificDiagnosticOptions, [.. before.SpecificDiagnosticOptions.Keys]);
    }

    public static Solution Rebound(Solution solution, IReadOnlyList<SeverityRemap> remaps)
    {
        if (remaps.Count is 0)
            return solution;

        foreach (var project in solution.Projects)
        {
            if (project.CompilationOptions is CSharpCompilationOptions options && Find(remaps, project.FilePath) is { } remap)
                solution = solution.WithProjectCompilationOptions(project.Id, Remapped(options, remap));
        }

        return solution;
    }

    private static CSharpCompilationOptions Remapped(CSharpCompilationOptions options, SeverityRemap remap) => options
        .WithGeneralDiagnosticOption(remap.General)
        .WithSpecificDiagnosticOptions(options.SpecificDiagnosticOptions.RemoveRange(remap.Loaded).SetItems(remap.Specific));

    private static SeverityRemap? Find(IReadOnlyList<SeverityRemap> remaps, string? projectPath)
    {
        foreach (var remap in remaps)
        {
            if (string.Equals(remap.ProjectPath, projectPath, PathBoundary.Comparison))
                return remap;
        }

        return null;
    }

    private static CSharpCompilationOptions Parsed(string[] values, string directory) =>
        CSharpCommandLineParser.Default.Parse(Arguments(values), directory, sdkDirectory: null).CompilationOptions;

    private static string[] Arguments(string[] values) =>
    [
        bool.TryParse(values[0].Trim(), out var all) && all ? "/warnaserror+" : "/warnaserror-",
        .. Listed("/warnaserror+:", values[1]),
        .. Listed("/warnaserror-:", values[2]),
        .. Listed("/nowarn:", values[3]),
    ];

    private static string[] Listed(string option, string ids) =>
        ids.AsSpan().Trim().IsEmpty ? [] : [option + ids.Replace(';', ',')];
}
