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
}
