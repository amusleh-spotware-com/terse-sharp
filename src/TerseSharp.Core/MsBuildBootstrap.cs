using Microsoft.Build.Locator;

namespace TerseSharp.Core;

public static class MsBuildBootstrap
{
    private static readonly Lock Gate = new();

    private static bool registered;

    public static string Ensure()
    {
        lock (Gate)
        {
            if (registered)
                return MSBuildLocator.IsRegistered ? "already-registered" : "unavailable";

            registered = true;

            return Register();
        }
    }

    private static string Register()
    {
        if (MSBuildLocator.IsRegistered)
            return "already-registered";

        var instance = Best(MSBuildLocator.QueryVisualStudioInstances());

        if (instance is null)
            return "no-msbuild-found";

        MSBuildLocator.RegisterInstance(instance);
        SdkPath = instance.MSBuildPath;

        return string.Create(CultureInfo.InvariantCulture, $"{instance.Name} {instance.Version} at {instance.MSBuildPath}");
    }

    private static VisualStudioInstance? Best(IEnumerable<VisualStudioInstance> candidates)
    {
        var ordered = candidates.OrderByDescending(candidate => candidate.Version).ToArray();

        return Array.Find(ordered, candidate => candidate.Version.Major == Environment.Version.Major)
            ?? ordered.FirstOrDefault();
    }

    public static string? SdkPath { get; private set; }
}
