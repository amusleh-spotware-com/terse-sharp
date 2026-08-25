using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class TestScope
{
    private static readonly string[] Frameworks = ["xunit", "nunit", "mstest", "Microsoft.TestPlatform", "Microsoft.VisualStudio.TestPlatform"];

    public static string Of(Project project) => IsTest(project) ? "test" : "src";

    private static bool IsTest(Project project) => project.MetadataReferences
        .Select(reference => reference.Display)
        .OfType<string>()
        .Select(Path.GetFileNameWithoutExtension)
        .Any(assembly => Frameworks.Any(framework =>
            assembly?.Contains(framework, StringComparison.OrdinalIgnoreCase) is true));

    public static string Of(string root, Document document) =>
            GeneratedCode.IsGenerated(root, document.FilePath) ? "gen" : Of(document.Project);

    public static ImmutableArray<string> TestProjectsOf(Solution solution, bool allowDirect)
    {
        var found = ImmutableArray.CreateBuilder<string>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not { Length: > 0 } path || !IsTest(project))
                continue;

            if (seen.TryGetValue(path, out var index))
            {
                found[index] = path;
            }
            else
            {
                seen[path] = found.Count;
                found.Add(allowDirect ? DirectlyRunnable(project) ?? path : path);
            }
        }

        return found.DrainToImmutable();
    }

    private const string InProcessRunner = "xunit.v3.runner.inproc.console";

    private static string? DirectlyRunnable(Project project) =>
        References(project, InProcessRunner)
        && project.OutputFilePath is { Length: > 0 } assembly
        && File.Exists(assembly)
            ? assembly
            : null;


    private static bool References(Project project, string assemblyName) => project.MetadataReferences
        .Select(reference => reference.Display)
        .OfType<string>()
        .Any(display => Path.GetFileNameWithoutExtension(display.AsSpan()).Equals(assemblyName, StringComparison.OrdinalIgnoreCase));
}
