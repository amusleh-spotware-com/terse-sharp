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
        VisualStudioInstance[] asLocated = [.. candidates];

        if (asLocated.Length is 0)
            return null;

        var versions = new Version[asLocated.Length];

        for (var index = 0; index < asLocated.Length; index++)
            versions[index] = asLocated[index].Version;

        return asLocated[Preferred(versions, Environment.Version.Major)];
    }

    public static string? SdkPath { get; private set; }

    internal static int Preferred(ReadOnlySpan<Version> candidates, int runtimeMajor)
    {
        for (var index = 0; index < candidates.Length; index++)
        {
            if (candidates[index].Major == runtimeMajor)
                return index;
        }

        return 0;
    }
}
