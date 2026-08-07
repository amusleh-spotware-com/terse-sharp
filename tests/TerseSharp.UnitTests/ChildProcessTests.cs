using System.Collections;
using TerseSharp.Core;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ChildProcessTests
{
    private static readonly string[] LocatorVariables =
        ["MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath"];

    [Fact]
    public void StartInfo_ForEveryVariableTheLocatorRegistered_KeepsItOutOfTheChild()
    {
        var registered = Registered();

        var start = ChildProcess.StartInfo("dotnet", ["--version"], AppContext.BaseDirectory);

        Assert.True(registered.Length > 0, "MSBuildLocator registered none of its variables, so this test proves nothing");
        Assert.All(registered, variable => Assert.False(start.Environment.ContainsKey(variable)));
    }

    [Fact]
    public void StartInfo_ForEveryOtherVariable_LeavesItInherited()
    {
        var inherited = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<string>()
            .Count(name => !LocatorVariables.Contains(name, StringComparer.OrdinalIgnoreCase));

        var start = ChildProcess.StartInfo("dotnet", [], AppContext.BaseDirectory);

        Assert.Equal(inherited, start.Environment.Count);
    }

    [Fact]
    public void StartInfo_PassesEveryArgumentThroughTheArgumentList()
    {
        var start = ChildProcess.StartInfo("dotnet", ["build", "-p:Name=Value"], AppContext.BaseDirectory);

        Assert.Equal(["build", "-p:Name=Value"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void RegisteredMsBuildVariables_CoversEveryMsBuildVariableThatPointsAtADirectory()
    {
        var registered = Registered();
        var uncovered = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => ((string)entry.Key).StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Value is string value && value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(entry => (string)entry.Key)
            .Where(name => !LocatorVariables.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(registered.Length > 0, "MSBuildLocator registered none of its variables, so this test proves nothing");
        Assert.True(
            uncovered.Length is 0,
            "these MSBuild variables name a directory and reach every child unscrubbed: " + string.Join(", ", uncovered));
    }

    private static string[] Registered()
    {
        MsBuildBootstrap.Ensure();

        return [.. LocatorVariables.Where(variable => Environment.GetEnvironmentVariable(variable) is { Length: > 0 })];
    }
}
