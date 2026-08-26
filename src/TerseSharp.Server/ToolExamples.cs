using System.Collections.Frozen;

namespace TerseSharp.Server;

public static class ToolExamples
{
    private static readonly FrozenDictionary<string, string> Worked = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["razor_outline"] = "razor_outline path=\"Components/Card.razor\"",
        ["razor_component"] = "razor_component name=\"Badge\"",
        ["razor_find"] = "razor_find query=\"Card\" kind=\"component\"",
        ["razor_bindings"] = "razor_bindings path=\"Components/Card.razor\"",
        ["razor_codebehind"] = "razor_codebehind path=\"Components/Card.razor\"",
        ["razor_validate"] = "razor_validate scope=\"solution\"",
        ["razor_set_attribute"] = "razor_set_attribute path=\"Components/Card.razor\" target=\"div/Badge\" attribute=\"Count\" value=\"0\" dryRun=true",
        ["razor_add_element"] = "razor_add_element path=\"Components/Card.razor\" parent=\"div\" markup=\"<Badge/>\" dryRun=true",
        ["razor_remove_element"] = "razor_remove_element path=\"Components/Card.razor\" target=\"div/button\" dryRun=true allowErrors=true",
        ["razor_set_directive"] = "razor_set_directive path=\"Components/Card.razor\" directive=\"using\" value=\"System.Text\" dryRun=true",
        ["package_add"] = "package_add project=\"src/App/App.csproj\" package=\"Serilog\" version=\"4.2.0\"",
        ["package_remove"] = "package_remove project=\"src/App/App.csproj\" package=\"Serilog\"",
        ["find_files"] = "find_files glob=\"src/**/*.{cs,csproj}\"",
        ["search_text"] = "search_text query=\"OrderService\" glob=\"src/**/*.cs\"",
        ["search_regex"] = "search_regex query=\"class \\\\w+Service\" glob=\"src/**/*.cs\"",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Tools => Worked.Keys;

    public static string For(string tool) => Worked.TryGetValue(tool, out var example) ? example : string.Empty;

    public static string Suffix(string? tool) =>
        tool is { Length: > 0 } name && Worked.TryGetValue(name, out var example)
            ? "; example: " + example
            : string.Empty;
}
