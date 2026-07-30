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

        var instance = MSBuildLocator.QueryVisualStudioInstances()
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault();

        if (instance is null)
            return "no-msbuild-found";

        MSBuildLocator.RegisterInstance(instance);

        return string.Create(CultureInfo.InvariantCulture, $"{instance.Name} {instance.Version} at {instance.MSBuildPath}");
    }
}
